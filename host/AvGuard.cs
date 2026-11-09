using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>
    /// Proteções do antivírus do GROBS que ficam ligadas enquanto o app está aberto (inclusive na bandeja):
    /// tempo real (Downloads, Área de Trabalho, temporários e inicialização), pendrive ao conectar,
    /// anti-ransomware (iscas + rajada de alterações; pausa o programa e pergunta), quarentena,
    /// "Verificar com GROBS" no clique direito e regras YARA (YARA oficial + regras MIT da ReversingLabs).
    /// </summary>
    internal static class AvGuard
    {
        static string Dir => Path.Combine(Program.DataDir, "av");
        static string QDir => Path.Combine(Dir, "quarantine");
        static string QIndex => Path.Combine(QDir, "index.json");
        static readonly object _qlock = new object();

        static Dictionary<string, object> Cfg() => Settings.Get<object>("av") as Dictionary<string, object> ?? new Dictionary<string, object>();
        static bool Flag(string k, bool def) { object v; return Cfg().TryGetValue(k, out v) && v is bool b ? b : def; }
        static void SetFlag(string k, object v) { var c = Cfg(); c[k] = v; Settings.Set("av", c); }
        public static bool AutoQuarantine => Flag("autoQ", true);

        public static object Status() => new Dictionary<string, object> {
            { "realtime", _watchers.Count > 0 }, { "usb", _usb != null }, { "ransom", _ransomWatchers.Count > 0 },
            { "ransomFolders", RansomFolders() }, { "customFolders", CustomFolders() }, { "cfa", CfaStatus() },
            { "contextMenu", ContextMenuOn() }, { "autoQ", AutoQuarantine },
            { "yara", YaraReady }, { "yaraRules", RuleCount() }, { "yaraDate", Convert.ToString(Cfg().ContainsKey("yaraDate") ? Cfg()["yaraDate"] : "") },
            { "quarantine", QList() }, { "alert", _alert }, { "admin", Program.IsAdmin } };

        static void Push() => Program.Window?.Notify("avGuard", Status());

        /// <summary>Liga o que estava ligado da última vez (chamado ao abrir o app).</summary>
        public static void Start()
        {
            try { if (Flag("realtime", false)) StartRealtime(); } catch (Exception ex) { Program.Log(ex); }
            try { if (Flag("usb", false)) StartUsb(); } catch (Exception ex) { Program.Log(ex); }
            try { if (Flag("ransom", false)) StartRansom(); } catch (Exception ex) { Program.Log(ex); }
        }

        public static object Set(string what, bool on)
        {
            switch (what)
            {
                case "realtime": if (on) StartRealtime(); else StopRealtime(); break;
                case "usb": if (on) StartUsb(); else StopUsb(); break;
                case "ransom":
                    if (on) StartRansom();
                    else
                    {
                        StopRansom();
                        foreach (var d in RansomFolders()) try { var b = Path.Combine(d, Bait); if (File.Exists(b)) { File.SetAttributes(b, FileAttributes.Normal); File.Delete(b); } } catch { }
                    }
                    break;
                case "contextMenu": ContextMenu(on); break;
                case "autoQ": break;
                case "cfa": Cfa(on); break;
                default: throw new ArgumentException("Opção desconhecida");
            }
            if (what != "contextMenu" && what != "cfa") SetFlag(what, on);
            return Status();
        }

        // ------------------------------------------------------------------ tempo real (leve: só executáveis e scripts novos)
        static readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        static readonly ConcurrentDictionary<string, DateTime> _queue = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        static Thread _worker;

        static IEnumerable<string> RealtimeFolders() => new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), Path.GetTempPath(),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) }
            .Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase);

        static void StartRealtime()
        {
            StopRealtime();
            foreach (var d in RealtimeFolders())
            {
                var w = new FileSystemWatcher(d) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, InternalBufferSize = 65536 };
                FileSystemEventHandler h = (s, e) => Enqueue(e.FullPath);
                w.Created += h; w.Changed += h; w.Renamed += (s, e) => Enqueue(e.FullPath);
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            if (_worker == null)
            {
                _worker = new Thread(Worker) { IsBackground = true, Name = "AvRealtime", Priority = ThreadPriority.BelowNormal };
                _worker.Start();
            }
        }

        static void StopRealtime() { foreach (var w in _watchers) { w.EnableRaisingEvents = false; w.Dispose(); } _watchers.Clear(); Antivirus.CloseSigs(); }

        static void Enqueue(string f) { if (Antivirus.IsLightTarget(f)) _queue[f] = DateTime.Now; }

        static void Worker()
        {
            using (var md5 = MD5.Create())
                while (true)
                {
                    Thread.Sleep(1000);
                    foreach (var kv in _queue.ToArray())
                    {
                        if ((DateTime.Now - kv.Value).TotalSeconds < 2) continue; // espera o download/cópia terminar
                        DateTime t; _queue.TryRemove(kv.Key, out t);
                        if (!File.Exists(kv.Key)) continue;
                        try { using (File.Open(kv.Key, FileMode.Open, FileAccess.Read, FileShare.Read)) { } }
                        catch (IOException) { if ((DateTime.Now - kv.Value).TotalMinutes < 10) _queue[kv.Key] = DateTime.Now; continue; } // ainda sendo escrito
                        catch { continue; }
                        try { Antivirus.CheckRealtime(kv.Key, md5, "tempo real"); } catch (Exception ex) { Program.Log(ex); }
                    }
                }
        }

        // ------------------------------------------------------------------ pendrive / HD externo ao conectar
        static ManagementEventWatcher _usb;

        static void StartUsb()
        {
            StopUsb();
            _usb = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2"));
            _usb.EventArrived += (s, e) =>
            {
                string drive = Convert.ToString(e.NewEvent["DriveName"]);
                if (string.IsNullOrEmpty(drive)) return;
                new Thread(() =>
                {
                    try
                    {
                        Thread.Sleep(1500);
                        var di = new DriveInfo(drive);
                        if (!di.IsReady || (di.DriveType != DriveType.Removable && di.DriveType != DriveType.Fixed)) return;
                        if (di.DriveType == DriveType.Fixed && di.RootDirectory.FullName.Equals(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase)) return;
                        Program.Window?.Balloon("GROBS: verificando " + drive, "Procurando vírus no dispositivo que você conectou…", false);
                        int found = Antivirus.ScanDevice(di.RootDirectory.FullName);
                        Program.Window?.Balloon("GROBS: " + drive + (found == 0 ? " está limpo" : " tem " + found + " ameaça(s)"), found == 0 ? "Nenhuma ameaça encontrada." : "Abra o GROBS > Antivírus para ver.", found > 0);
                    }
                    catch (Exception ex) { Program.Log(ex); }
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
            };
            _usb.Start();
        }

        static void StopUsb() { if (_usb == null) return; try { _usb.Stop(); _usb.Dispose(); } catch { } _usb = null; }

        // ------------------------------------------------------------------ anti-ransomware
        static readonly List<FileSystemWatcher> _ransomWatchers = new List<FileSystemWatcher>();
        static readonly ConcurrentQueue<Tuple<DateTime, string, string>> _events = new ConcurrentQueue<Tuple<DateTime, string, string>>(); // hora, tipo, extensão nova
        static Dictionary<string, object> _alert;
        static System.Threading.Timer _ransomTimer;
        static DateTime _lastAlert = DateTime.MinValue;
        const string Bait = "~$GROBS-isca-nao-apague.docx";
        static readonly HashSet<string> CommonExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".tmp", ".crdownload", ".part", ".partial", ".download", ".bak", ".log", ".lnk", ".ini", "" };

        static List<string> CustomFolders() => ((Cfg().ContainsKey("ransomCustom") ? Cfg()["ransomCustom"] as System.Collections.IEnumerable : null)?.Cast<object>().Select(Convert.ToString) ?? Enumerable.Empty<string>()).Where(Directory.Exists).ToList();

        static List<string> RansomFolders()
        {
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Path.Combine(up, "Downloads") }
                .Concat(CustomFolders()).Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static object AddFolder(string path)
        {
            if (!Directory.Exists(path)) throw new InvalidOperationException("Pasta não encontrada.");
            var list = CustomFolders(); if (!list.Contains(path, StringComparer.OrdinalIgnoreCase)) list.Add(path);
            SetFlag("ransomCustom", list.Cast<object>().ToList());
            if (_ransomWatchers.Count > 0) StartRansom();
            if (CfaStatus() == "on") try { Ps("Add-MpPreference -ControlledFolderAccessProtectedFolders '" + path.Replace("'", "''") + "'"); } catch { }
            return Status();
        }

        public static object RemoveFolder(string path)
        {
            SetFlag("ransomCustom", CustomFolders().Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToList());
            if (_ransomWatchers.Count > 0) StartRansom();
            return Status();
        }

        static void StartRansom()
        {
            StopRansom();
            foreach (var d in RansomFolders())
            {
                // isca: arquivo oculto que nenhum programa normal mexe; se for alterado, é sinal forte de ransomware
                string bait = Path.Combine(d, Bait);
                try
                {
                    if (!File.Exists(bait)) { File.WriteAllBytes(bait, Encoding.UTF8.GetBytes("Arquivo-isca do GROBS Optimizer (anti-ransomware). Pode ignorar; nao altere.\r\n" + new string('=', 4000))); File.SetAttributes(bait, FileAttributes.Hidden | FileAttributes.System); }
                }
                catch { }
                var w = new FileSystemWatcher(d) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, InternalBufferSize = 65536 };
                w.Changed += (s, e) => Event("w", e.FullPath, null);
                w.Deleted += (s, e) => Event("d", e.FullPath, null);
                w.Renamed += (s, e) => Event("r", e.FullPath, e.OldFullPath);
                w.EnableRaisingEvents = true;
                _ransomWatchers.Add(w);
            }
            _ransomTimer = new System.Threading.Timer(_ => Evaluate(), null, 1000, 1000);
        }

        static void StopRansom()
        {
            _ransomTimer?.Dispose(); _ransomTimer = null;
            foreach (var w in _ransomWatchers) { w.EnableRaisingEvents = false; w.Dispose(); }
            _ransomWatchers.Clear();
        }

        static void Event(string kind, string path, string old)
        {
            if (path.EndsWith(Bait, StringComparison.OrdinalIgnoreCase) || (old ?? "").EndsWith(Bait, StringComparison.OrdinalIgnoreCase)) { Trigger("um arquivo-isca protegido foi alterado"); return; }
            _events.Enqueue(Tuple.Create(DateTime.Now, kind, kind == "r" ? Path.GetExtension(path) : null));
        }

        static void Evaluate()
        {
            Tuple<DateTime, string, string> t;
            while (_events.TryPeek(out t) && (DateTime.Now - t.Item1).TotalSeconds > 10) _events.TryDequeue(out t);
            var ev = _events.ToArray();
            var renames = ev.Where(e => e.Item2 == "r" && !CommonExt.Contains(e.Item3 ?? "")).GroupBy(e => e.Item3, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (renames != null && renames.Count() >= 15) Trigger(renames.Count() + " arquivos renomeados para \"" + renames.Key + "\" em 10 segundos");
            else if (ev.Count(e => e.Item2 != "d") >= 120) Trigger(ev.Length + " arquivos alterados em 10 segundos");
        }

        [StructLayout(LayoutKind.Sequential)] struct IO_COUNTERS { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr h, out IO_COUNTERS c);
        [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr h);
        [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr h);

        static readonly HashSet<string> NeverSuspend = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "explorer", "svchost", "System", "Idle", "csrss", "wininit", "winlogon", "services", "lsass", "smss", "MsMpEng", "SearchIndexer", "SearchProtocolHost", "dwm", "OneDrive", "GROBS" };

        /// <summary>Acha quem mais está escrevendo no disco agora (fora do Windows e de programas essenciais) e o pausa.</summary>
        static void Trigger(string reason)
        {
            if ((DateTime.Now - _lastAlert).TotalSeconds < 30 || _alert != null) return;
            _lastAlert = DateTime.Now;
            while (_events.TryDequeue(out _)) { }
            var before = new Dictionary<int, ulong>();
            foreach (var p in Process.GetProcesses()) using (p) try { IO_COUNTERS c; if (GetProcessIoCounters(p.Handle, out c)) before[p.Id] = c.WriteBytes; } catch { }
            Thread.Sleep(1500);
            Process culprit = null; ulong most = 0;
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var p in Process.GetProcesses())
            {
                bool keep = false;
                try
                {
                    if (p.Id == Process.GetCurrentProcess().Id || NeverSuspend.Contains(p.ProcessName) || p.ProcessName.StartsWith("GROBS", StringComparison.OrdinalIgnoreCase)) continue;
                    string file = p.MainModule?.FileName ?? "";
                    if (file.StartsWith(win, StringComparison.OrdinalIgnoreCase)) continue; // componentes do Windows
                    IO_COUNTERS c; ulong b;
                    if (before.TryGetValue(p.Id, out b) && GetProcessIoCounters(p.Handle, out c) && c.WriteBytes - b > most) { most = c.WriteBytes - b; culprit?.Dispose(); culprit = p; keep = true; }
                }
                catch { }
                finally { if (!keep) p.Dispose(); }
            }
            var a = new Dictionary<string, object> { { "reason", reason }, { "time", DateTime.Now.ToString("HH:mm:ss") } };
            if (culprit != null && most > 0)
            {
                try
                {
                    a["pid"] = culprit.Id; a["name"] = culprit.ProcessName; a["path"] = culprit.MainModule?.FileName; a["written"] = (double)most;
                    NtSuspendProcess(culprit.Handle); a["suspended"] = true;
                }
                catch (Exception ex) { a["suspended"] = false; a["error"] = ex.Message; }
                finally { culprit.Dispose(); }
            }
            _alert = a;
            Program.Window?.Balloon("GROBS: possível ransomware", (a.ContainsKey("name") ? a["name"] + " foi pausado. " : "") + "Abra o GROBS para decidir.", true);
            Program.Window?.ShowWindowNow();
            Push();
        }

        /// <summary>action: resume | kill | quarantine</summary>
        public static object Resolve(string action)
        {
            var a = _alert; _alert = null;
            if (a != null && a.ContainsKey("pid"))
                try
                {
                    using (var p = Process.GetProcessById(Convert.ToInt32(a["pid"])))
                    {
                        if (action == "resume") NtResumeProcess(p.Handle);
                        else { p.Kill(); p.WaitForExit(5000); }
                    }
                    if (action == "quarantine" && a.ContainsKey("path") && File.Exists(Convert.ToString(a["path"])))
                        Quarantine(Convert.ToString(a["path"]), "Comportamento de ransomware: " + a["reason"], null);
                }
                catch (Exception ex) { Program.Log(ex); }
            Push();
            return Status();
        }

        // ------------------------------------------------------------------ "Acesso controlado a pastas" do Windows (Defender)
        static string Ps(string cmd) => Power.Run(Power.Sys(@"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -Command \"" + cmd.Replace("\"", "\\\"") + "\"", 60000).Trim();

        static string _cfaCache; static DateTime _cfaWhen;
        static string CfaStatus()
        {
            if (_cfaCache != null && (DateTime.Now - _cfaWhen).TotalSeconds < 30) return _cfaCache;
            string v;
            try { v = Ps("(Get-MpPreference).EnableControlledFolderAccess"); } catch { v = ""; }
            _cfaWhen = DateTime.Now;
            return _cfaCache = v == "1" ? "on" : v == "0" ? "off" : v == "2" ? "audit" : "unavailable";
        }

        static void Cfa(bool on)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            Ps("Set-MpPreference -EnableControlledFolderAccess " + (on ? "Enabled" : "Disabled"));
            if (on) foreach (var f in RansomFolders()) Ps("Add-MpPreference -ControlledFolderAccessProtectedFolders '" + f.Replace("'", "''") + "'");
            _cfaCache = null;
            if (on && CfaStatus() != "on") throw new InvalidOperationException("O Windows não ligou o Acesso controlado a pastas (ele só funciona com a proteção em tempo real do Defender ligada).");
        }

        // ------------------------------------------------------------------ quarentena (arquivo cifrado numa pasta do GROBS)
        class QItem { public string Id, Path, Reason, Md5, Date; public double Size; }

        static List<QItem> QLoad()
        {
            try { return File.Exists(QIndex) ? new JavaScriptSerializer().Deserialize<List<QItem>>(File.ReadAllText(QIndex)) ?? new List<QItem>() : new List<QItem>(); }
            catch { return new List<QItem>(); }
        }
        static void QSave(List<QItem> l) { Directory.CreateDirectory(QDir); File.WriteAllText(QIndex, new JavaScriptSerializer().Serialize(l)); }
        static List<object> QList() { lock (_qlock) return QLoad().Select(q => (object)new Dictionary<string, object> { { "id", q.Id }, { "path", q.Path }, { "reason", q.Reason }, { "date", q.Date }, { "size", q.Size } }).ToList(); }

        static byte[] Xor(byte[] b) { for (int i = 0; i < b.Length; i++) b[i] ^= (byte)(0xA5 ^ (i & 0x3F)); return b; }

        /// <summary>Move o arquivo para a quarentena (cifrado, não executa); fecha antes o programa que o estiver usando.</summary>
        public static void Quarantine(string path, string reason, string md5)
        {
            if (ForceDelete.Blocked(Path.GetFullPath(path)) != null) throw new InvalidOperationException(ForceDelete.Blocked(Path.GetFullPath(path)));
            foreach (var p in Process.GetProcesses())
                using (p) try { if (string.Equals(p.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(3000); } } catch { }
            lock (_qlock)
            {
                Directory.CreateDirectory(QDir);
                string id = Guid.NewGuid().ToString("N");
                byte[] data = File.ReadAllBytes(path);
                File.WriteAllBytes(Path.Combine(QDir, id + ".q"), Xor(data));
                try { File.Delete(path); }
                catch { ForceDelete.Run(path, true); }
                var l = QLoad();
                l.Insert(0, new QItem { Id = id, Path = path, Reason = reason, Md5 = md5, Date = DateTime.Now.ToString("dd/MM/yyyy HH:mm"), Size = data.Length });
                QSave(l);
            }
            Push();
        }

        public static object Restore(string id)
        {
            lock (_qlock)
            {
                var l = QLoad(); var q = l.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Item não está na quarentena.");
                string dest = q.Path;
                if (File.Exists(dest)) dest = Path.Combine(Path.GetDirectoryName(dest), Path.GetFileNameWithoutExtension(dest) + " (restaurado)" + Path.GetExtension(dest));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.WriteAllBytes(dest, Xor(File.ReadAllBytes(Path.Combine(QDir, id + ".q"))));
                File.Delete(Path.Combine(QDir, id + ".q"));
                l.Remove(q); QSave(l);
                Antivirus.IgnoreHash(q.Md5 ?? dest); // não acusar de novo o que você restaurou
            }
            Push();
            return Status();
        }

        public static object DeleteQ(string id)
        {
            lock (_qlock)
            {
                var l = QLoad(); var q = l.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Item não está na quarentena.");
                try { File.Delete(Path.Combine(QDir, id + ".q")); } catch { }
                l.Remove(q); QSave(l);
            }
            Push();
            return Status();
        }

        // ------------------------------------------------------------------ clique direito "Verificar com GROBS"
        static readonly string[] MenuKeys = { @"Software\Classes\*\shell\GROBSScan", @"Software\Classes\Directory\shell\GROBSScan", @"Software\Classes\Drive\shell\GROBSScan" };
        static bool ContextMenuOn() { using (var k = Registry.CurrentUser.OpenSubKey(MenuKeys[0])) return k != null; }

        static void ContextMenu(bool on)
        {
            foreach (var key in MenuKeys)
            {
                if (!on) { Registry.CurrentUser.DeleteSubKeyTree(key, false); continue; }
                using (var k = Registry.CurrentUser.CreateSubKey(key))
                {
                    k.SetValue("MUIVerb", "Verificar com GROBS");
                    k.SetValue("Icon", "\"" + Application.ExecutablePath + "\",0");
                }
                using (var c = Registry.CurrentUser.CreateSubKey(key + @"\command")) c.SetValue("", "\"" + Application.ExecutablePath + "\" --scan \"%1\"");
            }
        }

        static string RequestFile => Path.Combine(Dir, "scan-request.txt");

        /// <summary>Chamado pelo Program quando o exe é aberto com --scan: guarda o pedido para a janela aberta pegar.</summary>
        public static void SaveRequest(string path) { try { Directory.CreateDirectory(Dir); File.WriteAllText(RequestFile, path); } catch { } }

        /// <summary>Pedido de verificação pendente (clique direito); a interface chama ao abrir e quando a janela é chamada.</summary>
        public static object Pending()
        {
            try
            {
                if (!File.Exists(RequestFile)) return null;
                string p = File.ReadAllText(RequestFile).Trim(); File.Delete(RequestFile);
                if (!File.Exists(p) && !Directory.Exists(p)) return null;
                Antivirus.Scan("custom", p);
                return p;
            }
            catch (Exception ex) { Program.Log(ex); return null; }
        }

        // ------------------------------------------------------------------ YARA
        static string YaraExe => Path.Combine(Dir, "yara", "yara64.exe");
        static string YaraC => Path.Combine(Dir, "yara", "yarac64.exe");
        static string Rules => Path.Combine(Dir, "rules.yarc");
        public static bool YaraReady => File.Exists(YaraExe) && File.Exists(Rules);
        static int RuleCount() { object v; return Cfg().TryGetValue("yaraRules", out v) ? Convert.ToInt32(v) : 0; }

        static WebClient Client() { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; var w = new WebClient(); w.Headers[HttpRequestHeader.UserAgent] = "GROBS-Optimizer"; return w; }

        /// <summary>Baixa o YARA oficial (VirusTotal) e as regras MIT da ReversingLabs e compila tudo num arquivo só.</summary>
        public static void UpdateYara(Action<string> phase)
        {
            if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException("As regras YARA do GROBS precisam do Windows 64 bits.");
            string ydir = Path.Combine(Dir, "yara"), tmp = Path.Combine(Dir, "yara-tmp");
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            Directory.CreateDirectory(ydir); Directory.CreateDirectory(tmp);
            using (var w = Client())
            {
                if (!File.Exists(YaraExe))
                {
                    phase("Baixando o YARA (VirusTotal, GitHub)");
                    var rel = new JavaScriptSerializer().DeserializeObject(w.DownloadString("https://api.github.com/repos/VirusTotal/yara/releases/latest")) as Dictionary<string, object>;
                    string url = (rel["assets"] as object[]).Cast<Dictionary<string, object>>().Select(a => Convert.ToString(a["browser_download_url"])).First(u => u.EndsWith("-win64.zip"));
                    string zip = Path.Combine(tmp, "yara.zip"); w.DownloadFile(url, zip);
                    using (var z = ZipFile.OpenRead(zip))
                        foreach (var e in z.Entries.Where(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                            e.ExtractToFile(Path.Combine(ydir, e.Name.StartsWith("yarac", StringComparison.OrdinalIgnoreCase) ? "yarac64.exe" : "yara64.exe"), true);
                }
                phase("Baixando as regras YARA (ReversingLabs, licença MIT)");
                string rz = Path.Combine(tmp, "rules.zip");
                w.DownloadFile("https://codeload.github.com/reversinglabs/reversinglabs-yara-rules/zip/refs/heads/develop", rz);
                ZipFile.ExtractToDirectory(rz, Path.Combine(tmp, "rules"));
            }
            phase("Compilando as regras YARA");
            var files = Directory.GetFiles(Path.Combine(tmp, "rules"), "*.yar*", SearchOption.AllDirectories).Where(f => f.EndsWith(".yara") || f.EndsWith(".yar")).ToList();
            string outFile = Path.Combine(tmp, "rules.yarc");
            // um arquivo-índice com "include" evita o limite de tamanho da linha de comando do Windows
            string index = Path.Combine(tmp, "index.yar");
            Func<IEnumerable<string>, bool> compile = fs =>
            {
                File.WriteAllLines(index, fs.Select(x => "include \"" + x.Replace("\\", "/") + "\""), new UTF8Encoding(false));
                try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }
                using (var p = Process.Start(new ProcessStartInfo(YaraC, "\"" + index + "\" \"" + outFile + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true }))
                { p.StandardOutput.ReadToEndAsync(); p.StandardError.ReadToEnd(); p.WaitForExit(300000); return p.ExitCode == 0 && File.Exists(outFile); }
            };
            var good = files;
            if (!compile(files))
            {
                // alguma regra não compila nesta versão: testa uma a uma e fica só com as boas
                good = files.Where(f => compile(new[] { f })).ToList();
                if (good.Count == 0 || !compile(good)) throw new InvalidOperationException("Não consegui compilar as regras YARA.");
            }
            File.Copy(outFile, Rules, true);
            try { Directory.Delete(tmp, true); } catch { }
            var c = Cfg(); c["yaraRules"] = good.Count; c["yaraDate"] = DateTime.Now.ToString("dd/MM/yyyy HH:mm"); Settings.Set("av", c);
        }

        /// <summary>Roda as regras YARA numa lista de arquivos. Devolve (arquivo, regra).</summary>
        public static List<KeyValuePair<string, string>> YaraScan(IList<string> files)
        {
            var r = new List<KeyValuePair<string, string>>();
            if (!YaraReady || files.Count == 0) return r;
            string list = Path.Combine(Dir, "yara-list-" + Thread.CurrentThread.ManagedThreadId + ".txt");
            File.WriteAllLines(list, files, new UTF8Encoding(false));
            try
            {
                var psi = new ProcessStartInfo(YaraExe, "-C -f -p 2 --skip-larger=67108864 --scan-list \"" + Rules + "\" \"" + list + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                using (var p = Process.Start(psi))
                {
                    try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                    p.ErrorDataReceived += (s, e) => { };
                    p.BeginErrorReadLine();
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        int sp = line.IndexOf(' ');
                        if (sp > 0) r.Add(new KeyValuePair<string, string>(line.Substring(sp + 1).Trim(), line.Substring(0, sp)));
                    }
                    p.WaitForExit(600000);
                }
            }
            finally { try { File.Delete(list); } catch { } }
            return r;
        }
    }
}
