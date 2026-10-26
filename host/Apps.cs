using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>Programas instalados (mesmas chaves que o Painel de Controle usa).</summary>
    internal static class Apps
    {
        public static object List()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<object>();
            var sources = new List<Tuple<RegistryHive, RegistryView, string>>
            {
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry64, "Sistema"),
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry32, "Sistema (32 bits)"),
                Tuple.Create(RegistryHive.CurrentUser, RegistryView.Registry64, "Usuário"),
            };
            if (!Environment.Is64BitOperatingSystem) sources.RemoveAt(1);
            foreach (var src in sources)
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(src.Item1, Environment.Is64BitOperatingSystem ? src.Item2 : RegistryView.Default))
                    using (var un = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (un == null) continue;
                        foreach (var sub in un.GetSubKeyNames())
                            using (var k = un.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                string name = Convert.ToString(k.GetValue("DisplayName") ?? "").Trim();
                                string cmd = Convert.ToString(k.GetValue("UninstallString") ?? "");
                                if (name.Length == 0 || cmd.Length == 0) continue;
                                if (Convert.ToInt32(k.GetValue("SystemComponent") ?? 0) == 1) continue;
                                if (k.GetValue("ParentKeyName") != null || k.GetValue("ReleaseType") != null) continue;
                                string ver = Convert.ToString(k.GetValue("DisplayVersion") ?? "");
                                if (!seen.Add(name + "|" + ver)) continue;
                                string date = Convert.ToString(k.GetValue("InstallDate") ?? "");
                                if (date.Length == 8) date = date.Substring(6, 2) + "/" + date.Substring(4, 2) + "/" + date.Substring(0, 4);
                                list.Add(new Dictionary<string, object> {
                                    { "id", (src.Item1 == RegistryHive.CurrentUser ? "cu" : src.Item2 == RegistryView.Registry32 ? "lm32" : "lm") + "|" + sub },
                                    { "name", name }, { "publisher", Convert.ToString(k.GetValue("Publisher") ?? "") }, { "version", ver },
                                    { "size", Convert.ToDouble(k.GetValue("EstimatedSize") ?? 0) * 1024 }, { "date", date },
                                    { "location", Convert.ToString(k.GetValue("InstallLocation") ?? "") }, { "scope", src.Item3 },
                                    { "quiet", k.GetValue("QuietUninstallString") != null } });
                                IconSources[(src.Item1 == RegistryHive.CurrentUser ? "cu" : src.Item2 == RegistryView.Registry32 ? "lm32" : "lm") + "|" + sub] =
                                    new[] { Convert.ToString(k.GetValue("DisplayIcon") ?? ""), cmd, Convert.ToString(k.GetValue("InstallLocation") ?? "") };
                            }
                    }
                }
                catch { }
            }
            return list.Cast<Dictionary<string, object>>().OrderBy(d => (string)d["name"]).Cast<object>().ToList();
        }

        // ---------------------------------------------------------------- ícones

        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> IconSources = new System.Collections.Concurrent.ConcurrentDictionary<string, string[]>();
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> IconCache = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint n);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        /// <summary>"C:\app.exe,0" / "\"C:\app.exe\",-101" / "C:\app.ico" -> caminho e índice.</summary>
        static Tuple<string, int> ParseIcon(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = Environment.ExpandEnvironmentVariables(s.Trim());
            int idx = 0;
            var m = System.Text.RegularExpressions.Regex.Match(s, @"^""?(.+?)""?\s*,\s*(-?\d+)$");
            if (m.Success) { s = m.Groups[1].Value; idx = int.Parse(m.Groups[2].Value); }
            s = s.Trim('"');
            return File.Exists(s) ? Tuple.Create(s, idx) : null;
        }

        static string ToDataUrl(System.Drawing.Icon ico)
        {
            using (var bmp = ico.ToBitmap())
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }

        static string IconFor(string[] src)
        {
            // 1) DisplayIcon do instalador  2) exe principal da pasta  3) o próprio desinstalador
            var candidates = new List<Tuple<string, int>> { ParseIcon(src[0]) };
            if (!string.IsNullOrEmpty(src[2]) && Directory.Exists(src[2]))
                try { candidates.Add(Directory.GetFiles(src[2], "*.exe").Where(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase)).Select(f => Tuple.Create(f, 0)).FirstOrDefault()); } catch { }
            candidates.Add(ParseIcon(Startup.ExePath(src[1])));
            foreach (var c in candidates.Where(c => c != null))
            {
                try
                {
                    if (c.Item1.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                        using (var ico = new System.Drawing.Icon(c.Item1, 32, 32)) return ToDataUrl(ico);
                    var large = new IntPtr[1];
                    if (ExtractIconEx(c.Item1, c.Item2, large, null, 1) > 0 && large[0] != IntPtr.Zero)
                        try { using (var ico = System.Drawing.Icon.FromHandle(large[0])) return ToDataUrl(ico); } finally { DestroyIcon(large[0]); }
                    if (c.Item1.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(c.Item1)) if (ico != null) return ToDataUrl(ico);
                }
                catch { }
            }
            return null;
        }

        /// <summary>Ícones de todos os programas (PNG 32×32 em data URL), em paralelo e com cache.</summary>
        public static object Icons()
        {
            var result = new System.Collections.Concurrent.ConcurrentDictionary<string, object>();
            System.Threading.Tasks.Parallel.ForEach(IconSources, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 4 }, kv =>
            {
                string key = string.Join("|", kv.Value), url;
                if (!IconCache.TryGetValue(key, out url)) { url = IconFor(kv.Value); IconCache[key] = url; }
                if (url != null) result[kv.Key] = url;
            });
            return result.ToDictionary(k => k.Key, k => k.Value);
        }

        static RegistryKey OpenEntry(string id)
        {
            var p = id.Split(new[] { '|' }, 2);
            var hive = p[0] == "cu" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
            var view = !Environment.Is64BitOperatingSystem ? RegistryView.Default : p[0] == "lm32" ? RegistryView.Registry32 : RegistryView.Registry64;
            return RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + p[1]);
        }

        public static object Uninstall(string id)
        {
            string cmd;
            using (var k = OpenEntry(id))
            {
                if (k == null) throw new InvalidOperationException("Programa não encontrado");
                cmd = Convert.ToString(k.GetValue("UninstallString") ?? "");
            }
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            string exe, args;
            if (cmd.StartsWith("\"")) { int e = cmd.IndexOf('"', 1); exe = cmd.Substring(1, e - 1); args = cmd.Substring(e + 1).Trim(); }
            else if (cmd.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase))
            {
                int sp = cmd.IndexOf(' '); exe = "msiexec.exe"; args = sp > 0 ? cmd.Substring(sp + 1).Replace("/I{", "/X{").Replace("/i{", "/X{") : "";
            }
            else
            {
                int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                exe = i > 0 ? cmd.Substring(0, i + 4) : cmd; args = i > 0 ? cmd.Substring(i + 4).Trim() : "";
            }
            var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
            p?.WaitForExit();
            bool still;
            using (var k = OpenEntry(id)) still = k != null;
            return new Dictionary<string, object> { { "removed", !still } };
        }

        /// <summary>Restos depois de desinstalar: pastas com o nome do programa que ficaram para trás.</summary>
        public static object Leftovers(string name, string location)
        {
            var found = new List<object>();
            var roots = new[] {
                Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) }.Distinct();
            // Só nomes exatos: o nome completo ou o nome sem versão/arquitetura ("7-Zip 24.08 (x64)" -> "7-Zip").
            // Nunca uma palavra solta, para não pegar pastas compartilhadas como "Google" ou "Microsoft".
            var stripped = System.Text.RegularExpressions.Regex.Replace(name, @"\s*(\(.*?\)|v?\d+(\.\d+)+.*|x64|x86|64-bit|32-bit)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            var names = new[] { name.Trim(), stripped }.Where(n => n.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var shared = new[] { "Microsoft", "Google", "Common Files", "Windows", "Intel", "AMD", "NVIDIA", "Packages", "Temp" };
            if (!string.IsNullOrEmpty(location) && Directory.Exists(location) && !roots.Any(r => string.Equals(r?.TrimEnd('\\'), location.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                found.Add(Info(location));
            if (names.Length > 0)
                foreach (var r in roots)
                {
                    if (string.IsNullOrEmpty(r) || !Directory.Exists(r)) continue;
                    foreach (var d in SafeDirs(r))
                    {
                        string n = Path.GetFileName(d);
                        if (shared.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
                        if (names.Any(x => n.Equals(x, StringComparison.OrdinalIgnoreCase)))
                            if (!found.Cast<Dictionary<string, object>>().Any(f => ((string)f["path"]).Equals(d, StringComparison.OrdinalIgnoreCase)))
                                found.Add(Info(d));
                    }
                }
            return found;
        }

        static string[] SafeDirs(string p) { try { return Directory.GetDirectories(p); } catch { return new string[0]; } }

        static Dictionary<string, object> Info(string dir)
        {
            long size = 0; int files = 0;
            try { foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)) { size += f.Length; files++; if (files > 20000) break; } } catch { }
            return new Dictionary<string, object> { { "path", dir }, { "size", (double)size }, { "files", files } };
        }

        public static object DeleteLeftover(string path)
        {
            // só aceita pastas dentro das raízes de programas/dados — nunca a raiz em si
            var full = Path.GetFullPath(path).TrimEnd('\\');
            var roots = new[] {
                Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) }.Where(r => !string.IsNullOrEmpty(r)).Select(r => r.TrimEnd('\\'));
            if (!roots.Any(r => full.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))) throw new UnauthorizedAccessException("Pasta fora das áreas permitidas");
            var di = new DirectoryInfo(full);
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Link simbólico ignorado");
            // envia para a Lixeira (recuperável)
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(full, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return true;
        }
    }

    /// <summary>Analisador de espaço em disco (estilo WizTree/TreeSize).</summary>
    internal static class DiskAnalyzer
    {
        sealed class Node { public string Name; public long Size; public int Files; public List<Node> Kids; }

        static Node _root; static string _rootPath;
        static CancellationTokenSource _cts;
        static int _progressFiles; static volatile bool _running;
        static readonly List<KeyValuePair<string, long>> _bigFiles = new List<KeyValuePair<string, long>>();

        public static object Start(string path)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _rootPath = path.TrimEnd('\\') + "\\";
            _progressFiles = 0; _running = true;
            lock (_bigFiles) _bigFiles.Clear();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Node n; try { n = Walk(_rootPath, _rootPath, ct, 0); } catch (OperationCanceledException) { n = new Node { Kids = new List<Node>() }; }
                n.Name = _rootPath;
                if (!ct.IsCancellationRequested) _root = n;
                _running = false;
                Program.Window?.Notify("diskDone", new Dictionary<string, object> { { "path", _rootPath } });
            });
            return true;
        }

        static Node Walk(string path, string name, CancellationToken ct, int depth)
        {
            var node = new Node { Name = name, Kids = new List<Node>() };
            if (ct.IsCancellationRequested) return node;
            var dirs = new List<string>();
            Native.WIN32_FIND_DATA fd;
            // FindExInfoBasic + LARGE_FETCH: sem nome curto 8.3 e com leituras em lote (bem mais rápido)
            IntPtr h = Native.FindFirstFileEx(@"\\?\" + path.TrimEnd('\\') + @"\*", 1, out fd, 0, IntPtr.Zero, 2);
            if (h == Native.INVALID_HANDLE) return node;
            try
            {
                do
                {
                    if ((fd.dwFileAttributes & 0x400) != 0) continue; // junção/link: não seguir
                    if ((fd.dwFileAttributes & 0x10) != 0)
                    {
                        if (fd.cFileName == "." || fd.cFileName == "..") continue;
                        dirs.Add(fd.cFileName);
                    }
                    else
                    {
                        long len = ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
                        if ((fd.dwFileAttributes & 0xA00) != 0) // esparso/comprimido: conta o espaço realmente ocupado
                        {
                            uint hi; uint lo = Native.GetCompressedFileSize(@"\\?\" + Path.Combine(path, fd.cFileName), out hi);
                            if (lo != 0xFFFFFFFF || Marshal.GetLastWin32Error() == 0) len = ((long)hi << 32) | lo;
                        }
                        node.Size += len; node.Files++;
                        Interlocked.Increment(ref _progressFiles);
                        if (len > 100L * 1024 * 1024) lock (_bigFiles) _bigFiles.Add(new KeyValuePair<string, long>(Path.Combine(path, fd.cFileName), len));
                    }
                } while (Native.FindNextFile(h, out fd));
            }
            finally { Native.FindClose(h); }

            var kids = new Node[dirs.Count];
            if (depth < 2 && dirs.Count > 1)
                System.Threading.Tasks.Parallel.For(0, dirs.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount), CancellationToken = ct },
                    i => kids[i] = Walk(Path.Combine(path, dirs[i]), dirs[i], ct, depth + 1));
            else
                for (int i = 0; i < dirs.Count; i++) kids[i] = Walk(Path.Combine(path, dirs[i]), dirs[i], ct, depth + 1);
            foreach (var k in kids) { if (k == null) continue; node.Size += k.Size; node.Files += k.Files; node.Kids.Add(k); }
            node.Kids.Sort((a, b) => b.Size.CompareTo(a.Size));
            if (node.Kids.Count > 300) node.Kids.RemoveRange(300, node.Kids.Count - 300);
            return node;
        }

        public static object Status() => new Dictionary<string, object> { { "running", _running }, { "files", _progressFiles } };

        public static object Cancel() { _cts?.Cancel(); _running = false; return true; }

        /// <summary>Filhos de uma pasta já analisada; sub = caminho relativo separado por "\".</summary>
        public static object Children(string sub)
        {
            if (_root == null) return null;
            var n = _root;
            if (!string.IsNullOrEmpty(sub))
                foreach (var part in sub.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    n = n.Kids.FirstOrDefault(k => k.Name == part);
                    if (n == null) return null;
                }
            long kidsSize = n.Kids.Sum(k => k.Size);
            var kids = n.Kids.Take(60).Select(k => (object)new Dictionary<string, object> { { "name", k.Name }, { "size", (double)k.Size }, { "files", k.Files }, { "dirs", k.Kids.Count } }).ToList();
            List<object> big;
            lock (_bigFiles) big = _bigFiles.OrderByDescending(f => f.Value).Take(25).Select(f => (object)new Dictionary<string, object> { { "path", f.Key }, { "size", (double)f.Value } }).ToList();
            return new Dictionary<string, object> {
                { "root", _rootPath }, { "path", sub ?? "" }, { "size", (double)n.Size }, { "files", n.Files },
                { "looseFiles", (double)(n.Size - kidsSize) }, { "children", kids }, { "bigFiles", big } };
        }
    }

    internal static class Tools
    {
        public static object CreateRestorePoint(string desc)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            // permite mais de um ponto por dia
            using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default)
                .CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore"))
                k.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
            using (var c = new ManagementClass(@"\\.\root\default", "SystemRestore", null))
            {
                var p = c.GetMethodParameters("CreateRestorePoint");
                p["Description"] = desc; p["RestorePointType"] = 12; p["EventType"] = 100;
                var r = c.InvokeMethod("CreateRestorePoint", p, null);
                uint code = Convert.ToUInt32(r["ReturnValue"]);
                if (code != 0) throw new InvalidOperationException(code == 1058 ? "A Proteção do Sistema está desativada na unidade C:" : "Falha ao criar ponto de restauração (" + code + ")");
            }
            return true;
        }

        public static object FlushDns() { Native.DnsFlushResolverCache(); return true; }

        public static object RestartExplorer()
        {
            foreach (var p in Process.GetProcessesByName("explorer")) using (p) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
            Thread.Sleep(800);
            if (Process.GetProcessesByName("explorer").Length == 0) Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            return true;
        }

        /// <summary>Abre um console elevado com SFC/DISM para o usuário acompanhar.</summary>
        public static object RepairSystem(string which)
        {
            string cmd = which == "dism" ? "DISM /Online /Cleanup-Image /RestoreHealth" : "sfc /scannow";
            Process.Start(new ProcessStartInfo(Power.Sys("cmd.exe"), "/k title GROBS - Reparo do sistema && " + cmd) { UseShellExecute = true, Verb = "runas" });
            return true;
        }

        public static object Ping(string host)
        {
            using (var p = new System.Net.NetworkInformation.Ping())
            {
                var times = new List<long>(); int lost = 0;
                for (int i = 0; i < 4; i++)
                {
                    try { var r = p.Send(host, 1500); if (r.Status == System.Net.NetworkInformation.IPStatus.Success) times.Add(r.RoundtripTime); else lost++; }
                    catch { lost++; }
                }
                return new Dictionary<string, object> { { "host", host }, { "avg", times.Count > 0 ? times.Average() : -1 }, { "min", times.Count > 0 ? times.Min() : -1 }, { "max", times.Count > 0 ? times.Max() : -1 }, { "lost", lost } };
            }
        }
    }
}
