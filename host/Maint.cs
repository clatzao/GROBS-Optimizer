using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Grobs
{
    /// <summary>Arquivos duplicados (mesmo conteúdo, comparado por hash) e arquivos grandes esquecidos. Removidos vão para a Lixeira.</summary>
    internal static class Duplicates
    {
        static volatile bool _cancel;
        static Thread _thread;
        static volatile bool _busy; // falso já na notificação final (a thread ainda está viva nesse momento)
        static volatile string _phase = "";
        static volatile int _files;
        static Dictionary<string, object> _result;

        public static object DefaultFolders()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[] { Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.Combine(home, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) }
                .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static object Status() => new Dictionary<string, object> { { "running", _busy }, { "phase", _phase }, { "files", _files }, { "result", _result } };
        public static object Cancel() { _cancel = true; return true; }

        static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        static readonly string[] SkipRoots = { WinDir, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) };

        public static object Start(string[] folders, int minKb)
        {
            if (_busy) return Status();
            if (folders == null || folders.Length == 0) folders = (string[])DefaultFolders();
            _cancel = false; _files = 0; _result = null;
            long min = Math.Max(1, minKb) * 1024L;
            _thread = new Thread(() =>
            {
                try { _result = Scan(folders, min); }
                catch (Exception ex) { _result = new Dictionary<string, object> { { "error", ex.Message } }; }
                _busy = false; _phase = "";
                Program.Window?.Notify("dupDone", Status());
            }) { IsBackground = true, Name = "Duplicates" };
            _busy = true;
            _thread.Start();
            return Status();
        }

        static IEnumerable<FileInfo> Walk(string root)
        {
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(root));
            bool wholeDrive = root.TrimEnd('\\').Length <= 2;
            while (stack.Count > 0 && !_cancel)
            {
                var d = stack.Pop();
                FileSystemInfo[] items;
                try { items = d.GetFileSystemInfos(); } catch { continue; }
                foreach (var i in items)
                {
                    if ((i.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (i is DirectoryInfo sub)
                    {
                        if ((sub.Attributes & FileAttributes.System) != 0) continue;
                        if (wholeDrive && SkipRoots.Any(r => r.Length > 0 && sub.FullName.Equals(r, StringComparison.OrdinalIgnoreCase))) continue;
                        if (sub.Name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) || sub.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) || sub.Name == ".git") continue;
                        stack.Push(sub);
                    }
                    else if ((i.Attributes & FileAttributes.System) == 0) yield return (FileInfo)i;
                }
            }
        }

        static string Hash(string path, bool partial)
        {
            using (var md5 = MD5.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan))
            {
                if (!partial) return BitConverter.ToString(md5.ComputeHash(fs));
                var buf = new byte[65536 * 2];
                int n = fs.Read(buf, 0, 65536);
                if (fs.Length > 131072) { fs.Seek(-65536, SeekOrigin.End); n += fs.Read(buf, n, 65536); }
                return BitConverter.ToString(md5.ComputeHash(buf, 0, n));
            }
        }

        static Dictionary<string, object> Scan(string[] folders, long min)
        {
            _phase = "Listando arquivos";
            var bySize = new Dictionary<long, List<FileInfo>>();
            var big = new List<FileInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DateTime old = DateTime.Now.AddDays(-180);
            foreach (var root in folders.Where(Directory.Exists))
                foreach (var f in Walk(root))
                {
                    if (!seen.Add(f.FullName)) continue;
                    _files++;
                    long len; DateTime w;
                    try { len = f.Length; w = f.LastWriteTime; } catch { continue; }
                    if (len >= 500L << 20 && w < old) big.Add(f);
                    if (len < min) continue;
                    List<FileInfo> l; if (!bySize.TryGetValue(len, out l)) bySize[len] = l = new List<FileInfo>();
                    l.Add(f);
                }
            var groups = new List<Dictionary<string, object>>();
            var cands = bySize.Where(kv => kv.Value.Count > 1).OrderByDescending(kv => kv.Key).ToList();
            int done = 0;
            foreach (var kv in cands)
            {
                if (_cancel) break;
                _phase = "Comparando conteúdo (" + (++done) + "/" + cands.Count + ")";
                // 1º: começo+fim do arquivo; 2º: arquivo inteiro só para quem empatou
                foreach (var pg in kv.Value.GroupBy(f => { try { return Hash(f.FullName, true); } catch { return Guid.NewGuid().ToString(); } }).Where(g => g.Count() > 1))
                    foreach (var fg in (kv.Key <= 131072 ? pg.GroupBy(f => pg.Key) : pg.GroupBy(f => { try { return Hash(f.FullName, false); } catch { return Guid.NewGuid().ToString(); } })).Where(g => g.Count() > 1))
                    {
                        var files = fg.OrderBy(f => f.CreationTime).Select(f => (object)new Dictionary<string, object> { { "path", f.FullName }, { "modified", f.LastWriteTime.ToString("dd/MM/yyyy") } }).ToList();
                        groups.Add(new Dictionary<string, object> { { "size", (double)kv.Key }, { "wasted", (double)kv.Key * (files.Count - 1) }, { "files", files } });
                    }
            }
            var top = groups.OrderByDescending(g => (double)g["wasted"]).Take(500).Cast<object>().ToList();
            return new Dictionary<string, object> {
                { "groups", top }, { "wasted", groups.Sum(g => (double)g["wasted"]) }, { "scanned", _files }, { "cancelled", _cancel },
                { "big", big.OrderByDescending(f => f.Length).Take(50).Select(f => (object)new Dictionary<string, object> { { "path", f.FullName }, { "size", (double)f.Length }, { "modified", f.LastWriteTime.ToString("dd/MM/yyyy") } }).ToList() } };
        }

        public static object Delete(string[] paths)
        {
            int n = 0; string err = null;
            foreach (var p in paths)
            {
                var full = Path.GetFullPath(p);
                if (full.StartsWith(WinDir + "\\", StringComparison.OrdinalIgnoreCase)) { err = "Arquivos do Windows não são removidos"; continue; }
                try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); n++; }
                catch (Exception ex) { err = ex.Message; }
            }
            if (n == 0 && err != null) throw new InvalidOperationException(err);
            return n;
        }
    }

    /// <summary>Reparos do Windows com o andamento na tela: SFC, DISM, CHKDSK, rede, Windows Update, Microsoft Store e cache de ícones.</summary>
    internal static class Repair
    {
        sealed class Job { public string Id; public string[][] Steps; } // cada passo: exe, argumentos

        static readonly Job[] Jobs = {
            new Job { Id = "sfc", Steps = new[] { new[] { "sfc.exe", "/scannow" } } },
            new Job { Id = "dism", Steps = new[] { new[] { "dism.exe", "/Online /Cleanup-Image /RestoreHealth" } } },
            new Job { Id = "chkdsk", Steps = new[] { new[] { "chkdsk.exe", "C: /scan" } } },
            new Job { Id = "net", Steps = new[] { new[] { "netsh.exe", "winsock reset" }, new[] { "netsh.exe", "int ip reset" }, new[] { "netsh.exe", "interface ip delete arpcache" }, new[] { "ipconfig.exe", "/flushdns" }, new[] { "ipconfig.exe", "/registerdns" } } },
            new Job { Id = "wu", Steps = new[] { new[] { "net.exe", "stop wuauserv" }, new[] { "net.exe", "stop bits" }, new[] { "net.exe", "stop cryptsvc" },
                new[] { "cmd.exe", "/c ren \"%SystemRoot%\\SoftwareDistribution\" SoftwareDistribution.grobs-%RANDOM%" }, new[] { "cmd.exe", "/c ren \"%SystemRoot%\\System32\\catroot2\" catroot2.grobs-%RANDOM%" },
                new[] { "net.exe", "start cryptsvc" }, new[] { "net.exe", "start bits" }, new[] { "net.exe", "start wuauserv" } } },
            new Job { Id = "store", Steps = new[] { new[] { "wsreset.exe", "-i" }, new[] { "cmd.exe", "/c taskkill /im WinStore.App.exe /f" } } },
            new Job { Id = "icons", Steps = new[] { new[] { "ie4uinit.exe", "-show" }, new[] { "cmd.exe", "/c del /a /q \"%LocalAppData%\\IconCache.db\" & del /a /q \"%LocalAppData%\\Microsoft\\Windows\\Explorer\\iconcache_*.db\"" } } },
        };

        static Thread _thread;
        static volatile bool _busy; // falso já na notificação final (a thread ainda está viva nesse momento)
        static string _job;
        static readonly List<string> _lines = new List<string>();
        static volatile int _pct = -1;
        static int _exit;

        public static object Status()
        {
            lock (_lines) return new Dictionary<string, object> { { "job", _job }, { "running", _busy }, { "pct", _pct }, { "exit", _exit }, { "lines", _lines.Skip(Math.Max(0, _lines.Count - 300)).ToArray() }, { "admin", Program.IsAdmin } };
        }

        public static object Start(string id)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            if (_busy) throw new InvalidOperationException("Outro reparo ainda está em andamento");
            var job = Jobs.First(j => j.Id == id);
            _job = id; _pct = -1; _exit = 0;
            lock (_lines) _lines.Clear();
            _thread = new Thread(() =>
            {
                try
                {
                    foreach (var s in job.Steps)
                    {
                        Add("> " + s[0].Replace(".exe", "") + " " + Environment.ExpandEnvironmentVariables(s[1]));
                        _exit = RunStep(s[0], Environment.ExpandEnvironmentVariables(s[1]));
                    }
                    if (id == "icons") Tools.RestartExplorer();
                    Add(id == "net" ? "Concluído. Reinicie o PC para terminar o reparo da rede." : id == "wu" ? "Concluído. O Windows Update vai baixar tudo de novo na próxima verificação." : "Concluído.");
                }
                catch (Exception ex) { Add("Erro: " + ex.Message); _exit = -1; }
                _busy = false; _pct = 100;
                Program.Window?.Notify("repair", Status());
            }) { IsBackground = true, Name = "Repair" };
            _busy = true;
            _thread.Start();
            return Status();
        }

        static void Add(string line)
        {
            line = line.Trim();
            if (line.Length == 0) return;
            var m = Regex.Match(line, @"(\d{1,3}(?:[.,]\d)?)\s?%");
            if (m.Success) { double v; if (double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v <= 100) _pct = (int)v; }
            lock (_lines)
            {
                // linhas de progresso ("[=====  45.0%  ]") substituem a anterior em vez de acumular
                if (m.Success && _lines.Count > 0 && Regex.IsMatch(_lines[_lines.Count - 1], @"\d\s?%")) _lines[_lines.Count - 1] = line; else _lines.Add(line);
            }
            Program.Window?.Notify("repair", Status());
        }

        static int RunStep(string exe, string args)
        {
            var psi = new ProcessStartInfo(Power.Sys(exe), args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) Add(e.Data); };
                p.BeginErrorReadLine();
                var stream = p.StandardOutput.BaseStream;
                var buf = new byte[4096]; Encoding enc = null; var sb = new StringBuilder();
                int n;
                while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                {
                    // o SFC escreve em UTF-16; os outros na página de código OEM do console
                    if (enc == null) enc = n > 3 && buf.Take(n).Count(b => b == 0) > n / 4 ? Encoding.Unicode : Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                    sb.Append(enc.GetString(buf, 0, enc == Encoding.Unicode ? n - n % 2 : n));
                    string text = sb.ToString();
                    int cut = Math.Max(text.LastIndexOf('\n'), text.LastIndexOf('\r'));
                    if (cut >= 0)
                    {
                        foreach (var l in text.Substring(0, cut).Split('\r', '\n')) Add(l.Replace("\0", ""));
                        sb.Clear(); sb.Append(text.Substring(cut + 1));
                    }
                }
                Add(sb.ToString().Replace("\0", ""));
                p.WaitForExit();
                return p.ExitCode;
            }
        }
    }

    /// <summary>Atualizador de programas pelo winget (Gerenciador de Pacotes do Windows).</summary>
    internal static class Winget
    {
        static Thread _thread;
        static volatile bool _busy; // falso já na notificação final (a thread ainda está viva nesse momento)
        static readonly Dictionary<string, string> _state = new Dictionary<string, string>();
        static string _current;

        static string Exe()
        {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
            if (File.Exists(p)) return p;
            try
            {
                var dir = Directory.GetDirectories(Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"), "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe")
                    .OrderByDescending(x => x).FirstOrDefault();
                if (dir != null && File.Exists(Path.Combine(dir, "winget.exe"))) return Path.Combine(dir, "winget.exe");
            }
            catch { }
            return null;
        }

        static string Run(string args, int timeout = 180000)
        {
            var psi = new ProcessStartInfo(Exe(), args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
            using (var p = Process.Start(psi))
            {
                var o = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(timeout)) { try { p.Kill(); } catch { } throw new TimeoutException("O winget demorou demais para responder"); }
                return o.Result;
            }
        }

        public static object List()
        {
            if (Exe() == null) return new Dictionary<string, object> { { "available", false } };
            string o = Run("upgrade --include-unknown --accept-source-agreements --disable-interactivity");
            if (o.Contains("--disable-interactivity") && o.Length < 2000) o = Run("upgrade --include-unknown --accept-source-agreements");
            var lines = o.Split('\n').Select(l => { int r = l.LastIndexOf('\r', Math.Max(0, l.Length - 2)); return (r >= 0 ? l.Substring(r + 1) : l).TrimEnd('\r'); }).ToList();
            int dash = lines.FindIndex(l => l.Trim().Length > 10 && l.Trim().All(c => c == '-'));
            var items = new List<object>();
            if (dash > 0)
            {
                string head = lines[dash - 1];
                var starts = new List<int>();
                for (int i = 0; i < head.Length; i++) if (head[i] != ' ' && (i == 0 || head[i - 1] == ' ')) starts.Add(i);
                if (starts.Count >= 4)
                    for (int i = dash + 1; i < lines.Count; i++)
                    {
                        string l = lines[i];
                        if (l.Trim().Length == 0 || l.Length < starts[3]) break;
                        Func<int, string> col = c => { int a = starts[c]; int b = c + 1 < starts.Count ? starts[c + 1] : l.Length; return a < l.Length ? l.Substring(a, Math.Min(b, l.Length) - a).Trim() : ""; };
                        string id = col(1);
                        if (id.Length == 0 || id.Contains(" ")) continue;
                        items.Add(new Dictionary<string, object> { { "name", col(0) }, { "id", id }, { "version", col(2) }, { "available", col(3) }, { "source", starts.Count > 4 ? col(4) : "" } });
                    }
            }
            return new Dictionary<string, object> { { "available", true }, { "items", items } };
        }

        public static object Status()
        {
            lock (_state) return new Dictionary<string, object> { { "running", _busy }, { "current", _current }, { "state", new Dictionary<string, string>(_state) } };
        }

        public static object Upgrade(string[] ids)
        {
            if (_busy) return Status();
            if (Exe() == null) throw new InvalidOperationException("winget não encontrado (instale o \"Instalador de Aplicativo\" pela Microsoft Store)");
            lock (_state) { _state.Clear(); foreach (var id in ids) _state[id] = "queued"; }
            _thread = new Thread(() =>
            {
                foreach (var id in ids)
                {
                    if (!Regex.IsMatch(id, @"^[\w.\-+]+$")) { lock (_state) _state[id] = "failed"; continue; }
                    _current = id;
                    lock (_state) _state[id] = "running";
                    Program.Window?.Notify("winget", Status());
                    string res;
                    try
                    {
                        var psi = new ProcessStartInfo(Exe(), "upgrade --id " + id + " --exact --silent --accept-source-agreements --accept-package-agreements --disable-interactivity") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                        using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); res = p.ExitCode == 0 ? "done" : "failed"; }
                    }
                    catch { res = "failed"; }
                    lock (_state) _state[id] = res;
                    Program.Window?.Notify("winget", Status());
                }
                _busy = false; _current = null;
                Program.Window?.Notify("winget", Status());
            }) { IsBackground = true, Name = "winget" };
            _busy = true;
            _thread.Start();
            return Status();
        }
    }
}
