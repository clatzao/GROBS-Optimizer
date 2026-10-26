using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Grobs
{
    /// <summary>Limpeza segura: só apaga dentro das pastas listadas, nunca segue junções, ignora arquivos em uso.</summary>
    internal static class Cleaner
    {
        sealed class Target
        {
            public string Root, Pattern = "*"; public bool Recursive = true; public int MinAgeHours;
        }

        sealed class Category
        {
            public string Id, Name, Group, Desc, Process; public bool Admin, Default = true;
            public Func<IEnumerable<Target>> Targets;
        }

        static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        static readonly string ProgData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        static Target T(string root, string pattern = "*", bool rec = true, int age = 0) => new Target { Root = root, Pattern = pattern, Recursive = rec, MinAgeHours = age };

        static IEnumerable<Target> Chromium(string userData)
        {
            if (!Directory.Exists(userData)) yield break;
            foreach (var prof in SafeDirs(userData).Where(d => { var n = Path.GetFileName(d); return n == "Default" || n.StartsWith("Profile ") || n == "Guest Profile"; }))
            {
                yield return T(Path.Combine(prof, "Cache"));
                yield return T(Path.Combine(prof, "Code Cache"));
                yield return T(Path.Combine(prof, "GPUCache"));
                yield return T(Path.Combine(prof, "Service Worker", "CacheStorage"));
            }
            yield return T(Path.Combine(userData, "ShaderCache"));
            yield return T(Path.Combine(userData, "GrShaderCache"));
        }

        static IEnumerable<string> SafeDirs(string p)
        {
            try { return Directory.GetDirectories(p); } catch { return new string[0]; }
        }

        static readonly List<Category> Categories = new List<Category>
        {
            new Category { Id = "usertemp", Group = "Windows", Name = "Arquivos temporários do usuário", Desc = "Pasta %TEMP% (arquivos com mais de 24 h)",
                Targets = () => new[] { T(Path.GetTempPath(), age: 24) } },
            new Category { Id = "wintemp", Group = "Windows", Name = "Temporários do Windows", Desc = @"C:\Windows\Temp (mais de 24 h)", Admin = true,
                Targets = () => new[] { T(Path.Combine(Win, "Temp"), age: 24) } },
            new Category { Id = "thumbs", Group = "Windows", Name = "Cache de miniaturas", Desc = "Recriado automaticamente pelo Explorer",
                Targets = () => new[] { T(Path.Combine(Local, @"Microsoft\Windows\Explorer"), "thumbcache_*.db", false) } },
            new Category { Id = "wer", Group = "Windows", Name = "Relatórios de erro", Desc = "Windows Error Reporting",
                Targets = () => new[] { T(Path.Combine(Local, @"Microsoft\Windows\WER")), T(Path.Combine(ProgData, @"Microsoft\Windows\WER\ReportArchive")), T(Path.Combine(ProgData, @"Microsoft\Windows\WER\ReportQueue")) } },
            new Category { Id = "dumps", Group = "Windows", Name = "Despejos de memória", Desc = "Arquivos de falha (crash dumps)",
                Targets = () => new[] { T(Path.Combine(Local, "CrashDumps")), T(Path.Combine(Win, "Minidump")), T(Win, "MEMORY.DMP", false) } },
            new Category { Id = "wupdate", Group = "Windows", Name = "Cache do Windows Update", Desc = "Pacotes já instalados (SoftwareDistribution\\Download)", Admin = true,
                Targets = () => new[] { T(Path.Combine(Win, @"SoftwareDistribution\Download"), age: 24) } },
            new Category { Id = "delivery", Group = "Windows", Name = "Otimização de Entrega", Desc = "Cache de atualizações compartilhadas", Admin = true,
                Targets = () => new[] { T(Path.Combine(Win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")) } },
            new Category { Id = "dxcache", Group = "Windows", Name = "Cache de shaders DirectX", Desc = "Recriado quando necessário",
                Targets = () => new[] { T(Path.Combine(Local, "D3DSCache")) } },
            new Category { Id = "logs", Group = "Windows", Name = "Logs de instalação", Desc = "Logs antigos do CBS/DISM (mais de 7 dias)", Admin = true,
                Targets = () => new[] { T(Path.Combine(Win, @"Logs\CBS"), "*.log", false, 168), T(Path.Combine(Win, @"Logs\CBS"), "*.cab", false, 168), T(Path.Combine(Win, @"Logs\DISM"), "*.log", false, 168) } },
            new Category { Id = "recycle", Group = "Windows", Name = "Lixeira", Desc = "Esvazia a Lixeira de todas as unidades", Default = false,
                Targets = () => new Target[0] },

            new Category { Id = "chrome", Group = "Navegadores", Name = "Google Chrome", Desc = "Somente cache (não apaga senhas, histórico nem cookies)", Process = "chrome",
                Targets = () => Chromium(Path.Combine(Local, @"Google\Chrome\User Data")) },
            new Category { Id = "edge", Group = "Navegadores", Name = "Microsoft Edge", Desc = "Somente cache", Process = "msedge",
                Targets = () => Chromium(Path.Combine(Local, @"Microsoft\Edge\User Data")) },
            new Category { Id = "brave", Group = "Navegadores", Name = "Brave", Desc = "Somente cache", Process = "brave",
                Targets = () => Chromium(Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data")) },
            new Category { Id = "opera", Group = "Navegadores", Name = "Opera / Opera GX", Desc = "Somente cache", Process = "opera",
                Targets = () => new[] { T(Path.Combine(Local, @"Opera Software\Opera Stable\Cache")), T(Path.Combine(Local, @"Opera Software\Opera GX Stable\Cache")),
                    T(Path.Combine(Roaming, @"Opera Software\Opera Stable\Code Cache")), T(Path.Combine(Roaming, @"Opera Software\Opera GX Stable\Code Cache")) } },
            new Category { Id = "vivaldi", Group = "Navegadores", Name = "Vivaldi", Desc = "Somente cache", Process = "vivaldi",
                Targets = () => Chromium(Path.Combine(Local, @"Vivaldi\User Data")) },
            new Category { Id = "firefox", Group = "Navegadores", Name = "Mozilla Firefox", Desc = "Somente cache", Process = "firefox",
                Targets = () => SafeDirs(Path.Combine(Local, @"Mozilla\Firefox\Profiles")).Select(p => T(Path.Combine(p, "cache2"))) },

            new Category { Id = "discord", Group = "Aplicativos", Name = "Discord", Desc = "Cache de imagens e código", Process = "discord",
                Targets = () => new[] { T(Path.Combine(Roaming, @"discord\Cache")), T(Path.Combine(Roaming, @"discord\Code Cache")), T(Path.Combine(Roaming, @"discord\GPUCache")) } },
            new Category { Id = "spotify", Group = "Aplicativos", Name = "Spotify", Desc = "Cache de streaming", Process = "spotify",
                Targets = () => new[] { T(Path.Combine(Local, @"Spotify\Data")), T(Path.Combine(Local, @"Spotify\Browser\Cache")) } },
            new Category { Id = "steam", Group = "Aplicativos", Name = "Steam (navegador interno)", Desc = "htmlcache da loja", Process = "steam",
                Targets = () => new[] { T(Path.Combine(Local, @"Steam\htmlcache")) } },
            new Category { Id = "nvidia", Group = "Aplicativos", Name = "Cache de shaders NVIDIA/AMD", Desc = "DXCache / GLCache (recriado nos jogos)",
                Targets = () => new[] { T(Path.Combine(Local, @"NVIDIA\DXCache")), T(Path.Combine(Local, @"NVIDIA\GLCache")), T(Path.Combine(Local, @"AMD\DxCache")), T(Path.Combine(Local, @"AMD\DxcCache")) } },
            new Category { Id = "teams", Group = "Aplicativos", Name = "Microsoft Teams", Desc = "Cache", Process = "ms-teams",
                Targets = () => new[] { T(Path.Combine(Local, @"Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\EBWebView\Default\Cache")), T(Path.Combine(Roaming, @"Microsoft\Teams\Cache")) } },
        };

        static bool IsReparse(FileSystemInfo f) => (f.Attributes & FileAttributes.ReparsePoint) != 0;

        /// <summary>Enumera arquivos de um alvo sem seguir junções/links simbólicos.</summary>
        static IEnumerable<FileInfo> Files(Target t, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(t.Root) || t.Root.Length < 4) yield break;
            DirectoryInfo root;
            try { root = new DirectoryInfo(t.Root); if (!root.Exists || IsReparse(root)) yield break; } catch { yield break; }
            DateTime limit = DateTime.Now.AddHours(-t.MinAgeHours);
            var stack = new Stack<DirectoryInfo>(); stack.Push(root);
            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested) yield break;
                var dir = stack.Pop();
                FileSystemInfo[] items;
                try { items = dir.GetFileSystemInfos(); } catch { continue; }
                foreach (var it in items)
                {
                    if (IsReparse(it)) continue;
                    var di = it as DirectoryInfo;
                    if (di != null) { if (t.Recursive) stack.Push(di); continue; }
                    var fi = (FileInfo)it;
                    if (t.Pattern != "*" && !Like(fi.Name, t.Pattern)) continue;
                    if (t.MinAgeHours > 0 && fi.LastWriteTime > limit) continue;
                    yield return fi;
                }
            }
        }

        static bool Like(string name, string pattern)
        {
            var rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(name, rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        static bool Running(string name) => !string.IsNullOrEmpty(name) && Process.GetProcessesByName(name).Length > 0;

        public static object Scan(CancellationToken ct)
        {
            var result = new List<object>();
            bool admin = Program.IsAdmin;
            foreach (var c in Categories)
            {
                long size = 0, count = 0;
                if (c.Id == "recycle")
                {
                    var info = new Native.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.SHQUERYRBINFO)) };
                    if (Native.SHQueryRecycleBin(null, ref info) == 0) { size = info.i64Size; count = info.i64NumItems; }
                }
                else if (!c.Admin || admin)
                {
                    foreach (var t in c.Targets())
                        foreach (var f in Files(t, ct))
                        {
                            try { size += f.Length; count++; } catch { }
                        }
                }
                if (ct.IsCancellationRequested) break;
                result.Add(new Dictionary<string, object> {
                    { "id", c.Id }, { "name", c.Name }, { "group", c.Group }, { "desc", c.Desc }, { "admin", c.Admin },
                    { "locked", c.Admin && !admin }, { "size", (double)size }, { "count", count }, { "default", c.Default },
                    { "running", Running(c.Process) } });
            }
            return result;
        }

        public static object Clean(string[] ids, CancellationToken ct)
        {
            long freed = 0, files = 0, failed = 0;
            bool admin = Program.IsAdmin;
            foreach (var c in Categories.Where(x => ids.Contains(x.Id)))
            {
                if (c.Admin && !admin) continue;
                if (c.Id == "recycle")
                {
                    var info = new Native.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.SHQUERYRBINFO)) };
                    Native.SHQueryRecycleBin(null, ref info);
                    // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
                    if (Native.SHEmptyRecycleBin(IntPtr.Zero, null, 0x7) == 0) { freed += info.i64Size; files += info.i64NumItems; }
                    continue;
                }
                foreach (var t in c.Targets())
                {
                    foreach (var f in Files(t, ct).ToList())
                    {
                        try
                        {
                            long len = f.Length;
                            if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes = FileAttributes.Normal;
                            f.Delete(); freed += len; files++;
                        }
                        catch { failed++; }
                    }
                    if (t.Recursive) RemoveEmptyDirs(t.Root);
                }
            }
            return new Dictionary<string, object> { { "freed", (double)freed }, { "files", files }, { "skipped", failed } };
        }

        /// <summary>Remove subpastas vazias (nunca a raiz do alvo).</summary>
        static void RemoveEmptyDirs(string root)
        {
            try
            {
                var rootInfo = new DirectoryInfo(root);
                if (!rootInfo.Exists || IsReparse(rootInfo)) return;
                foreach (var d in rootInfo.GetDirectories())
                {
                    if (IsReparse(d)) continue;
                    RemoveEmptyDirsInner(d);
                }
            }
            catch { }
        }

        static void RemoveEmptyDirsInner(DirectoryInfo d)
        {
            try
            {
                foreach (var s in d.GetDirectories()) if (!IsReparse(s)) RemoveEmptyDirsInner(s);
                if (d.GetFileSystemInfos().Length == 0) d.Delete(false);
            }
            catch { }
        }
    }
}
