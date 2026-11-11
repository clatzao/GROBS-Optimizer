using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace Grobs
{
    /// <summary>
    /// Leitura completa do PC em duas fases.
    /// Fase 1 (só leitura): hardware e uso, processos, inicialização, serviços de terceiros, saúde do disco
    /// e o que ocupa cada disco escolhido, com o tamanho REAL no disco (arquivos esparsos, compactados e da nuvem).
    /// Fase 2: apaga só os itens que o usuário marcar, confere de novo antes e depois e mede o espaço livre.
    /// </summary>
    internal static class FullScan
    {
        // ------------------------------------------------------------ árvore de pastas
        sealed class Node
        {
            public string Name; public Node Parent; public long Size, Own; public int Files; public long Modified;
            public List<Node> Dirs = new List<Node>();
            public string FullPath() { var parts = new List<string>(); for (var n = this; n != null; n = n.Parent) parts.Add(n.Name); parts.Reverse(); return Path.Combine(parts.ToArray()); }
        }

        sealed class Item
        {
            public string Id, Cat, Title, Why, Risk, Kind, Path, Note; public long Size; public DateTime? Modified;
            public bool Deletable = true, NeedsAdmin; public string[] Procs = new string[0];
        }

        const long Big = 500L << 20;
        static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static string Downloads => Path.Combine(Home, "Downloads");

        static volatile bool _busy, _deleting;
        static volatile string _phase = "";
        static double _progress;
        static string _error;
        static CancellationTokenSource _cts;
        static Dictionary<string, object> _report;
        static List<Item> _items = new List<Item>();
        static readonly Dictionary<string, Node> _trees = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        static Dictionary<string, object> _lastDelete;
        static long _scanned, _toScan; static int _denied;

        // ------------------------------------------------------------ RPC
        public static object Drives() => DriveInfo.GetDrives().Where(d => { try { return d.DriveType == DriveType.Fixed && d.IsReady; } catch { return false; } })
            .Select(d => (object)new Dictionary<string, object> {
                { "root", d.Name }, { "label", SafeLabel(d) }, { "total", (double)d.TotalSize }, { "free", (double)d.AvailableFreeSpace },
                { "system", string.Equals(d.Name, Path.GetPathRoot(Win), StringComparison.OrdinalIgnoreCase) } }).ToList();

        static string SafeLabel(DriveInfo d) { try { return d.VolumeLabel; } catch { return ""; } }

        public static object Status() => new Dictionary<string, object> {
            { "running", _busy }, { "deleting", _deleting }, { "phase", _phase }, { "progress", Math.Round(_progress, 1) }, { "error", _error },
            { "report", _report }, { "items", _report == null ? null : _items.Select(ToUi).ToList() }, { "lastDelete", _lastDelete }, { "admin", Program.IsAdmin } };

        public static object Cancel() { _cts?.Cancel(); return true; }

        public static object Start(string[] roots)
        {
            if (_busy || _deleting) return Status();
            var valid = (roots ?? new string[0]).Where(r => Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (valid.Length == 0) throw new ArgumentException("Escolha pelo menos um disco.");
            _busy = true; _error = null; _progress = 0; _lastDelete = null;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            new Thread(() =>
            {
                try { Run(valid, ct); }
                catch (OperationCanceledException) { _error = "Leitura cancelada."; }
                catch (Exception ex) { _error = ex.Message; Program.Log(ex); }
                _busy = false; _phase = "";
                Notify();
            }, 64 << 20) { IsBackground = true, Name = "FullScan", Priority = ThreadPriority.BelowNormal }.Start();
            return Status();
        }

        static void Notify() => Program.Window?.Notify("fullScan", Status());
        static DateTime _lastNotify;
        static void Phase(string p, double progress)
        {
            _phase = p; _progress = progress;
            if ((DateTime.UtcNow - _lastNotify).TotalMilliseconds > 400) { _lastNotify = DateTime.UtcNow; Notify(); }
        }

        static object ToUi(Item i) => new Dictionary<string, object> {
            { "id", i.Id }, { "cat", i.Cat }, { "title", i.Title }, { "why", i.Why }, { "risk", i.Risk }, { "kind", i.Kind }, { "path", i.Path ?? "" },
            { "size", (double)i.Size }, { "modified", i.Modified?.ToString("dd/MM/yyyy") }, { "deletable", i.Deletable }, { "needsAdmin", i.NeedsAdmin },
            { "inUse", RunningOf(i).Count > 0 ? string.Join(", ", RunningOf(i)) : null }, { "note", i.Note } };

        // ------------------------------------------------------------ fase 1
        static void Run(string[] roots, CancellationToken ct)
        {
            var report = new Dictionary<string, object>();
            var notChecked = new List<string>();
            var items = new List<Item>();
            int n = 0;
            Func<string> id = () => "i" + (++n);

            // 1. hardware e uso: medido ANTES da varredura, que distorce o uso do disco
            Phase("Medindo CPU, memória e disco (antes da varredura)", 1);
            report["system"] = SystemNow();

            Phase("Processos", 3);
            report["processes"] = ProcessesNow();

            Phase("Programas que abrem com o Windows", 5);
            report["startup"] = StartupNow();

            Phase("Serviços de terceiros", 7);
            report["services"] = ServicesNow();

            Phase("Saúde dos discos", 9);
            report["health"] = HealthNow(notChecked);

            // 6. armazenamento
            _trees.Clear(); _scanned = 0; _denied = 0;
            _toScan = roots.Sum(r => { try { var d = new DriveInfo(r); return d.TotalSize - d.TotalFreeSpace; } catch { return 0L; } });
            var bigFiles = new List<Tuple<string, long, DateTime>>();
            var projects = new List<Tuple<string, string>>(); // caminho, tipo
            foreach (var root in roots)
            {
                ct.ThrowIfCancellationRequested();
                var node = new Node { Name = root };
                _trees[root] = node;
                Walk(node, root, bigFiles, projects, false, ct);
            }
            if (_denied > 0) notChecked.Add(_denied + " pasta(s) sem permissão de leitura não entraram na conta" + (Program.IsAdmin ? "." : " (abra o GROBS como administrador para ler mais)."));

            Phase("Montando as categorias", 92);
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses()) using (p) running.Add(p.ProcessName);

            // Lixeira de cada disco
            foreach (var root in roots)
            {
                var bin = new Native.SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO)) };
                if (Native.SHQueryRecycleBin(root, ref bin) == 0 && bin.i64Size > 0)
                    items.Add(new Item { Id = id(), Cat = "lixeira", Title = "Lixeira (" + root.TrimEnd('\\') + ")", Kind = "recycle", Path = root, Size = bin.i64Size, Risk = "moderado",
                        Why = bin.i64NumItems + " item(ns) que você já apagou. Depois de esvaziar não dá para recuperar. Confira antes se não há nada que você queira." });
            }

            // temporários, caches do Windows e de navegadores (regras da Limpeza: só o que se recria)
            try
            {
                foreach (Dictionary<string, object> c in (List<object>)Cleaner.Scan(ct))
                {
                    string cid = (string)c["id"]; long size = (long)Convert.ToDouble(c["size"]);
                    if (cid == "recycle" || size < (1L << 20)) continue;
                    string cat = cid == "usertemp" || cid == "wintemp" ? "temp" : cid == "wupdate" || cid == "delivery" ? "update" : (string)c["group"] == "Navegadores" ? "browser" : "caches";
                    bool locked = Convert.ToBoolean(c["locked"]);
                    items.Add(new Item { Id = id(), Cat = cat, Title = (string)c["name"], Kind = "clean", Path = cid, Size = size, Risk = "tranquilo", NeedsAdmin = Convert.ToBoolean(c["admin"]), Deletable = !locked,
                        Why = c["desc"] + ". O programa recria sozinho; o que estiver em uso fica." + (locked ? " Precisa abrir o GROBS como administrador." : ""),
                        Note = Convert.ToBoolean(c["running"]) ? "Programa aberto: feche antes para limpar mais." : null });
                }
            }
            catch (Exception ex) { Program.Log(ex); }

            // caches de ferramentas de programação, IA e emuladores
            foreach (var r in DiskClean.Rules())
            {
                if (!Directory.Exists(r.Path)) continue;
                long size = SizeAny(r.Path);
                if (size < (20L << 20)) continue;
                bool heavy = r.Id == "ollama" || r.Id == "hf" || r.Id == "avd" || r.Id == "playwright";
                items.Add(new Item { Id = id(), Cat = heavy ? "ai" : "dev", Title = r.Title, Why = r.Why, Kind = "contents", Path = r.Path, Size = size, Procs = r.Procs ?? new string[0],
                    Risk = heavy ? "perigoso" : r.Risk == "safe" ? "tranquilo" : "moderado", Modified = Directory.GetLastWriteTime(r.Path) });
            }
            // npm: só as partes que o txt manda (o resto do npm-cache tem configurações)
            string npm = Path.Combine(Local, "npm-cache");
            items.RemoveAll(i => i.Path == npm);
            foreach (var sub in new[] { "_cacache", "_npx", "_prebuilds" })
            {
                string p = Path.Combine(npm, sub);
                if (!Directory.Exists(p)) continue;
                long size = SizeAny(p);
                if (size >= (20L << 20)) items.Add(new Item { Id = id(), Cat = "dev", Title = "Cache do npm (" + sub + ")", Why = "Pacotes já baixados. O npm baixa de novo quando precisar.", Kind = "folder", Path = p, Size = size, Procs = new[] { "node", "npm" }, Risk = "tranquilo" });
            }
            // Android SDK, Docker e WSL: só informação (apagar quebra as ferramentas)
            foreach (var t in new[] {
                Tuple.Create(Path.Combine(Local, @"Android\Sdk"), "Android SDK", "Ferramentas do Android Studio. Remova versões antigas pelo SDK Manager do próprio Android Studio."),
                Tuple.Create(Path.Combine(Local, "Docker"), "Docker Desktop", "Imagens e contêineres. Para liberar, use \"docker system prune\" ou o próprio Docker Desktop."),
                Tuple.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DockerDesktop"), "Docker Desktop (dados do sistema)", "Use o próprio Docker Desktop para limpar."),
                Tuple.Create(Path.Combine(Home, ".cache"), "Pasta .cache", "Caches de várias ferramentas (Python, IA e outras). Veja os arquivos antes de apagar.") })
            {
                if (!Directory.Exists(t.Item1)) continue;
                long size = SizeAny(t.Item1);
                if (size < (100L << 20)) continue;
                bool info = !t.Item2.StartsWith("Pasta");
                items.Add(new Item { Id = id(), Cat = "ai", Title = t.Item2, Why = t.Item3, Kind = info ? "info" : "contents", Path = t.Item1, Size = size, Risk = info ? "protegido" : "perigoso", Deletable = !info });
            }
            foreach (var pkg in SafeDirs(Path.Combine(Local, "Packages")).Where(d => Path.GetFileName(d).StartsWith("CanonicalGroup", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(d).Contains("Linux")))
            {
                long size = SizeAny(pkg);
                if (size >= (500L << 20)) items.Add(new Item { Id = id(), Cat = "ai", Title = "Linux do WSL (" + Path.GetFileName(pkg).Split('_')[0] + ")", Kind = "info", Path = pkg, Size = size, Risk = "protegido", Deletable = false,
                    Why = "Disco virtual do Linux. Apagar destrói tudo o que está nele. Para remover, use \"wsl --unregister\"." });
            }

            // projetos: node_modules (só o de cima) e target do Rust
            foreach (var pr in projects.Where(p => IsUserProject(p.Item1, p.Item2)))
            {
                long size = SizeAny(pr.Item1);
                if (size < (50L << 20)) continue;
                string proj = Path.GetDirectoryName(pr.Item1);
                int days = (int)(DateTime.Now - Touched(proj)).TotalDays;
                bool node = pr.Item2 == "node";
                items.Add(new Item { Id = id(), Cat = "proj", Title = (node ? "node_modules de " : "target (Rust) de ") + Path.GetFileName(proj), Kind = "folder", Path = pr.Item1, Size = size,
                    Procs = node ? new[] { "node", "npm" } : new[] { "cargo", "rustc", "rust-analyzer" }, Modified = Touched(proj), Risk = days < 7 ? "moderado" : "tranquilo",
                    Why = (node ? "Dependências do projeto. Se voltar a usar, rode npm install; a próxima instalação vai demorar mais." : "Arquivos de compilação (equivale a cargo clean). A próxima compilação vai demorar mais.")
                        + (days < 7 ? " O projeto foi mexido há " + days + " dia(s)." : " Projeto parado há " + days + " dias.") });
            }

            // Windows.old
            string old = Path.Combine(Path.GetPathRoot(Win), "Windows.old");
            if (Directory.Exists(old))
                items.Add(new Item { Id = id(), Cat = "update", Title = "Windows antigo (Windows.old)", Kind = "winold", Path = old, Size = SizeAny(old), NeedsAdmin = true, Risk = "moderado", Deletable = Program.IsAdmin,
                    Why = "Cópia da versão anterior do Windows. Sem ela não dá para voltar à versão anterior." });

            // instaladores esquecidos em Downloads
            var bigSet = new HashSet<string>(bigFiles.Select(b => b.Item1), StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(Downloads))
                foreach (var f in SafeFiles(Downloads))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (!new[] { ".exe", ".msi", ".zip", ".rar", ".7z", ".iso", ".msix", ".appx" }.Contains(ext)) continue;
                    var fi = new FileInfo(f); long size = RealSize(f, fi.Length, (uint)fi.Attributes);
                    if (size < (5L << 20)) continue;
                    bigSet.Remove(f);
                    items.Add(new Item { Id = id(), Cat = "downloads", Title = fi.Name, Kind = "file", Path = f, Size = size, Modified = fi.LastWriteTime, Risk = "moderado",
                        Why = "Instalador ou arquivo compactado baixado em " + fi.LastWriteTime.ToString("dd/MM/yyyy") + ". Se o programa já está instalado, pode sair." });
                }

            // arquivos grandes
            string sysRoot = Path.GetPathRoot(Win);
            var sysFiles = new[] { "hiberfil.sys", "pagefile.sys", "swapfile.sys" }.Select(x => Path.Combine(sysRoot, x)).ToList();
            var covered = items.Where(i => (i.Kind == "contents" || i.Kind == "folder" || i.Kind == "info" || i.Kind == "winold") && !string.IsNullOrEmpty(i.Path) && i.Cat != "personal").Select(i => i.Path).ToList();
            foreach (var b in bigFiles.Where(b => bigSet.Contains(b.Item1) && !sysFiles.Contains(b.Item1, StringComparer.OrdinalIgnoreCase) && !Under(b.Item1, Win) && !covered.Any(c => Under(b.Item1, c))).OrderByDescending(b => b.Item2))
            {
                string p = b.Item1, risk, why; bool del = true;
                string ext = Path.GetExtension(p).ToLowerInvariant();
                bool vm = new[] { ".vhdx", ".vhd", ".vmdk", ".vdi", ".img", ".qcow2", ".avhdx" }.Contains(ext);
                bool model = new[] { ".gguf", ".safetensors", ".ckpt", ".bin", ".pt", ".onnx" }.Contains(ext) || p.IndexOf(@"\models\",StringComparison.OrdinalIgnoreCase) >= 0;
                if (Under(p, Win)) { risk = "protegido"; del = false; why = "Arquivo do Windows. Não apague."; }
                else if (vm) { risk = "perigoso"; why = "Disco virtual de emulador ou máquina virtual" + (p.IndexOf("Google Play Games", StringComparison.OrdinalIgnoreCase) >= 0 ? " (Google Play Games)" : "") + ". Apagar perde tudo o que está nele. O tamanho mostrado é o real no disco."; }
                else if (model) { risk = "perigoso"; why = "Pode ser um modelo de IA ou dado de programa. Baixar de novo pode demorar muito."; }
                else if (Under(p, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) || Under(p, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)) || Under(p, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)) || Under(p, Local) || Under(p, Roaming))
                { risk = "perigoso"; why = "Faz parte de um programa instalado. Apagar pode quebrar o programa; prefira desinstalar o programa."; }
                else { risk = "moderado"; why = "Arquivo seu. Apagar é definitivo: confira se não precisa dele ou se tem cópia."; }
                items.Add(new Item { Id = id(), Cat = "big", Title = Path.GetFileName(p), Kind = "file", Path = p, Size = b.Item2, Modified = b.Item3, Risk = risk, Why = why, Deletable = del });
            }

            // sistema: nunca apagar à mão
            if (roots.Any(r => Under(sysRoot, r)))
            {
                Func<string, long> sz = p => { var nd = Find(p); return nd != null ? nd.Size : (Directory.Exists(p) ? SizeAny(p) : 0); };
                long winsxs = sz(Path.Combine(Win, "WinSxS"));
                if (winsxs > 0) items.Add(new Item { Id = id(), Cat = "system", Title = "Componentes do Windows (WinSxS)", Kind = "dism", Path = Path.Combine(Win, "WinSxS"), Size = winsxs, Risk = "tranquilo", NeedsAdmin = true, Deletable = Program.IsAdmin,
                    Why = "Nunca apague à mão. A limpeza segura usa o DISM (StartComponentCleanup), que remove só versões antigas. Parte do tamanho é compartilhada com o Windows; o ganho real costuma ser menor.", Note = "Pode levar de 5 a 20 minutos." });
                long inst = sz(Path.Combine(Win, "Installer"));
                if (inst > 0) items.Add(new Item { Id = id(), Cat = "system", Title = "Instaladores do Windows (Windows\\Installer)", Kind = "info", Path = Path.Combine(Win, "Installer"), Size = inst, Risk = "protegido", Deletable = false,
                    Why = "Usados para atualizar, reparar e desinstalar programas. Apagar quebra desinstalações." });
                foreach (var f in sysFiles)
                {
                    long size = FindSize(f);
                    if (size <= 0) continue;
                    string nm = Path.GetFileName(f).ToLowerInvariant();
                    if (nm == "hiberfil.sys")
                        items.Add(new Item { Id = id(), Cat = "system", Title = "Arquivo de hibernação (hiberfil.sys)", Kind = "hibernate", Path = f, Size = size, NeedsAdmin = true, Deletable = Program.IsAdmin, Risk = "moderado",
                            Why = "Some ao desligar a hibernação (powercfg /h off). A opção \"Hibernar\" e a Inicialização Rápida deixam de existir. Não recomendado em notebook que você hiberna." });
                    else
                        items.Add(new Item { Id = id(), Cat = "system", Title = nm == "pagefile.sys" ? "Memória virtual (pagefile.sys)" : "Arquivo de troca de apps (swapfile.sys)", Kind = "info", Path = f, Size = size, Risk = "protegido", Deletable = false,
                            Why = "O Windows usa como extensão da memória RAM. Não apague." });
                }
            }

            // suas pastas: só informação, os arquivos grandes delas já aparecem acima
            foreach (var t in new[] { Tuple.Create(Downloads, "Downloads"), Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Área de Trabalho"),
                Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Vídeos"), Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documentos"),
                Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Imagens"), Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Músicas") })
            {
                var nd = Find(t.Item1);
                if (nd == null || nd.Size < (100L << 20)) continue;
                items.Add(new Item { Id = id(), Cat = "personal", Title = t.Item2, Kind = "info", Path = t.Item1, Size = nd.Size, Risk = "protegido", Deletable = false, Modified = FromFt(nd.Modified),
                    Why = nd.Files.ToString("N0") + " arquivo(s) direto na pasta. São seus: abra a lista e apague só o que quiser pelo Explorador." });
            }

            report["folders"] = TopFolders();
            report["specific"] = SpecificFolders();
            report["drives"] = Drives();
            report["date"] = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            report["roots"] = roots;
            report["notChecked"] = notChecked;

            _items = items;
            report["causes"] = Causes(report, items);
            report["advice"] = Advice(items);
            _report = report;
            _progress = 100;
        }

        /// <summary>Projeto do usuário: fora de pastas de programas e do sistema, com package.json (ou Cargo.toml) ao lado.</summary>
        static bool IsUserProject(string path, string type)
        {
            string proj = Path.GetDirectoryName(path);
            var deny = new[] { Win, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Local, Roaming };
            if (deny.Any(d => Under(path, d))) return false;
            // pastas ocultas da pasta do usuário (.cache, .vscode, .nuget...) são de ferramentas, não projetos
            if (Under(path, Home) && path.Substring(Home.Length).TrimStart('\\').StartsWith(".")) return false;
            return File.Exists(Path.Combine(proj, type == "node" ? "package.json" : "Cargo.toml"));
        }

        static bool Under(string path, string dir) => !string.IsNullOrEmpty(dir) && (path.Equals(dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
        static DateTime? FromFt(long ft) { try { return ft > 0 ? DateTime.FromFileTime(ft) : (DateTime?)null; } catch { return null; } }
        static IEnumerable<string> SafeDirs(string d) { try { return Directory.Exists(d) ? Directory.GetDirectories(d) : new string[0]; } catch { return new string[0]; } }
        static IEnumerable<string> SafeFiles(string d) { try { return Directory.GetFiles(d); } catch { return new string[0]; } }

        static DateTime Touched(string dir)
        {
            DateTime t = DateTime.MinValue;
            try { foreach (var f in Directory.EnumerateFileSystemEntries(dir)) { string nm = Path.GetFileName(f); if (nm == "node_modules" || nm == "target") continue; var x = File.GetLastWriteTime(f); if (x > t) t = x; } } catch { }
            return t == DateTime.MinValue ? Directory.GetLastWriteTime(dir) : t;
        }

        // ------------------------------------------------------------ varredura com o tamanho real
        const uint ATTR_DIR = 0x10, ATTR_REPARSE = 0x400, ATTR_SPARSE = 0x200, ATTR_COMPRESSED = 0x800, ATTR_OFFLINE = 0x1000, ATTR_RECALL_OPEN = 0x40000, ATTR_RECALL_ACCESS = 0x400000;
        const uint TAG_MOUNT = 0xA0000003, TAG_SYMLINK = 0xA000000C;

        /// <summary>Esparsos (discos virtuais de emuladores), compactados e arquivos da nuvem ocupam menos do que o tamanho que mostram.</summary>
        static long RealSize(string path, long logical, uint attrs)
        {
            if ((attrs & (ATTR_SPARSE | ATTR_COMPRESSED | ATTR_OFFLINE | ATTR_RECALL_OPEN | ATTR_RECALL_ACCESS | ATTR_REPARSE)) == 0) return logical;
            uint hi; uint lo = Native.GetCompressedFileSize(@"\\?\" + path, out hi);
            if (lo == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0) return logical;
            return ((long)hi << 32) | lo;
        }

        static void Walk(Node node, string dir, List<Tuple<string, long, DateTime>> big, List<Tuple<string, string>> projects, bool insideModules, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException();
            Native.WIN32_FIND_DATA fd;
            IntPtr h = Native.FindFirstFileEx(@"\\?\" + dir.TrimEnd('\\') + @"\*", 1, out fd, 0, IntPtr.Zero, 2);
            if (h == Native.INVALID_HANDLE) { _denied++; return; }
            var subs = new List<string>(); bool cargo = false;
            try
            {
                do
                {
                    string name = fd.cFileName;
                    if ((fd.dwFileAttributes & ATTR_DIR) != 0)
                    {
                        if (name == "." || name == "..") continue;
                        // junções e links simbólicos: não segue, para não contar a mesma pasta duas vezes
                        if ((fd.dwFileAttributes & ATTR_REPARSE) != 0 && (fd.dwReserved0 == TAG_MOUNT || fd.dwReserved0 == TAG_SYMLINK)) continue;
                        subs.Add(name);
                    }
                    else
                    {
                        if (name.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)) cargo = true;
                        if ((fd.dwFileAttributes & ATTR_REPARSE) != 0 && fd.dwReserved0 == TAG_SYMLINK) continue;
                        long logical = ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
                        string full = Path.Combine(dir, name);
                        long real = RealSize(full, logical, fd.dwFileAttributes);
                        long ft = ((long)fd.ftWriteHigh << 32) | fd.ftWriteLow;
                        node.Own += real; node.Files++;
                        if (ft > node.Modified) node.Modified = ft;
                        _scanned += real;
                        if (real >= Big) big.Add(Tuple.Create(full, real, FromFt(ft) ?? DateTime.MinValue));
                    }
                } while (Native.FindNextFile(h, out fd));
            }
            finally { Native.FindClose(h); }
            node.Size = node.Own;
            if (_toScan > 0) Phase("Lendo " + dir, 10 + Math.Min(80, 80.0 * _scanned / _toScan));
            foreach (var s in subs)
            {
                string full = Path.Combine(dir, s);
                bool modules = s.Equals("node_modules", StringComparison.OrdinalIgnoreCase);
                if (modules && !insideModules) projects.Add(Tuple.Create(full, "node"));
                else if (cargo && s.Equals("target", StringComparison.OrdinalIgnoreCase)) projects.Add(Tuple.Create(full, "rust"));
                var child = new Node { Name = s, Parent = node };
                Walk(child, full, big, projects, insideModules || modules, ct);
                node.Size += child.Size;
                if (child.Modified > node.Modified) node.Modified = child.Modified;
                node.Dirs.Add(child);
            }
            // pastas pequenas demais para o relatório não guardam filhos (economiza memória)
            if (node.Size < (8L << 20)) node.Dirs = new List<Node>();
        }

        static long SizeAny(string path)
        {
            var nd = Find(path);
            if (nd != null) return nd.Size;
            long total = 0;
            WalkSize(path, ref total);
            return total;
        }

        static void WalkSize(string dir, ref long total)
        {
            Native.WIN32_FIND_DATA fd;
            IntPtr h = Native.FindFirstFileEx(@"\\?\" + dir.TrimEnd('\\') + @"\*", 1, out fd, 0, IntPtr.Zero, 2);
            if (h == Native.INVALID_HANDLE) return;
            var subs = new List<string>();
            try
            {
                do
                {
                    if ((fd.dwFileAttributes & ATTR_DIR) != 0)
                    {
                        if (fd.cFileName == "." || fd.cFileName == "..") continue;
                        if ((fd.dwFileAttributes & ATTR_REPARSE) != 0 && (fd.dwReserved0 == TAG_MOUNT || fd.dwReserved0 == TAG_SYMLINK)) continue;
                        subs.Add(fd.cFileName);
                    }
                    else total += RealSize(Path.Combine(dir, fd.cFileName), ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow, fd.dwFileAttributes);
                } while (Native.FindNextFile(h, out fd));
            }
            finally { Native.FindClose(h); }
            foreach (var s in subs) WalkSize(Path.Combine(dir, s), ref total);
        }

        /// <summary>Tamanho de um arquivo do sistema como hiberfil.sys (como o "dir /a": Get-Item às vezes não enxerga).</summary>
        static long FindSize(string file)
        {
            Native.WIN32_FIND_DATA fd;
            IntPtr h = Native.FindFirstFileEx(@"\\?\" + file, 1, out fd, 0, IntPtr.Zero, 0);
            if (h == Native.INVALID_HANDLE) return -1;
            Native.FindClose(h);
            return ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
        }

        static Node Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            foreach (var t in _trees)
            {
                if (!Under(path, t.Key) && !path.Equals(t.Key, StringComparison.OrdinalIgnoreCase)) continue;
                var node = t.Value;
                foreach (var part in path.Substring(t.Key.Length).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    node = node.Dirs.FirstOrDefault(d => d.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                    if (node == null) return null;
                }
                return node;
            }
            return null;
        }

        static object NodeUi(Node n) => new Dictionary<string, object> { { "name", n.Name }, { "path", n.FullPath() }, { "size", (double)n.Size }, { "files", n.Files }, { "modified", FromFt(n.Modified)?.ToString("dd/MM/yyyy") }, { "dirs", n.Dirs.Count } };

        static object TopFolders()
        {
            var list = new List<object>();
            var places = new List<Tuple<string, string>>();
            foreach (var root in _trees.Keys) places.Add(Tuple.Create(root, "Disco " + root.TrimEnd('\\')));
            places.Add(Tuple.Create(Home, "Sua pasta de usuário"));
            places.Add(Tuple.Create(Local, "AppData\\Local"));
            places.Add(Tuple.Create(Roaming, "AppData\\Roaming"));
            places.Add(Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Program Files"));
            if (Environment.Is64BitOperatingSystem) places.Add(Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Program Files (x86)"));
            places.Add(Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProgramData"));
            places.Add(Tuple.Create(Win, "Windows"));
            foreach (var p in places.Where(p => !string.IsNullOrEmpty(p.Item1)))
            {
                var nd = Find(p.Item1);
                if (nd == null) continue;
                list.Add(new Dictionary<string, object> { { "title", p.Item2 }, { "path", nd.FullPath() }, { "size", (double)nd.Size },
                    { "top", nd.Dirs.OrderByDescending(d => d.Size).Take(15).Select(NodeUi).ToList() } });
            }
            return list;
        }

        static object SpecificFolders()
        {
            string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), sysRoot = Path.GetPathRoot(Win);
            var list = new[] {
                Tuple.Create(Path.GetTempPath().TrimEnd('\\'), "%TEMP%"), Tuple.Create(Path.Combine(Win, "Temp"), "Windows\\Temp"),
                Tuple.Create(Path.Combine(sysRoot, "$Recycle.Bin"), "Lixeira ($Recycle.Bin)"), Tuple.Create(Path.Combine(Win, @"SoftwareDistribution\Download"), "Windows Update (downloads)"),
                Tuple.Create(Path.Combine(Win, "Installer"), "Windows\\Installer"), Tuple.Create(Path.Combine(Win, "WinSxS"), "WinSxS"),
                Tuple.Create(Downloads, "Downloads"), Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Área de Trabalho"),
                Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Vídeos"), Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documentos"),
                Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Imagens"),
                Tuple.Create(Path.Combine(Local, @"Google\Chrome\User Data"), "Chrome (perfil e cache)"), Tuple.Create(Path.Combine(Local, @"Microsoft\Edge\User Data"), "Edge (perfil e cache)"),
                Tuple.Create(Path.Combine(Local, @"Mozilla\Firefox\Profiles"), "Firefox (cache)"), Tuple.Create(Path.Combine(Local, "npm-cache"), "npm-cache"),
                Tuple.Create(Path.Combine(Home, ".gradle"), ".gradle"), Tuple.Create(Path.Combine(Home, ".cache"), ".cache"), Tuple.Create(Path.Combine(Home, ".ollama"), ".ollama"),
                Tuple.Create(Path.Combine(Home, ".android"), ".android"), Tuple.Create(Path.Combine(Local, @"Android\Sdk"), "Android SDK"),
                Tuple.Create(Path.Combine(Local, "Docker"), "Docker"), Tuple.Create(Path.Combine(pd, "DockerDesktop"), "Docker (ProgramData)"),
                Tuple.Create(Path.Combine(sysRoot, "Windows.old"), "Windows.old") };
            var res = new List<object>();
            foreach (var t in list)
            {
                var nd = Find(t.Item1);
                long size = nd != null ? nd.Size : -1;
                if (nd == null && Directory.Exists(t.Item1) && !_trees.Keys.Any(k => Under(t.Item1, k))) continue; // disco que não foi lido
                if (nd == null && !Directory.Exists(t.Item1)) continue;
                if (nd == null) { long tot = 0; WalkSize(t.Item1, ref tot); size = tot; }
                res.Add(new Dictionary<string, object> { { "title", t.Item2 }, { "path", t.Item1 }, { "size", (double)size } });
            }
            return res;
        }

        /// <summary>Mapa visual: os filhos de uma pasta já lida, mais os maiores arquivos direto nela.</summary>
        public static object Children(string path)
        {
            var nd = string.IsNullOrEmpty(path) ? null : Find(path);
            if (nd == null)
                return new Dictionary<string, object> { { "roots", _trees.Values.Select(NodeUi).ToList() } };
            var files = new List<object>();
            try
            {
                foreach (var f in new DirectoryInfo(nd.FullPath()).EnumerateFiles().Select(f => new { f, real = SafeReal(f) }).OrderByDescending(x => x.real).Take(30))
                    files.Add(new Dictionary<string, object> { { "name", f.f.Name }, { "path", f.f.FullName }, { "size", (double)f.real }, { "modified", f.f.LastWriteTime.ToString("dd/MM/yyyy") } });
            }
            catch { }
            return new Dictionary<string, object> { { "node", NodeUi(nd) }, { "own", (double)nd.Own }, { "parent", nd.Parent?.FullPath() },
                { "dirs", nd.Dirs.OrderByDescending(d => d.Size).Take(60).Select(NodeUi).ToList() }, { "files", files } };
        }

        static long SafeReal(FileInfo f) { try { return RealSize(f.FullName, f.Length, (uint)f.Attributes); } catch { return 0; } }

        /// <summary>Os maiores arquivos dentro de um item (aberto pelo usuário na lista).</summary>
        public static object Files(string id)
        {
            var it = _items.FirstOrDefault(i => i.Id == id);
            if (it == null || string.IsNullOrEmpty(it.Path)) return new List<object>();
            if (File.Exists(it.Path))
            {
                var fi = new FileInfo(it.Path);
                return new List<object> { new Dictionary<string, object> { { "name", fi.Name }, { "path", fi.FullName }, { "size", (double)SafeReal(fi) }, { "modified", fi.LastWriteTime.ToString("dd/MM/yyyy HH:mm") } } };
            }
            if (!Directory.Exists(it.Path)) return new List<object>();
            var top = new List<Tuple<string, long, DateTime>>();
            int seen = 0;
            TopFiles(it.Path, top, ref seen, 0);
            return top.OrderByDescending(t => t.Item2).Take(40).Select(t => (object)new Dictionary<string, object> {
                { "name", Path.GetFileName(t.Item1) }, { "path", t.Item1 }, { "size", (double)t.Item2 }, { "modified", t.Item3.ToString("dd/MM/yyyy") } }).ToList();
        }

        static void TopFiles(string dir, List<Tuple<string, long, DateTime>> top, ref int seen, int depth)
        {
            if (depth > 40 || seen > 300000) return;
            try
            {
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles())
                {
                    seen++;
                    long s = SafeReal(f);
                    if (top.Count < 40 || s > top.Min(t => t.Item2))
                    {
                        top.Add(Tuple.Create(f.FullName, s, f.LastWriteTime));
                        if (top.Count > 60) { top.Sort((a, b) => b.Item2.CompareTo(a.Item2)); top.RemoveRange(40, top.Count - 40); }
                    }
                }
                foreach (var d in new DirectoryInfo(dir).EnumerateDirectories())
                    if ((d.Attributes & FileAttributes.ReparsePoint) == 0) TopFiles(d.FullName, top, ref seen, depth + 1);
            }
            catch { }
        }

        // ------------------------------------------------------------ sistema, processos, inicialização, serviços, saúde
        static object SystemNow()
        {
            var d = new Dictionary<string, object>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, LoadPercentage FROM Win32_Processor"))
                    foreach (ManagementObject o in s.Get())
                    {
                        d["cpu"] = Convert.ToString(o["Name"]).Trim(); d["cores"] = Convert.ToInt32(o["NumberOfCores"]); d["threads"] = Convert.ToInt32(o["NumberOfLogicalProcessors"]);
                        d["cpuLoad"] = Convert.ToInt32(o["LoadPercentage"] ?? 0); break;
                    }
                using (var s = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory, LastBootUpTime, Caption FROM Win32_OperatingSystem"))
                    foreach (ManagementObject o in s.Get())
                    {
                        double total = Convert.ToDouble(o["TotalVisibleMemorySize"]) * 1024, free = Convert.ToDouble(o["FreePhysicalMemory"]) * 1024;
                        d["ramTotal"] = total; d["ramFree"] = free; d["ramUsedPct"] = Math.Round((1 - free / total) * 100);
                        var boot = ManagementDateTimeConverter.ToDateTime(Convert.ToString(o["LastBootUpTime"]));
                        d["boot"] = boot.ToString("dd/MM/yyyy HH:mm"); d["uptimeHours"] = Math.Round((DateTime.Now - boot).TotalHours, 1);
                        d["os"] = Convert.ToString(o["Caption"]); break;
                    }
                using (var s = new ManagementObjectSearcher("SELECT PCSystemType, Model FROM Win32_ComputerSystem"))
                    foreach (ManagementObject o in s.Get()) { d["laptop"] = Convert.ToInt32(o["PCSystemType"] ?? 0) == 2; d["model"] = Convert.ToString(o["Model"]); break; }
                // amostra de 1 s do processador e do disco (classes formatadas: funcionam em Windows em português)
                using (var s = new ManagementObjectSearcher("SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'"))
                { s.Get(); Thread.Sleep(1000); foreach (ManagementObject o in s.Get()) d["cpuLoad"] = Convert.ToInt32(o["PercentProcessorTime"]); }
                using (var s = new ManagementObjectSearcher("SELECT PercentIdleTime FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'"))
                    foreach (ManagementObject o in s.Get()) d["diskBusy"] = Math.Max(0, 100 - Convert.ToInt32(o["PercentIdleTime"]));
            }
            catch (Exception ex) { Program.Log(ex); }
            return d;
        }

        static object ProcessesNow()
        {
            // uso de CPU agora: tempo de CPU de cada processo em 1 segundo
            var t0 = new Dictionary<int, double>();
            foreach (var p in Process.GetProcesses()) using (p) try { t0[p.Id] = p.TotalProcessorTime.TotalMilliseconds; } catch { }
            Thread.Sleep(1000);
            var now = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses()) using (p) try
                {
                    double a; if (!t0.TryGetValue(p.Id, out a) || p.ProcessName == "Idle") continue;
                    double pct = (p.TotalProcessorTime.TotalMilliseconds - a) / 10.0 / Environment.ProcessorCount;
                    double cur; now.TryGetValue(p.ProcessName, out cur); now[p.ProcessName] = cur + pct;
                }
                catch { }
            var rows = new List<Tuple<string, int, long, double>>();
            foreach (var g in Process.GetProcesses().GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                long ram = 0; double cpu = 0; int count = 0;
                foreach (var p in g)
                    using (p)
                    {
                        count++;
                        try { ram += p.WorkingSet64; } catch { }
                        try { cpu += p.TotalProcessorTime.TotalSeconds; } catch { }
                    }
                rows.Add(Tuple.Create(g.Key, count, ram, cpu));
            }
            Func<Tuple<string, int, long, double>, object> ui = r => new Dictionary<string, object> { { "name", r.Item1 }, { "count", r.Item2 }, { "ramMB", Math.Round(r.Item3 / 1048576.0) }, { "cpuSec", Math.Round(r.Item4) } };
            return new Dictionary<string, object> {
                { "byCpu", rows.Where(r => r.Item1 != "Idle").OrderByDescending(r => r.Item4).Take(12).Select(ui).ToList() },
                { "byRam", rows.OrderByDescending(r => r.Item3).Take(15).Select(ui).ToList() }, { "total", rows.Sum(r => r.Item2) },
                { "byCpuNow", now.Where(x => x.Value >= 1).OrderByDescending(x => x.Value).Take(8).Select(x => (object)new Dictionary<string, object> { { "name", x.Key }, { "pct", Math.Round(Math.Min(100, x.Value)) } }).ToList() } };
        }

        static readonly string[] ProtectedStartup = { "SecurityHealth", "Windows Defender", "WindowsDefender", "Realtek", "RtHDVCpl", "RTHDVBg", "RtkAudUService", "igfx", "Intel", "NvBackend", "Synaptics", "ETDCtrl" };

        static object StartupNow()
        {
            var list = new List<object>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Dictionary<string, object> e in (List<object>)Startup.List())
                {
                    string cmd = Convert.ToString(e["command"]), path = Convert.ToString(e["path"]);
                    names.Add(Convert.ToString(e["name"]));
                    var flags = Flags(cmd, path, e["signer"] as string, Convert.ToBoolean(e["exists"]), Convert.ToString(e["company"]));
                    e["flags"] = flags; e["unknown"] = flags.Count > 0;
                    e["protected"] = ProtectedStartup.Any(p => Convert.ToString(e["name"]).IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0 || cmd.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
                    list.Add(e);
                }
                // o que só a WMI enxerga (outras contas, chaves antigas)
                using (var s = new ManagementObjectSearcher("SELECT Name, Command, Location, User FROM Win32_StartupCommand"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string name = Convert.ToString(o["Name"]), cmd = Convert.ToString(o["Command"]);
                        if (names.Contains(name) || names.Contains(Path.GetFileNameWithoutExtension(name))) continue;
                        string path = Startup.ExePath(cmd);
                        var flags = Flags(cmd, path, Startup.Signer(path), File.Exists(path), "");
                        list.Add(new Dictionary<string, object> { { "id", null }, { "name", name }, { "command", cmd }, { "path", path }, { "location", Convert.ToString(o["Location"]) + " (" + o["User"] + ")" },
                            { "enabled", true }, { "flags", flags }, { "unknown", flags.Count > 0 }, { "protected", true }, { "company", "" }, { "signer", null } });
                    }
            }
            catch (Exception ex) { Program.Log(ex); }
            return list;
        }

        static List<string> Flags(string cmd, string path, string signer, bool exists, string company)
        {
            var f = new List<string>();
            string c = (cmd ?? "").ToLowerInvariant();
            if (new[] { ".vbs", ".vbe", ".js", ".jse", ".wsf", ".hta", ".bat", ".cmd", ".ps1" }.Any(x => c.Contains(x))) f.Add("script (" + new[] { ".vbs", ".vbe", ".js", ".jse", ".wsf", ".hta", ".bat", ".cmd", ".ps1" }.First(x => c.Contains(x)) + ")");
            if (c.Contains("wscript") || c.Contains("cscript") || c.Contains("mshta") || c.Contains("powershell") && (c.Contains("-enc") || c.Contains("hidden"))) f.Add("roda escondido por um interpretador");
            if (c.Contains(@"\temp\") || c.Contains(@"\appdata\local\temp")) f.Add("está numa pasta temporária");
            if (!exists && !string.IsNullOrEmpty(path)) f.Add("o arquivo não existe mais");
            else if (exists && signer == null && string.IsNullOrEmpty(company)) f.Add("sem assinatura nem fabricante");
            return f;
        }

        static object ServicesNow()
        {
            var list = new List<object>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, DisplayName, StartMode, PathName, State FROM Win32_Service WHERE State='Running'"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string path = Convert.ToString(o["PathName"]);
                        if (path.IndexOf(@"\Windows\", StringComparison.OrdinalIgnoreCase) >= 0 || string.IsNullOrEmpty(path)) continue;
                        string exe = Startup.ExePath(path), company = "";
                        try { if (File.Exists(exe)) company = FileVersionInfo.GetVersionInfo(exe).CompanyName ?? ""; } catch { }
                        string name = Convert.ToString(o["Name"]);
                        list.Add(new Dictionary<string, object> { { "name", name }, { "display", Convert.ToString(o["DisplayName"]) }, { "startMode", Convert.ToString(o["StartMode"]) },
                            { "path", exe }, { "company", company }, { "protected", IsProtectedService(name, company, exe) } });
                    }
            }
            catch (Exception ex) { Program.Log(ex); }
            return list.OrderBy(x => (string)((Dictionary<string, object>)x)["display"]).ToList();
        }

        static bool IsProtectedService(string name, string company, string exe)
        {
            string all = (name + " " + company + " " + exe).ToLowerInvariant();
            return new[] { "windefend", "securityhealth", "defender", "realtek", "intel", "nvidia", "amd", "synaptics", "elan", "grobs", "antivirus", "kaspersky", "avast", "avg", "eset", "bitdefender", "norton", "mcafee", "malwarebytes" }.Any(all.Contains);
        }

        /// <summary>Desativa (ou reativa) um serviço de terceiros escolhido pelo usuário. Nunca Defender, segurança nem drivers.</summary>
        public static object ServiceSet(string name, bool disable)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            string mode = "", path = "", company = "";
            using (var s = new ManagementObjectSearcher("SELECT StartMode, PathName FROM Win32_Service WHERE Name='" + name.Replace("'", "") + "'"))
                foreach (ManagementObject o in s.Get()) { mode = Convert.ToString(o["StartMode"]); path = Startup.ExePath(Convert.ToString(o["PathName"])); }
            if (path == "" || path.IndexOf(@"\Windows\", StringComparison.OrdinalIgnoreCase) >= 0) throw new InvalidOperationException("Só serviços de terceiros.");
            try { company = FileVersionInfo.GetVersionInfo(path).CompanyName ?? ""; } catch { }
            if (IsProtectedService(name, company, path)) throw new InvalidOperationException("Serviço de segurança ou de driver: o GROBS não mexe.");
            string sc = Power.Sys("sc.exe");
            if (disable)
            {
                Settings.Set("fullScanSvc." + name, mode); // modo original, para reativar igual
                Power.Run(sc, "config \"" + name + "\" start= disabled", 20000);
                Power.Run(sc, "stop \"" + name + "\"", 20000);
            }
            else
            {
                string back = Settings.Get<string>("fullScanSvc." + name) ?? "Manual";
                Power.Run(sc, "config \"" + name + "\" start= " + (back == "Auto" ? "auto" : "demand"), 20000);
                Power.Run(sc, "start \"" + name + "\"", 20000);
            }
            return true;
        }

        static object HealthNow(List<string> notChecked)
        {
            var d = new Dictionary<string, object>();
            try { d["physical"] = ((System.Collections.IEnumerable)SysInfo.Disks()["physical"]).Cast<object>().ToList(); } catch (Exception ex) { Program.Log(ex); d["physical"] = new List<object>(); }
            if (!Program.IsAdmin) notChecked.Add("Contadores de confiabilidade do disco (desgaste e erros) precisam de administrador.");
            var vols = new List<object>();
            foreach (var dr in DriveInfo.GetDrives())
                try
                {
                    if (dr.DriveType != DriveType.Fixed || !dr.IsReady) continue;
                    double freePct = 100.0 * dr.AvailableFreeSpace / dr.TotalSize;
                    vols.Add(new Dictionary<string, object> { { "root", dr.Name }, { "label", SafeLabel(dr) }, { "total", (double)dr.TotalSize }, { "free", (double)dr.AvailableFreeSpace }, { "freePct", Math.Round(freePct, 1) },
                        { "alert", freePct < 15 && Under(Path.GetPathRoot(Win), dr.Name) || freePct < 15 && dr.Name.Equals(Path.GetPathRoot(Win), StringComparison.OrdinalIgnoreCase) } });
                }
                catch { }
            d["volumes"] = vols;
            string sysRoot = Path.GetPathRoot(Win);
            d["sysFiles"] = new[] { "hiberfil.sys", "pagefile.sys", "swapfile.sys" }.Select(f => (object)new Dictionary<string, object> { { "name", f }, { "size", (double)Math.Max(0, FindSize(Path.Combine(sysRoot, f))) } }).ToList();
            if (Program.IsAdmin)
                try
                {
                    int count = 0; DateTime last = DateTime.MinValue;
                    using (var s = new ManagementObjectSearcher(@"root\default", "SELECT CreationTime FROM SystemRestore"))
                        foreach (ManagementObject o in s.Get()) { count++; var t = ManagementDateTimeConverter.ToDateTime(Convert.ToString(o["CreationTime"])); if (t > last) last = t; }
                    d["restorePoints"] = count; d["restoreLast"] = count > 0 ? last.ToString("dd/MM/yyyy HH:mm") : null;
                }
                catch { notChecked.Add("Pontos de restauração: o Windows não respondeu."); }
            else notChecked.Add("Pontos de restauração precisam de administrador.");
            return d;
        }

        // ------------------------------------------------------------ relatório
        static object Causes(Dictionary<string, object> r, List<Item> items)
        {
            var c = new List<object>();
            Action<string, string, string> add = (lvl, t, det) => c.Add(new Dictionary<string, object> { { "level", lvl }, { "title", t }, { "detail", det } });
            var sys = (Dictionary<string, object>)r["system"];
            double ram = sys.ContainsKey("ramUsedPct") ? Convert.ToDouble(sys["ramUsedPct"]) : 0;
            if (ram >= 85) add("bad", "Memória quase cheia (" + ram + "%)", "O Windows passa a usar o disco como memória, o que deixa tudo lento. Feche programas pesados ou veja os que mais usam memória abaixo.");
            else if (ram >= 70) add("warn", "Memória bem usada (" + ram + "%)", "Com mais programas abertos o PC pode ficar lento.");
            var nowCpu = (List<object>)((Dictionary<string, object>)r["processes"])["byCpuNow"];
            if (sys.ContainsKey("cpuLoad") && Convert.ToInt32(sys["cpuLoad"]) >= 80)
                add("bad", "Processador muito ocupado (" + sys["cpuLoad"] + "%)", nowCpu.Count == 0 ? "Veja os processos que mais usam a CPU." :
                    "Quem mais usa agora: " + string.Join(", ", nowCpu.Take(3).Cast<Dictionary<string, object>>().Select(x => x["name"] + " (" + x["pct"] + "%)")) + ". CPU cheia o tempo todo também esquenta o processador.");
            if (sys.ContainsKey("diskBusy") && Convert.ToInt32(sys["diskBusy"]) >= 80) add("warn", "Disco muito ocupado (" + sys["diskBusy"] + "%)", "Medido antes da varredura. Algum programa está lendo ou gravando muito.");
            if (sys.ContainsKey("uptimeHours") && Convert.ToDouble(sys["uptimeHours"]) >= 24 * 7) add("warn", "PC ligado há " + Math.Round(Convert.ToDouble(sys["uptimeHours"]) / 24) + " dias", "Reiniciar libera memória e termina atualizações pendentes.");
            var health = (Dictionary<string, object>)r["health"];
            foreach (Dictionary<string, object> v in (List<object>)health["volumes"])
                if (Convert.ToBoolean(v["alert"])) add("bad", "Disco do sistema com pouco espaço (" + v["freePct"] + "% livre)", "Abaixo de 15% o Windows fica lento e as atualizações falham. Veja as recomendações de limpeza.");
            foreach (Dictionary<string, object> p in (List<object>)health["physical"])
            {
                string type = Convert.ToString(p.ContainsKey("type") ? p["type"] : ""), h = Convert.ToString(p.ContainsKey("health") ? p["health"] : "");
                if (h == "bad") add("bad", "Disco com falha: " + p["name"], "Faça backup agora.");
                else if (h == "warn") add("warn", "Disco com alerta de saúde: " + p["name"], "Acompanhe e mantenha backup.");
            }
            var phys = ((List<object>)health["physical"]).Cast<Dictionary<string, object>>().ToList();
            if (phys.Count > 0 && phys.All(p => Convert.ToString(p["type"]) == "HDD"))
                add("warn", "Windows num HD mecânico", "Este PC não tem SSD. HDs são muito mais lentos; trocar por SSD é a melhoria que mais acelera o PC.");
            var st = (List<object>)r["startup"];
            int on = st.Count(x => Convert.ToBoolean(((Dictionary<string, object>)x)["enabled"]));
            if (on >= 10) add("warn", on + " programas abrem com o Windows", "Desative os que você não usa na aba Inicialização.");
            int unk = st.Count(x => Convert.ToBoolean(((Dictionary<string, object>)x)["unknown"]));
            if (unk > 0) add("bad", unk + " item(ns) de inicialização de origem desconhecida", "Scripts ou programas sem fabricante que abrem com o Windows. Verifique na aba Inicialização.");
            var procs = (Dictionary<string, object>)r["processes"];
            foreach (Dictionary<string, object> p in ((List<object>)procs["byRam"]).Take(3))
                if (Convert.ToDouble(p["ramMB"]) >= 1500) add("info", p["name"] + " usa " + Math.Round(Convert.ToDouble(p["ramMB"]) / 1024, 1) + " GB de memória", p["count"] + " processo(s).");
            int svc = ((List<object>)r["services"]).Count;
            if (svc >= 25) add("info", svc + " serviços de terceiros rodando", "Cada um usa um pouco de memória. Veja a aba Serviços.");
            long free = items.Where(i => i.Deletable && i.Risk == "tranquilo").Sum(i => i.Size);
            if (free >= (2L << 30)) add("info", "Dá para liberar " + Math.Round(free / 1073741824.0, 1) + " GB sem risco", "Itens marcados como tranquilos na aba Armazenamento.");
            return c;
        }

        static object Advice(List<Item> items)
        {
            var order = new Dictionary<string, int> { { "tranquilo", 0 }, { "moderado", 1 }, { "perigoso", 2 } };
            return items.Where(i => i.Deletable && i.Risk != "protegido" && i.Size >= (100L << 20))
                .OrderByDescending(i => i.Size).Take(15)
                .Select(i => (object)new Dictionary<string, object> { { "id", i.Id }, { "title", i.Title }, { "size", (double)i.Size }, { "risk", i.Risk }, { "cat", i.Cat }, { "why", i.Why } }).ToList();
        }

        // ------------------------------------------------------------ fase 2
        static List<string> RunningOf(Item i)
        {
            if (i.Procs.Length == 0) return new List<string>();
            var procs = i.Procs;
            if (i.Cat == "dev" || i.Cat == "proj") procs = procs.Concat(new[] { "java", "node", "gradle" }).Distinct().ToArray();
            var res = new List<string>();
            foreach (var p in procs) { var ps = Process.GetProcessesByName(p); if (ps.Length > 0) res.Add(p); foreach (var x in ps) x.Dispose(); }
            return res;
        }

        public static object Delete(string[] ids)
        {
            if (_busy || _deleting) throw new InvalidOperationException("Aguarde a tarefa atual terminar.");
            var chosen = (ids ?? new string[0]).Select(x => _items.FirstOrDefault(i => i.Id == x)).Where(i => i != null && i.Deletable).ToList();
            if (chosen.Count == 0) throw new ArgumentException("Nenhum item válido marcado.");
            _deleting = true;
            new Thread(() =>
            {
                var results = new List<object>();
                var roots = chosen.Select(i => Path.GetPathRoot(i.Kind == "clean" ? Win : i.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var freeBefore = roots.ToDictionary(r => r, r => FreeOf(r));
                try
                {
                    int k = 0;
                    foreach (var it in chosen)
                    {
                        k++;
                        _phase = "Apagando " + k + " de " + chosen.Count + ": " + it.Title; _progress = 100.0 * (k - 1) / chosen.Count; Notify();
                        results.Add(DeleteOne(it));
                    }
                }
                catch (Exception ex) { Program.Log(ex); _error = ex.Message; }
                var drives = roots.Select(r => (object)new Dictionary<string, object> { { "root", r }, { "before", (double)freeBefore[r] }, { "after", (double)FreeOf(r) } }).ToList();
                _lastDelete = new Dictionary<string, object> { { "results", results }, { "drives", drives }, { "date", DateTime.Now.ToString("HH:mm") } };
                _deleting = false; _phase = ""; _progress = 100;
                Notify();
            }) { IsBackground = true, Name = "FullScanDelete" }.Start();
            return Status();
        }

        static long FreeOf(string root) { try { return new DriveInfo(root).AvailableFreeSpace; } catch { return 0; } }

        static Dictionary<string, object> DeleteOne(Item it)
        {
            var r = new Dictionary<string, object> { { "id", it.Id }, { "title", it.Title } };
            long before = it.Size, after = 0; bool ok = false; string msg = null;
            try
            {
                var busy = RunningOf(it);
                if (busy.Count > 0) throw new InvalidOperationException("Programa aberto (" + string.Join(", ", busy) + "). Feche e tente de novo.");
                if (it.NeedsAdmin && !Program.IsAdmin) throw new UnauthorizedAccessException("Precisa abrir o GROBS como administrador.");
                int failed = 0;
                switch (it.Kind)
                {
                    case "folder":
                    case "contents":
                        if (!Directory.Exists(it.Path)) { ok = true; msg = "Já não existia."; break; }
                        string blocked = ForceDelete.Blocked(Path.GetFullPath(it.Path));
                        if (blocked != null) throw new InvalidOperationException(blocked);
                        before = SizeNow(it.Path);
                        DiskClean.Wipe(it.Path, it.Kind == "folder", ref failed);
                        after = Directory.Exists(it.Path) ? SizeNow(it.Path) : 0;
                        ok = it.Kind == "folder" ? !Directory.Exists(it.Path) : after < before;
                        if (failed > 0) msg = failed + " arquivo(s) em uso ficaram.";
                        break;
                    case "file":
                        if (!File.Exists(it.Path)) { ok = true; msg = "Já não existia."; break; }
                        if (ForceDelete.Blocked(Path.GetFullPath(it.Path)) != null) throw new InvalidOperationException("Arquivo protegido.");
                        var fi = new FileInfo(it.Path); before = SafeReal(fi);
                        if ((fi.Attributes & FileAttributes.ReadOnly) != 0) fi.Attributes = FileAttributes.Normal;
                        fi.Delete();
                        ok = !File.Exists(it.Path); after = ok ? 0 : before;
                        break;
                    case "recycle":
                        var q = new Native.SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO)) };
                        if (Native.SHQueryRecycleBin(it.Path, ref q) == 0) before = q.i64Size;
                        Native.SHEmptyRecycleBin(IntPtr.Zero, it.Path, 0x7);
                        q = new Native.SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO)) };
                        if (Native.SHQueryRecycleBin(it.Path, ref q) == 0) after = q.i64Size;
                        ok = after < before;
                        if (after > 0) msg = "Sobrou um pouco em uso (" + Math.Round(after / 1048576.0) + " MB).";
                        break;
                    case "hibernate":
                        Power.SetHibernation(false);
                        Thread.Sleep(1500);
                        after = Math.Max(0, FindSize(it.Path));
                        ok = FindSize(it.Path) < 0;
                        if (!ok) msg = "O hiberfil.sys ainda existe; reinicie o PC para o Windows removê-lo.";
                        break;
                    case "clean":
                        var res = (Dictionary<string, object>)Cleaner.Clean(new[] { it.Path }, new CancellationToken());
                        long freed = (long)Convert.ToDouble(res["freed"]);
                        after = Math.Max(0, before - freed); ok = freed > 0 || before == 0;
                        int skipped = Convert.ToInt32(res["skipped"]);
                        if (skipped > 0) msg = skipped + " arquivo(s) em uso ou recentes ficaram.";
                        break;
                    case "dism":
                        Power.Run(Power.Sys("Dism.exe"), "/Online /Cleanup-Image /StartComponentCleanup", 40 * 60000);
                        after = SizeNow(it.Path); ok = true; msg = "Limpeza do DISM concluída. O tamanho do WinSxS conta arquivos compartilhados; veja o ganho real no espaço livre.";
                        break;
                    case "winold":
                        PrivRun.AsSystem(new[] { "takeown /F \"" + it.Path + "\" /R /A /D Y >nul", "icacls \"" + it.Path + "\" /grant *S-1-5-32-544:F /T /C /Q >nul", "rd /s /q \"" + it.Path + "\"" }, 30 * 60000);
                        ok = !Directory.Exists(it.Path); after = ok ? 0 : SizeNow(it.Path);
                        if (!ok) msg = "Parte do Windows.old ficou. Use a Limpeza de Disco do Windows para o resto.";
                        break;
                    default: throw new InvalidOperationException("Este item não pode ser apagado.");
                }
                if (ok) _items.Remove(it); else it.Size = after;
            }
            catch (Exception ex) { msg = ex.Message; ok = false; after = before; }
            r["ok"] = ok; r["before"] = (double)before; r["after"] = (double)after; r["msg"] = msg;
            return r;
        }

        static long SizeNow(string dir) { long t = 0; WalkSize(dir, ref t); return t; }
    }
}
