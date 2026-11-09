using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>
    /// Antivírus próprio do GROBS (não usa o motor do Defender):
    /// 1) assinaturas: MD5 de malware conhecido (listas públicas da VirusShare e, com chave grátis, MalwareBazaar);
    /// 2) heurística: sinais típicos de malware (extensão dupla, nome de processo do sistema fora do lugar,
    ///    executável sem assinatura digital iniciando sozinho de pasta temporária, compactadores etc.).
    /// Detecções vão para a lista; o usuário decide apagar (Lixeira) ou ignorar.
    /// </summary>
    internal static class Antivirus
    {
        static readonly string Dir = Path.Combine(Program.DataDir, "av");
        static string SigFile => Path.Combine(Dir, "md5.bin");   // prefixos de 8 bytes do MD5, ordenados
        const int VsLists = 48;                                    // listas mais recentes da VirusShare (65.536 hashes cada)
        const long MaxHash = 64L << 20;                            // arquivos maiores não são hasheados

        static readonly object _lock = new object();
        static volatile bool _busy, _cancel;
        static string _phase = "", _error, _type = "";
        static long _files, _done;
        static string _current = "";
        static DateTime _start;
        static List<Hit> _hits = new List<Hit>();
        static string _finished;

        class Hit
        {
            public string Path, Kind, Reason, Md5, Source; public int Score; public long Size; public bool Removed;
        }

        // ------------------------------------------------------------------ estado
        static Dictionary<string, object> Cfg() => Settings.Get<object>("av") as Dictionary<string, object> ?? new Dictionary<string, object>();
        static void SaveCfg(Dictionary<string, object> c) => Settings.Set("av", c);
        static string CfgStr(string k) { object v; return Cfg().TryGetValue(k, out v) && v != null ? v.ToString() : ""; }

        static HashSet<string> Ignored()
        {
            object v; var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Cfg().TryGetValue("ignored", out v) && v is System.Collections.IEnumerable e) foreach (var o in e) if (o != null) set.Add(o.ToString());
            return set;
        }

        public static object Status()
        {
            lock (_lock)
            {
                long sigs = File.Exists(SigFile) ? new FileInfo(SigFile).Length / 8 : 0;
                return new Dictionary<string, object> {
                    { "running", _busy }, { "phase", _phase }, { "error", _error }, { "type", _type },
                    { "files", _files }, { "done", _done }, { "current", _current },
                    { "elapsed", _busy ? (int)(DateTime.Now - _start).TotalSeconds : 0 },
                    { "finished", _finished },
                    { "hits", _hits.Where(h => !h.Removed).Select(h => (object)new Dictionary<string, object> {
                        { "path", h.Path }, { "kind", h.Kind }, { "reason", h.Reason }, { "md5", h.Md5 }, { "source", h.Source }, { "score", h.Score }, { "size", (double)h.Size } }).ToList() },
                    { "sigs", sigs }, { "sigDate", CfgStr("updated") }, { "bazaar", CfgStr("bazaarKey") != "" },
                    { "ignored", Ignored().Count }, { "admin", Program.IsAdmin } };
            }
        }

        static void Push() => Program.Window?.Notify("av", Status());
        static void Phase(string p) { _phase = p; Push(); }

        static void Background(string type, Action work)
        {
            if (_busy) throw new InvalidOperationException("O antivírus já está trabalhando.");
            _busy = true; _cancel = false; _error = null; _type = type; _start = DateTime.Now;
            new Thread(() =>
            {
                try { work(); }
                catch (Exception ex) { _error = ex.Message; Program.Log(ex); }
                _busy = false; _phase = ""; _current = "";
                Push();
            }) { IsBackground = true, Name = "Antivirus", Priority = ThreadPriority.BelowNormal }.Start();
        }

        public static object Cancel() { _cancel = true; return true; }

        public static object SetKey(string key)
        {
            var c = Cfg();
            c["bazaarKey"] = string.IsNullOrWhiteSpace(key) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
            SaveCfg(c);
            return Status();
        }

        static string BazaarKey()
        {
            try { var s = CfgStr("bazaarKey"); return s == "" ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }

        // ------------------------------------------------------------------ base de assinaturas
        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var w = new WebClient();
            w.Headers[HttpRequestHeader.UserAgent] = "GROBS-Optimizer/" + Program.Version;
            return w;
        }

        static ulong Prefix(byte[] md5) { ulong v = 0; for (int i = 0; i < 8; i++) v = (v << 8) | md5[i]; return v; }

        static bool ParseMd5(string s, out ulong v)
        {
            v = 0;
            if (s.Length != 32) return false;
            for (int i = 0; i < 16; i++)
            {
                int hi = Hex(s[i]); if (hi < 0) return false;
                v = (v << 4) | (uint)hi;
            }
            for (int i = 16; i < 32; i++) if (Hex(s[i]) < 0) return false;
            return true;
        }
        static int Hex(char c) => c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

        public static object Update() { Background("update", DoUpdate); return Status(); }

        static void DoUpdate()
        {
            Directory.CreateDirectory(Dir);
            var c = Cfg();
            var have = new HashSet<int>();
            object v;
            if (c.TryGetValue("vsLists", out v) && v is System.Collections.IEnumerable e) foreach (var o in e) have.Add(Convert.ToInt32(o));
            var add = new List<ulong>();

            Phase("Consultando as listas da VirusShare");
            int latest;
            using (var w = Client())
            {
                var page = w.DownloadString("https://virusshare.com/hashes");
                var nums = Regex.Matches(page, @"VirusShare_(\d{5})\.md5").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();
                if (nums.Count == 0) throw new InvalidOperationException("A VirusShare não respondeu com a lista de arquivos.");
                latest = nums.Max();
                var want = Enumerable.Range(Math.Max(0, latest - VsLists + 1), Math.Min(VsLists, latest + 1)).Where(n => !have.Contains(n)).ToList();
                int i = 0;
                foreach (int n in want)
                {
                    if (_cancel) break;
                    Phase("Baixando assinaturas: lista " + (++i) + " de " + want.Count);
                    string txt = w.DownloadString("https://virusshare.com/hashfiles/VirusShare_" + n.ToString("00000") + ".md5");
                    foreach (var line in txt.Split('\n')) { ulong h; if (ParseMd5(line.Trim(), out h)) add.Add(h); }
                    have.Add(n);
                }
            }

            string key = BazaarKey();
            if (key != "" && !_cancel)
                try
                {
                    Phase("Baixando assinaturas da MalwareBazaar (últimas 48 h)");
                    using (var w = Client())
                    {
                        string csv = w.DownloadString("https://mb-api.abuse.ch/v2/files/exports/" + Uri.EscapeDataString(key) + "/recent.csv");
                        foreach (var tok in csv.Split(new[] { ',', '"', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)) { ulong h; if (ParseMd5(tok, out h)) add.Add(h); }
                    }
                }
                catch (WebException ex) { _error = "MalwareBazaar: " + ex.Message + " (confira a chave)"; }

            Phase("Montando a base de assinaturas");
            var all = new List<ulong>(add.Count + (File.Exists(SigFile) ? (int)(new FileInfo(SigFile).Length / 8) : 0));
            all.AddRange(Load());
            all.AddRange(add);
            all.Sort();
            string tmp = SigFile + ".tmp";
            using (var bw = new BinaryWriter(File.Create(tmp)))
            {
                ulong prev = 0; bool first = true;
                foreach (var h in all) { if (!first && h == prev) continue; bw.Write(h); prev = h; first = false; }
            }
            CloseSigs();
            if (File.Exists(SigFile)) File.Delete(SigFile);
            File.Move(tmp, SigFile);
            c = Cfg();
            c["vsLists"] = have.OrderBy(x => x).Cast<object>().ToList();
            c["updated"] = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            SaveCfg(c);
            if (!_cancel && Environment.Is64BitOperatingSystem)
                try { AvGuard.UpdateYara(Phase); }
                catch (Exception ex) { _error = "Regras YARA: " + ex.Message; }
            _finished = "update";
        }

        static ulong[] Load()
        {
            if (!File.Exists(SigFile)) return new ulong[0];
            var bytes = File.ReadAllBytes(SigFile);
            var arr = new ulong[bytes.Length / 8];
            Buffer.BlockCopy(bytes, 0, arr, 0, arr.Length * 8);
            return arr;
        }

        // ------------------------------------------------------------------ varredura
        static readonly HashSet<string> HashExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".exe", ".dll", ".scr", ".com", ".pif", ".cpl", ".sys", ".ocx", ".msi", ".msp", ".jar", ".js", ".jse", ".vbs", ".vbe", ".wsf", ".wsh", ".hta",
            ".ps1", ".bat", ".cmd", ".lnk", ".doc", ".docm", ".xls", ".xlsm", ".pptm", ".pdf", ".rtf", ".zip", ".rar", ".7z", ".iso", ".img", ".apk", ".bin", ".dat", ".tmp", "" };
        static readonly HashSet<string> ExeExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll", ".scr", ".com", ".pif", ".cpl", ".sys", ".ocx" };
        static readonly HashSet<string> ScriptExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".js", ".jse", ".vbs", ".vbe", ".wsf", ".wsh", ".hta", ".ps1", ".bat", ".cmd" };
        static readonly string[] SysNames = { "svchost", "csrss", "lsass", "winlogon", "services", "smss", "spoolsv", "taskhostw", "dwm", "wininit", "conhost", "rundll32", "explorer", "lsm", "ctfmon", "dllhost", "sihost" };
        static readonly Regex DoubleExt = new Regex(@"\.(pdf|docx?|xlsx?|pptx?|jpe?g|png|gif|txt|mp3|mp4|avi|zip|rar)\s*\.(exe|scr|com|pif|bat|cmd|vbs|js|hta|cpl)$", RegexOptions.IgnoreCase);
        static readonly string[] Packers = { "UPX0", "UPX1", ".aspack", ".adata", ".MPRESS1", ".themida", ".vmp0", ".vmp1", ".enigma1", "PEC2", ".petite", ".nsp0" };

        static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        static string Up => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        static string[] Writable => new[] {
            Path.GetTempPath(), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Path.Combine(Up, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public", Path.Combine(Win, "Temp") }
            .Where(s => !string.IsNullOrEmpty(s)).Select(s => s.TrimEnd('\\') + "\\").ToArray();

        static bool Under(string path, string dir) => path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

        /// <summary>type: quick | full | custom.</summary>
        public static object Scan(string type, string path)
        {
            if (type == "custom" && !Directory.Exists(path) && !File.Exists(path)) throw new InvalidOperationException("Escolha uma pasta que exista.");
            Background(type, () => DoScan(type, path));
            return Status();
        }

        static void DoScan(string type, string custom)
        {
            lock (_lock) { _hits = new List<Hit>(); }
            _files = 0; _done = 0; _finished = null;
            Phase("Carregando assinaturas");
            ulong[] sigs = Load();
            var ignored = Ignored();
            var autostart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Phase("Lendo programas que iniciam com o Windows");
            foreach (var a in AutostartTargets()) autostart.Add(a);
            foreach (var p in Process.GetProcesses())
                using (p) try { var f = p.MainModule?.FileName; if (!string.IsNullOrEmpty(f)) running.Add(f); } catch { }

            // lista de alvos
            var files = new List<string>();
            var dirs = new List<KeyValuePair<string, int>>(); // pasta, profundidade máxima
            if (type == "quick")
            {
                files.AddRange(autostart.Where(File.Exists));
                files.AddRange(running.Where(f => !Under(f, Win)));
                foreach (var w in Writable) dirs.Add(new KeyValuePair<string, int>(w, Under(w, Path.GetTempPath()) || w.EndsWith(@"\Downloads\", StringComparison.OrdinalIgnoreCase) ? 3 : 2));
                dirs.Add(new KeyValuePair<string, int>(Environment.GetFolderPath(Environment.SpecialFolder.Startup), 1));
                dirs.Add(new KeyValuePair<string, int>(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), 1));
            }
            else if (type == "full")
                foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)) dirs.Add(new KeyValuePair<string, int>(d.RootDirectory.FullName, int.MaxValue));
            else if (File.Exists(custom)) files.Add(custom);
            else dirs.Add(new KeyValuePair<string, int>(custom, int.MaxValue));

            Phase("Procurando arquivos");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targets = new List<string>();
            foreach (var f in files) if (seen.Add(f)) targets.Add(f);
            foreach (var d in dirs) Walk(d.Key, d.Value, f => { if (seen.Add(f)) { targets.Add(f); _files = targets.Count; if (_files % 5000 == 0) Push(); } });
            _files = targets.Count;

            Func<ulong, bool> has = sigs.Length > 0 ? (Func<ulong, bool>)(k => Array.BinarySearch(sigs, k) >= 0) : null;
            Phase(sigs.Length > 0 ? "Verificando arquivos" : "Verificando arquivos (sem base de assinaturas: só heurística)");
            var tick = Stopwatch.StartNew();
            using (var md5 = MD5.Create())
                foreach (var f in targets)
                {
                    if (_cancel) break;
                    _current = f; _done++;
                    if (tick.ElapsedMilliseconds > 400) { tick.Restart(); Push(); }
                    try { Check(f, md5, has, ignored, autostart.Contains(f), running.Contains(f)); } catch { }
                }
            if (!_cancel && AvGuard.YaraReady)
            {
                Phase("Verificando com as regras YARA");
                var ylist = targets.Where(t => IsLightTarget(t) || DocExt.Contains(Path.GetExtension(t))).ToList();
                foreach (var g in AvGuard.YaraScan(ylist).GroupBy(x => x.Key)) AddYara(g.Key, g.Select(x => x.Value));
            }
            _finished = _cancel ? "canceled" : "scan";
            Phase(_cancel ? "Verificação cancelada" : "Verificação concluída");
            var c = Cfg(); c["lastScan"] = DateTime.Now.ToString("dd/MM/yyyy HH:mm"); SaveCfg(c);
        }

        static void Walk(string dir, int depth, Action<string> found)
        {
            if (_cancel || depth < 0) return;
            Native.WIN32_FIND_DATA fd;
            IntPtr h = Native.FindFirstFileEx(@"\\?\" + dir.TrimEnd('\\') + @"\*", 1, out fd, 0, IntPtr.Zero, 2);
            if (h == Native.INVALID_HANDLE) return;
            var subs = new List<string>();
            try
            {
                do
                {
                    if ((fd.dwFileAttributes & 0x400) != 0) continue; // links: não segue
                    string n = fd.cFileName;
                    if ((fd.dwFileAttributes & 0x10) != 0) { if (n != "." && n != ".." && n != "WinSxS" && n != "System Volume Information") subs.Add(n); }
                    else if (HashExt.Contains(Path.GetExtension(n))) found(Path.Combine(dir, n));
                } while (Native.FindNextFile(h, out fd));
            }
            finally { Native.FindClose(h); }
            foreach (var s in subs) Walk(Path.Combine(dir, s), depth - 1, found);
        }

        static void Check(string f, MD5 md5, Func<ulong, bool> has, HashSet<string> ignored, bool auto, bool running)
        {
            var fi = new FileInfo(f);
            if (!fi.Exists) return;
            string ext = fi.Extension, name = fi.Name;
            string hash = null;
            if (has != null && fi.Length > 0 && fi.Length <= MaxHash)
            {
                using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
                {
                    var b = md5.ComputeHash(s);
                    hash = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
                    if (ignored.Contains(hash) || ignored.Contains(f)) return;
                    if (has(Prefix(b))) { Add(new Hit { Path = f, Kind = "malware", Reason = "Malware conhecido: este arquivo é idêntico a uma amostra catalogada", Source = "assinatura", Md5 = hash, Score = 100, Size = fi.Length }); return; }
                }
            }
            if (ignored.Contains(f)) return;

            // heurística: só executáveis e scripts
            bool exe = ExeExt.Contains(ext), script = ScriptExt.Contains(ext);
            if (!exe && !script) return;
            var why = new List<string>(); int score = 0;
            if (name.IndexOf('\u202E') >= 0) { score += 80; why.Add("nome com caractere invertido (disfarça a extensão)"); }
            if (DoubleExt.IsMatch(name)) { score += 60; why.Add("extensão dupla, finge ser documento ou imagem"); }
            if (Regex.IsMatch(name, @"\s{6,}\.\w+$")) { score += 50; why.Add("espaços escondendo a extensão"); }
            bool inWin = Under(f, Win), writable = Writable.Any(w => Under(f, w));
            bool startupDir = Under(f, Environment.GetFolderPath(Environment.SpecialFolder.Startup)) || Under(f, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
            string stem = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
            if (exe && !inWin && SysNames.Contains(stem)) { score += 70; why.Add("nome de processo do Windows fora da pasta do Windows"); }
            if (script && startupDir) { score += 35; why.Add("script que roda sozinho ao entrar no Windows"); }
            if ((fi.Attributes & FileAttributes.Hidden) != 0 && (fi.Attributes & FileAttributes.System) != 0 && writable && exe) { score += 25; why.Add("executável oculto e marcado como sistema"); }
            bool strong = score > 0; // sinais fracos (pasta temporária, rodando, compactado) sozinhos não acusam: instaladores legítimos também têm

            if (exe && (writable || score > 0))
            {
                bool signed = Signed(f);
                if (signed) { if (score < 60) return; }
                else
                {
                    if (auto && writable) { score += 40; strong = true; why.Add("inicia com o Windows a partir de pasta de usuário, sem assinatura digital"); }
                    if (Under(f, Path.GetTempPath()) || Under(f, Path.Combine(Win, "Temp"))) { score += 25; why.Add("executável sem assinatura na pasta temporária"); }
                    if (running && writable) { score += 15; why.Add("está rodando agora"); }
                    string packer = Packer(f);
                    if (packer != null) { score += 20; strong = true; why.Add("compactado com " + packer + " (comum para esconder código)"); }
                    if (score > 0 && fi.Length <= MaxHash && Entropy(f) > 7.4) { score += 15; why.Add("conteúdo cifrado ou compactado"); }
                }
            }
            if (score >= 40 && strong)
                Add(new Hit { Path = f, Kind = score >= 60 ? "suspeito" : "atenção", Reason = char.ToUpper(why[0][0]) + string.Join("; ", why).Substring(1), Source = "heurística", Md5 = hash, Score = Math.Min(99, score), Size = fi.Length });
        }

        static void Add(Hit h)
        {
            if (h.Kind == "malware" && AvGuard.AutoQuarantine)
                try { AvGuard.Quarantine(h.Path, h.Reason, h.Md5); h.Removed = true; Program.Window?.Balloon("GROBS: malware isolado na quarentena", Path.GetFileName(h.Path), true); }
                catch (Exception ex) { Program.Log(ex); }
            lock (_lock) _hits.Add(h);
            Push();
        }

        // ------------------------------------------------------------------ consulta de assinaturas sem carregar a base na memória (tempo real)
        static System.IO.MemoryMappedFiles.MemoryMappedFile _mmf;
        static System.IO.MemoryMappedFiles.MemoryMappedViewAccessor _view;
        static long _mmCount;
        static readonly object _mmLock = new object();

        internal static void CloseSigs() { lock (_mmLock) { _view?.Dispose(); _mmf?.Dispose(); _view = null; _mmf = null; } }

        static bool InSigs(ulong key)
        {
            lock (_mmLock)
            {
                if (_view == null)
                {
                    if (!File.Exists(SigFile) || new FileInfo(SigFile).Length < 8) return false;
                    var fs = new FileStream(SigFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                    _mmCount = fs.Length / 8;
                    _mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(fs, null, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read, null, HandleInheritability.None, false);
                    _view = _mmf.CreateViewAccessor(0, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
                }
                long lo = 0, hi = _mmCount - 1;
                while (lo <= hi)
                {
                    long mid = (lo + hi) >> 1;
                    ulong v = _view.ReadUInt64(mid * 8);
                    if (v == key) return true;
                    if (v < key) lo = mid + 1; else hi = mid - 1;
                }
                return false;
            }
        }

        static readonly HashSet<string> DocExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".doc", ".docm", ".xls", ".xlsm", ".pptm", ".rtf", ".pdf", ".msi", ".jar" };

        /// <summary>Tipos que a proteção em tempo real "leve" verifica: executáveis, scripts e instaladores.</summary>
        internal static bool IsLightTarget(string f) { string e = Path.GetExtension(f); return ExeExt.Contains(e) || ScriptExt.Contains(e) || e.Equals(".msi", StringComparison.OrdinalIgnoreCase) || e.Equals(".jar", StringComparison.OrdinalIgnoreCase); }

        /// <summary>Verifica um arquivo (tempo real ou pendrive); avisa na bandeja se achar algo.</summary>
        internal static int CheckRealtime(string f, MD5 md5, string source)
        {
            int before; lock (_lock) before = _hits.Count;
            bool any = File.Exists(SigFile);
            Check(f, md5, any ? (Func<ulong, bool>)InSigs : null, Ignored(), false, false);
            int found; lock (_lock) found = _hits.Count - before;
            if (found == 0 && AvGuard.YaraReady)
                foreach (var g in AvGuard.YaraScan(new[] { f }).GroupBy(x => x.Key))
                { AddYara(g.Key, g.Select(x => x.Value)); found++; }
            if (found > 0)
            {
                Hit h; lock (_lock) h = _hits.LastOrDefault();
                if (h != null && !h.Removed) Program.Window?.Balloon("GROBS (" + source + "): " + (h.Kind == "malware" ? "malware" : "arquivo suspeito"), Path.GetFileName(f) + " — abra o GROBS > Antivírus", true);
            }
            return found;
        }

        static void AddYara(string path, IEnumerable<string> rules)
        {
            if (Ignored().Contains(path)) return;
            lock (_lock) if (_hits.Any(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
            long size = 0; try { size = new FileInfo(path).Length; } catch { }
            Add(new Hit { Path = path, Kind = "suspeito", Reason = "Regra YARA de família de malware: " + string.Join(", ", rules.Distinct().Take(3)), Source = "YARA", Score = 90, Size = size });
        }

        /// <summary>Verificação rápida de um pendrive/HD externo recém-conectado. Devolve quantas ameaças achou.</summary>
        internal static int ScanDevice(string root)
        {
            var list = new List<string>();
            Walk(root, 4, f => { if (IsLightTarget(f) || DocExt.Contains(Path.GetExtension(f))) list.Add(f); });
            int found = 0;
            using (var md5 = MD5.Create())
                foreach (var f in list.Take(20000))
                {
                    int before; lock (_lock) before = _hits.Count;
                    try { Check(f, md5, File.Exists(SigFile) ? (Func<ulong, bool>)InSigs : null, Ignored(), false, false); } catch { }
                    lock (_lock) found += _hits.Count - before;
                }
            if (AvGuard.YaraReady)
                foreach (var g in AvGuard.YaraScan(list.Take(20000).ToList()).GroupBy(x => x.Key)) { int b; lock (_lock) b = _hits.Count; AddYara(g.Key, g.Select(x => x.Value)); lock (_lock) found += _hits.Count - b; }
            return found;
        }

        /// <summary>Não acusar mais este arquivo (hash ou caminho) — usado ao restaurar da quarentena.</summary>
        internal static void IgnoreHash(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            var c = Cfg(); var set = Ignored(); set.Add(key); c["ignored"] = set.Cast<object>().ToList(); SaveCfg(c);
        }

        static string Packer(string f)
        {
            try
            {
                using (var s = File.Open(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new BinaryReader(s))
                {
                    if (s.Length < 0x200 || r.ReadUInt16() != 0x5A4D) return null;
                    s.Position = 0x3C; int pe = r.ReadInt32();
                    if (pe <= 0 || pe > s.Length - 24) return null;
                    s.Position = pe; if (r.ReadUInt32() != 0x4550) return null;
                    s.Position = pe + 6; int n = r.ReadUInt16();
                    s.Position = pe + 20; int opt = r.ReadUInt16();
                    long sec = pe + 24 + opt;
                    for (int i = 0; i < Math.Min(n, 96); i++)
                    {
                        s.Position = sec + i * 40;
                        string nm = Encoding.ASCII.GetString(r.ReadBytes(8)).TrimEnd('\0');
                        var hit = Packers.FirstOrDefault(p => nm.Equals(p, StringComparison.OrdinalIgnoreCase));
                        if (hit != null) return hit.TrimStart('.').TrimEnd('0', '1').ToUpperInvariant();
                    }
                }
            }
            catch { }
            return null;
        }

        static double Entropy(string f)
        {
            var count = new long[256]; long total = 0;
            using (var s = File.Open(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var buf = new byte[1 << 16]; int n;
                while (total < (2 << 20) && (n = s.Read(buf, 0, buf.Length)) > 0) { for (int i = 0; i < n; i++) count[buf[i]]++; total += n; }
            }
            if (total == 0) return 0;
            double e = 0;
            foreach (var c in count) if (c > 0) { double p = (double)c / total; e -= p * Math.Log(p, 2); }
            return e;
        }

        // ------------------------------------------------------------------ inicialização automática
        static IEnumerable<string> AutostartTargets()
        {
            var keys = new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" };
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (var view in Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Default })
                    foreach (var kp in keys)
                    {
                        RegistryKey root = null, k = null;
                        try { root = RegistryKey.OpenBaseKey(hive, view); k = root.OpenSubKey(kp); } catch { }
                        if (k == null) { root?.Dispose(); continue; }
                        var vals = new List<string>();
                        try { foreach (var n in k.GetValueNames()) vals.Add(Convert.ToString(k.GetValue(n))); } catch { }
                        k.Dispose(); root.Dispose();
                        foreach (var v in vals) { var p = Startup.ExePath(v); if (!string.IsNullOrEmpty(p)) yield return p; }
                    }
            foreach (var d in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
            {
                string[] list = new string[0];
                try { list = Directory.GetFiles(d); } catch { }
                foreach (var f in list) yield return f;
            }
        }

        // ------------------------------------------------------------------ assinatura digital (WinVerifyTrust, só arquivo, sem consulta online)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO { public int cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }
        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public int cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData; public int dwUIChoice, fdwRevocationChecks, dwUnionChoice;
            public IntPtr pFile; public int dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference; public int dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
        }
        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)] static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WINTRUST_DATA data);
        static readonly Guid GenericVerify = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        static bool Signed(string f)
        {
            var fi = new WINTRUST_FILE_INFO { cbStruct = Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = f };
            IntPtr pf = Marshal.AllocHGlobal(fi.cbStruct);
            try
            {
                Marshal.StructureToPtr(fi, pf, false);
                var d = new WINTRUST_DATA { cbStruct = Marshal.SizeOf(typeof(WINTRUST_DATA)), dwUIChoice = 2, fdwRevocationChecks = 0, dwUnionChoice = 1, pFile = pf, dwStateAction = 0, dwProvFlags = 0x1000 | 0x80 };
                return WinVerifyTrust(IntPtr.Zero, GenericVerify, ref d) == 0; // 0x1000 = só cache de revogação, 0x80 = sem rede
            }
            catch { return false; }
            finally { Marshal.DestroyStructure(pf, typeof(WINTRUST_FILE_INFO)); Marshal.FreeHGlobal(pf); }
        }

        // ------------------------------------------------------------------ ações
        /// <summary>Isola uma detecção na quarentena (pode ser restaurada depois).</summary>
        public static void QuarantineHit(string path)
        {
            Hit h; lock (_lock) h = _hits.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && !x.Removed);
            if (h == null) throw new InvalidOperationException("Item não está na lista de detecções.");
            AvGuard.Quarantine(h.Path, h.Reason, h.Md5);
            lock (_lock) h.Removed = true;
            Push();
        }

        public static object Remove(string path, bool force)
        {
            Hit h; lock (_lock) h = _hits.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && !x.Removed);
            if (h == null) throw new InvalidOperationException("Item não está na lista de detecções.");
            var blocked = ForceDelete.Blocked(Path.GetFullPath(path));
            if (blocked != null) throw new InvalidOperationException(blocked);
            if (force) ForceDelete.Run(path, true);
            else
            {
                // fecha o programa se ele estiver rodando a partir deste arquivo
                foreach (var p in Process.GetProcesses())
                    using (p) try { if (string.Equals(p.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(3000); } } catch { }
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            lock (_lock) h.Removed = true;
            Push();
            return Status();
        }

        public static object Ignore(string path)
        {
            Hit h; lock (_lock) h = _hits.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && !x.Removed);
            if (h == null) throw new InvalidOperationException("Item não está na lista de detecções.");
            var c = Cfg(); var set = Ignored(); set.Add(h.Md5 ?? h.Path); c["ignored"] = set.Cast<object>().ToList(); SaveCfg(c);
            lock (_lock) h.Removed = true;
            Push();
            return Status();
        }

        public static object ClearIgnored() { var c = Cfg(); c["ignored"] = new List<object>(); SaveCfg(c); return Status(); }

        // ------------------------------------------------------------------ Microsoft Defender (desligar/religar)
        const string DefPol = @"SOFTWARE\Policies\Microsoft\Windows Defender";

        static string Ps(string script) =>
            Power.Run(Power.Sys(@"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -Command \"" + script.Replace("\"", "\\\"") + "\"", 60000);

        public static object Defender(bool on)
        {
            if (!Program.IsAdmin) throw new InvalidOperationException("Abra o GROBS como administrador para mudar o Defender.");
            var st = Security.Status() as Dictionary<string, object>;
            object tamper;
            if (!on && st != null && st.TryGetValue("tamper", out tamper) && tamper is bool t && t)
            {
                try { Process.Start(new ProcessStartInfo("windowsdefender://threatsettings") { UseShellExecute = true }); } catch { }
                throw new InvalidOperationException("A Proteção contra Adulteração está ligada e bloqueia qualquer programa de desligar o Defender. Desligue-a na janela que abriu (Segurança do Windows → Configurações de proteção) e tente de novo.");
            }
            var names = new[] { "DisableRealtimeMonitoring", "DisableBehaviorMonitoring", "DisableIOAVProtection", "DisableOnAccessProtection", "DisableScriptScanning" };
            using (var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default))
            using (var rt = lm.CreateSubKey(DefPol + @"\Real-Time Protection"))
                foreach (var n in names) { if (on) rt.DeleteValue(n, false); else rt.SetValue(n, 1, RegistryValueKind.DWord); }
            string flag = on ? "$false" : "$true";
            Ps("Set-MpPreference -DisableRealtimeMonitoring " + flag + " -DisableBehaviorMonitoring " + flag + " -DisableIOAVProtection " + flag + " -DisableScriptScanning " + flag + " -ErrorAction SilentlyContinue");
            Thread.Sleep(1500);
            var after = Security.Status() as Dictionary<string, object>;
            object rtNow; bool isOn = after != null && after.TryGetValue("realtime", out rtNow) && rtNow is bool b && b;
            if (isOn != on) throw new InvalidOperationException(on ? "O Windows não religou a proteção em tempo real. Ligue em Segurança do Windows." : "O Windows não aceitou desligar a proteção em tempo real (a Proteção contra Adulteração ou uma política da empresa pode estar bloqueando).");
            return after;
        }
    }
}
