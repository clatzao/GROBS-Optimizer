using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Grobs
{
    /// <summary>
    /// Limpeza de disco guiada: acha o que pode sair (caches que se recriam, projetos parados, arquivos grandes esquecidos, Lixeira, hibernação),
    /// mede cada item e classifica o risco. Só apaga o que o usuário marcar, e só itens encontrados pela própria busca (a IA nunca informa caminhos).
    /// </summary>
    internal static class DiskClean
    {
        sealed class Item
        {
            public string Id, Title, Why, Risk, Kind, Path, AiTitle; public long Size; public int AgeDays = -1; public string[] Procs = new string[0]; public bool InUse;
            public bool Regen; // o programa baixa ou recria sozinho
        }

        sealed class Rule { public string Id, Title, Why, Path, Risk; public string[] Procs; public bool Regen = true; }

        static string L => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static string H => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        static IEnumerable<Rule> Rules()
        {
            Func<string, string, string, string, string[], Rule> safe = (id, t, w, p, procs) => new Rule { Id = id, Title = t, Why = w, Path = p, Risk = "safe", Procs = procs };
            Func<string, string, string, string, string[], Rule> review = (id, t, w, p, procs) => new Rule { Id = id, Title = t, Why = w, Path = p, Risk = "review", Procs = procs };
            string[] node = { "node", "npm" }, java = { "java", "gradle", "studio64" }, dart = { "dart", "dartvm", "flutter", "flutter_tester", "dartaotruntime" };
            yield return safe("npm", "Cache do npm", "Pacotes já baixados. O npm baixa de novo quando precisar.", Path.Combine(L, "npm-cache"), node);
            yield return safe("yarn", "Cache do Yarn", "Pacotes já baixados. O Yarn baixa de novo quando precisar.", Path.Combine(L, @"Yarn\Cache"), node);
            yield return safe("pnpm", "Depósito do pnpm", "Pacotes já baixados. Rode pnpm install nos projetos depois.", Path.Combine(L, @"pnpm\store"), node);
            yield return safe("pip", "Cache do pip (Python)", "Pacotes já baixados. O pip baixa de novo quando precisar.", Path.Combine(L, @"pip\Cache"), new[] { "python", "pip" });
            yield return safe("uv", "Cache do uv (Python)", "Pacotes já baixados. O uv baixa de novo quando precisar.", Path.Combine(L, @"uv\cache"), new[] { "uv", "python" });
            yield return safe("gradle", "Cache do Gradle", "Dependências de projetos Android/Java. A próxima compilação baixa de novo e demora mais uma vez.", Path.Combine(H, @".gradle\caches"), java);
            yield return safe("gradlew", "Versões baixadas do Gradle", "O projeto baixa de novo a versão que precisar.", Path.Combine(H, @".gradle\wrapper\dists"), java);
            yield return safe("maven", "Repositório do Maven", "Dependências Java. A próxima compilação baixa de novo.", Path.Combine(H, @".m2\repository"), java);
            yield return safe("nuget", "Pacotes do NuGet (.NET)", "Dependências .NET. A próxima compilação baixa de novo.", Path.Combine(H, @".nuget\packages"), new[] { "dotnet", "devenv", "MSBuild" });
            yield return safe("cargo", "Cache do Cargo (Rust)", "Dependências Rust. A próxima compilação baixa de novo.", Path.Combine(H, @".cargo\registry"), new[] { "cargo", "rustc" });
            yield return safe("pub", "Cache do Dart/Flutter", "Pacotes já baixados. Rode flutter pub get nos projetos depois.", Path.Combine(L, @"Pub\Cache"), dart);
            yield return safe("electron", "Cache do Electron", "Versões baixadas do Electron. Os projetos baixam de novo quando precisarem.", Path.Combine(L, @"electron\Cache"), new[] { "node", "electron" });
            yield return safe("chromeai", "Modelo de IA do Chrome", "O Chrome baixa de novo se o recurso for usado.", Path.Combine(L, @"Google\Chrome\User Data\OptGuideOnDeviceModel"), new[] { "chrome" });
            yield return safe("crash", "Despejos de travamento de programas", "Só servem para analisar travamentos antigos.", Path.Combine(L, "CrashDumps"), new string[0]);
            yield return review("playwright", "Navegadores do Playwright", "Testes automáticos param até você rodar playwright install, que baixa tudo de novo.", Path.Combine(L, "ms-playwright"), new[] { "node" });
            yield return review("hf", "Modelos do Hugging Face", "Modelos de IA baixados. Baixar de novo pode demorar.", Path.Combine(H, @".cache\huggingface"), new[] { "python" });
            yield return review("ollama", "Modelos do Ollama", "A IA local deixa de funcionar até baixar os modelos de novo.", Path.Combine(H, @".ollama\models"), new[] { "ollama" });
            yield return review("avd", "Emuladores Android (AVD)", "Apaga os aparelhos virtuais e tudo o que está neles. Precisa criá-los de novo no Android Studio.", Path.Combine(H, @".android\avd"), new[] { "emulator", "qemu-system-x86_64", "studio64" });
        }

        static volatile bool _busy;
        static volatile string _phase = "";
        static List<Item> _items = new List<Item>();
        static string _date;
        static string _error;

        static object ToUi(Item i) => new Dictionary<string, object> { { "id", i.Id }, { "title", i.Title }, { "why", i.Why }, { "risk", i.Risk }, { "kind", i.Kind }, { "path", i.Path ?? "" }, { "size", (double)i.Size }, { "ageDays", i.AgeDays }, { "inUse", i.InUse } };

        static Dictionary<string, object> Result()
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
            return new Dictionary<string, object> { { "items", _items.OrderBy(i => i.Risk == "safe" ? 0 : 1).ThenByDescending(i => i.Size).Select(ToUi).ToList() }, { "date", _date }, { "free", (double)d.AvailableFreeSpace }, { "total", (double)d.TotalSize }, { "admin", Program.IsAdmin } };
        }

        public static object Status() => new Dictionary<string, object> { { "running", _busy }, { "phase", _phase }, { "error", _error }, { "result", _date != null ? Result() : null } };

        public static object Start()
        {
            if (_busy) return Status();
            _busy = true; _error = null;
            new Thread(() =>
            {
                try { Scan(); }
                catch (Exception ex) { _error = ex.Message; Program.Log(ex); }
                _busy = false; _phase = "";
                Program.Window?.Notify("diskClean", Status());
            }) { IsBackground = true, Name = "DiskClean" }.Start();
            return Status();
        }

        static void Phase(string p) { _phase = p; Program.Window?.Notify("diskClean", Status()); }

        static long SizeOf(string dir)
        {
            long total = 0;
            Native.WIN32_FIND_DATA fd;
            IntPtr h = Native.FindFirstFileEx(@"\\?\" + dir.TrimEnd('\\') + @"\*", 1, out fd, 0, IntPtr.Zero, 2);
            if (h == Native.INVALID_HANDLE) return 0;
            var subs = new List<string>();
            try
            {
                do
                {
                    if ((fd.dwFileAttributes & 0x400) != 0) continue; // link: não conta nem segue
                    if ((fd.dwFileAttributes & 0x10) != 0) { if (fd.cFileName != "." && fd.cFileName != "..") subs.Add(fd.cFileName); }
                    else total += ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
                } while (Native.FindNextFile(h, out fd));
            }
            finally { Native.FindClose(h); }
            foreach (var s in subs) total += SizeOf(Path.Combine(dir, s));
            return total;
        }

        static void Scan()
        {
            var items = new List<Item>();
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses()) using (p) running.Add(p.ProcessName);
            const long Min = 50L << 20;

            Phase("Lixeira e arquivo de hibernação");
            var bin = new Native.SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO)) };
            if (Native.SHQueryRecycleBin(null, ref bin) == 0 && bin.i64Size >= Min)
                items.Add(new Item { Id = "recycle", Title = "Lixeira", AiTitle = "Lixeira do Windows", Why = "Arquivos que você já apagou, mas ainda ocupam o disco. Depois de esvaziar não dá para recuperar. Confira antes se não tem nada lá que você queira.", Risk = "review", Kind = "recycle", Size = bin.i64Size });
            string hib = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "hiberfil.sys");
            if (Program.IsAdmin && File.Exists(hib))
                try
                {
                    items.Add(new Item { Id = "hibernate", Title = "Arquivo de hibernação", AiTitle = "Desligar a hibernação do Windows (hiberfil.sys)", Why = "Desliga a hibernação e a Inicialização Rápida. O PC pode demorar mais para ligar. Não recomendado em notebook que você hiberna. Dá para religar em Otimização.", Risk = "review", Kind = "hibernate", Size = new FileInfo(hib).Length, Regen = false });
                }
                catch { }

            Phase("Caches de programas e ferramentas");
            foreach (var r in Rules())
            {
                if (!Directory.Exists(r.Path)) continue;
                long size = SizeOf(r.Path);
                if (size < Min) continue;
                items.Add(new Item { Id = r.Id, Title = r.Title, AiTitle = r.Title, Why = r.Why, Risk = r.Risk, Kind = "contents", Path = r.Path, Size = size, Procs = r.Procs, InUse = r.Procs.Any(running.Contains), Regen = r.Regen, AgeDays = AgeDays(Directory.GetLastWriteTime(r.Path)) });
            }

            Phase("Temporários e caches de navegadores");
            try
            {
                foreach (Dictionary<string, object> c in (List<object>)Cleaner.Scan(new CancellationToken()))
                {
                    long size = (long)Convert.ToDouble(c["size"]);
                    if (size < Min || Convert.ToBoolean(c["locked"]) || (string)c["id"] == "recycle") continue;
                    items.Add(new Item { Id = "clean:" + c["id"], Title = (string)c["name"], AiTitle = (string)c["name"], Why = Convert.ToString(c["desc"]) + ". Só o que o programa recria sozinho.", Risk = "safe", Kind = "clean", Path = (string)c["id"], Size = size, InUse = Convert.ToBoolean(c["running"]) });
                }
            }
            catch (Exception ex) { Program.Log(ex); }

            Phase("Projetos parados");
            int n = 0;
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.Combine(H, "Projetos"), Path.Combine(H, "source"), Path.Combine(H, "dev") }.Distinct(StringComparer.OrdinalIgnoreCase))
                if (Directory.Exists(root)) FindProjects(root, 0, items, ref n);

            Phase("Arquivos grandes esquecidos");
            n = 0;
            foreach (var root in new[] { Path.Combine(H, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) }.Distinct(StringComparer.OrdinalIgnoreCase))
                if (Directory.Exists(root)) FindBig(root, Path.GetFileName(root), 0, items, ref n);

            _items = items;
            _date = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        }

        static int AgeDays(DateTime t) => (int)Math.Max(0, (DateTime.Now - t).TotalDays);

        /// <summary>Mais novo entre os arquivos do topo do projeto: mostra se ele foi mexido há pouco tempo.</summary>
        static DateTime Touched(string dir)
        {
            DateTime t = Directory.GetLastWriteTime(dir);
            try { foreach (var f in Directory.EnumerateFileSystemEntries(dir)) { var x = File.GetLastWriteTime(f); if (x > t && !f.EndsWith("node_modules") && !f.EndsWith("build") && !f.EndsWith(".dart_tool")) t = x; } } catch { }
            return t;
        }

        static void FindProjects(string dir, int depth, List<Item> items, ref int n)
        {
            if (depth > 4) return;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return; }
            foreach (var s in subs)
            {
                string name = Path.GetFileName(s);
                try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                if (name.StartsWith(".") && name != ".dart_tool") continue;
                bool nodeModules = name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(dir, "package.json"));
                bool flutter = (name == "build" || name == ".dart_tool") && File.Exists(Path.Combine(dir, "pubspec.yaml"));
                if (nodeModules || flutter)
                {
                    int age = AgeDays(Touched(dir));
                    if (age >= 45)
                    {
                        long size = SizeOf(s);
                        if (size >= 100L << 20)
                            items.Add(new Item { Id = "proj:" + (++n), Title = name + " de " + Path.GetFileName(dir), AiTitle = (nodeModules ? "node_modules" : "pasta " + name + " do Flutter") + " de um projeto parado", Why = nodeModules ? "Dependências de um projeto parado. Se voltar a usar, rode npm install." : "Arquivos gerados pela compilação. Se voltar a usar, compile de novo.",
                                Risk = "safe", Kind = "folder", Path = s, Size = size, AgeDays = age });
                    }
                    continue;
                }
                if (name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                FindProjects(s, depth + 1, items, ref n);
            }
        }

        static void FindBig(string dir, string where, int depth, List<Item> items, ref int n)
        {
            if (depth > 5) return;
            try
            {
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles())
                    try
                    {
                        int age = AgeDays(f.LastWriteTime);
                        if (f.Length >= 300L << 20 && age >= 60 && (f.Attributes & FileAttributes.System) == 0)
                            items.Add(new Item { Id = "file:" + (++n), Title = f.Name, AiTitle = "Arquivo " + f.Extension.ToLowerInvariant() + " na pasta " + where, Why = "Arquivo seu, sem alteração há " + age + " dias. Vai para a Lixeira: só libera espaço depois de esvaziá-la.", Risk = "review", Kind = "file", Path = f.FullName, Size = f.Length, AgeDays = age, Regen = false });
                    }
                    catch { }
                foreach (var d in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(d);
                    if (name.StartsWith(".") || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                    try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    FindBig(d, where, depth + 1, items, ref n);
                }
            }
            catch { }
        }

        /// <summary>Texto para a IA: tipo do item, tamanho e idade. Sem caminhos nem nomes de arquivos e projetos.</summary>
        public static List<Dictionary<string, object>> ForAi() => _items.OrderByDescending(i => i.Size).Take(40).Select(i => new Dictionary<string, object> {
            { "id", i.Id }, { "o_que_e", i.AiTitle }, { "tamanhoGB", Math.Round(i.Size / 1073741824.0, 2) }, { "diasSemMudar", i.AgeDays }, { "programaAberto", i.InUse }, { "recriaSozinho", i.Regen }, { "risco", i.Risk } }).ToList();

        public static HashSet<string> Ids() => new HashSet<string>(_items.Select(i => i.Id));

        // ---------------------------------------------------------------- apagar

        /// <summary>Apaga sem seguir links. Devolve os bytes realmente removidos; o que estiver em uso fica.</summary>
        static long Wipe(string dir, bool removeRoot, ref int failed)
        {
            long freed = 0;
            FileSystemInfo[] entries;
            try { entries = new DirectoryInfo(dir).GetFileSystemInfos(); } catch { failed++; return 0; }
            foreach (var e in entries)
                try
                {
                    bool link = (e.Attributes & FileAttributes.ReparsePoint) != 0;
                    if (e is DirectoryInfo d)
                    {
                        if (link) d.Delete(false); // remove só o link; o destino fica intacto
                        else freed += Wipe(d.FullName, true, ref failed);
                    }
                    else
                    {
                        long len = ((FileInfo)e).Length;
                        if ((e.Attributes & FileAttributes.ReadOnly) != 0) e.Attributes = FileAttributes.Normal;
                        e.Delete();
                        freed += len;
                    }
                }
                catch { failed++; }
            if (removeRoot) try { Directory.Delete(dir, false); } catch { }
            return freed;
        }

        public static object Delete(string[] ids)
        {
            long freed = 0, recycled = 0; int failed = 0, done = 0;
            var errors = new List<string>();
            foreach (var id in ids)
            {
                var it = _items.FirstOrDefault(x => x.Id == id);
                if (it == null) continue; // só o que a própria busca encontrou
                Phase("Apagando: " + it.Title);
                try
                {
                    int f = 0;
                    switch (it.Kind)
                    {
                        case "contents":
                        case "folder":
                            string blocked = ForceDelete.Blocked(Path.GetFullPath(it.Path));
                            if (blocked != null) throw new InvalidOperationException(blocked);
                            freed += Wipe(it.Path, it.Kind == "folder", ref f);
                            break;
                        case "file":
                            if (ForceDelete.Blocked(Path.GetFullPath(it.Path)) != null) throw new InvalidOperationException("Arquivo protegido");
                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(it.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            recycled += it.Size;
                            break;
                        case "recycle":
                            if (Native.SHEmptyRecycleBin(IntPtr.Zero, null, 0x7) != 0) throw new InvalidOperationException("O Windows não esvaziou a Lixeira");
                            freed += it.Size;
                            break;
                        case "hibernate":
                            Power.SetHibernation(false);
                            freed += it.Size;
                            break;
                        case "clean":
                            var r = (Dictionary<string, object>)Cleaner.Clean(new[] { it.Path }, new CancellationToken());
                            freed += (long)Convert.ToDouble(r["freed"]);
                            f += Convert.ToInt32(r["skipped"]);
                            break;
                    }
                    failed += f; done++;
                    _items.Remove(it);
                }
                catch (Exception ex) { errors.Add(it.Title + ": " + ex.Message); }
            }
            _phase = "";
            var res = Result();
            res["freed"] = (double)freed; res["recycled"] = (double)recycled; res["failed"] = failed; res["done"] = done; res["errors"] = errors;
            return res;
        }
    }
}
