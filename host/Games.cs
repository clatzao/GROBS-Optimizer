using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>
    /// Jogos instalados (Steam, Epic e os adicionados à mão) e otimizações por jogo:
    /// GPU de alto desempenho, sem "otimizações de tela cheia", prioridade de CPU alta,
    /// Turbo automático enquanto o jogo roda e perfis do driver NVIDIA.
    /// </summary>
    internal static class Games
    {
        const string GpuPref = @"Software\Microsoft\DirectX\UserGpuPreferences";
        const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        const string Ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\";

        static readonly string[] SkipExe = { "unins", "crash", "report", "redist", "vcredist", "dxsetup", "dotnet", "setup", "install", "launcher_helper", "easyanticheat", "be_", "battleye", "ue4prereq", "uninstall", "updater", "cefsharp", "helper" };

        // ---------------------------------------------------------------- detecção

        static string MainExe(string dir)
        {
            if (!Directory.Exists(dir)) return null;
            try
            {
                return Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories)
                    .Where(f => f.Count(c => c == '\\') - dir.Count(c => c == '\\') <= 4)
                    .Where(f => !SkipExe.Any(s => Path.GetFileName(f).ToLowerInvariant().Contains(s)))
                    .OrderByDescending(f => { try { return new FileInfo(f).Length; } catch { return 0L; } })
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        static IEnumerable<Dictionary<string, object>> Steam()
        {
            string steam = Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) ?? "");
            if (steam.Length == 0 || !Directory.Exists(steam)) yield break;
            var libs = new List<string> { steam.Replace('/', '\\') };
            var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    string txt; try { txt = File.ReadAllText(acf); } catch { continue; }
                    string name = Regex.Match(txt, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                    string inst = Regex.Match(txt, "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
                    string id = Regex.Match(txt, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                    if (name.Length == 0 || inst.Length == 0 || name.StartsWith("Steamworks") || name.Contains("Redistributable") || name.StartsWith("Proton")) continue;
                    string dir = Path.Combine(apps, "common", inst);
                    var exe = MainExe(dir);
                    if (exe == null) continue;
                    yield return new Dictionary<string, object> { { "name", name }, { "store", "Steam" }, { "exe", exe }, { "dir", dir }, { "appid", id } };
                }
            }
        }

        static IEnumerable<Dictionary<string, object>> Epic()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (!Directory.Exists(dir)) yield break;
            foreach (var f in Directory.GetFiles(dir, "*.item"))
            {
                Dictionary<string, object> j;
                try { j = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f)); } catch { continue; }
                string name = Convert.ToString(j.ContainsKey("DisplayName") ? j["DisplayName"] : ""), loc = Convert.ToString(j.ContainsKey("InstallLocation") ? j["InstallLocation"] : ""),
                    launch = Convert.ToString(j.ContainsKey("LaunchExecutable") ? j["LaunchExecutable"] : "");
                if (name.Length == 0 || loc.Length == 0) continue;
                string exe = launch.Length > 0 ? Path.Combine(loc, launch) : MainExe(loc);
                if (exe == null || !File.Exists(exe)) continue;
                yield return new Dictionary<string, object> { { "name", name }, { "store", "Epic" }, { "exe", exe }, { "dir", loc } };
            }
        }

        static readonly string[] GamePublishers = { "Riot Games", "Mojang", "Rockstar", "Ubisoft", "Electronic Arts", "Activision", "Blizzard", "Valve", "CD PROJEKT", "Bethesda",
            "Roblox", "Garena", "KRAFTON", "miHoYo", "HoYoverse", "COGNOSPHERE", "BANDAI NAMCO", "SQUARE ENIX", "SEGA", "CAPCOM", "2K", "Take-Two", "NetEase", "Tencent", "Wargaming", "Gaijin",
            "Paradox", "Smilegate", "Kakao", "Nexon", "Pearl Abyss", "FromSoftware", "Grinding Gear", "Digital Extremes", "Bungie", "Respawn", "Epic Games", "Hi-Rez", "Level Infinite", "Supercell", "Psyonix" };
        static readonly string[] NotGames = { "launcher", "client", "helper", "redistributable", "anticheat", "anti-cheat", "vanguard", "overwolf", "curseforge", "uninstall", "updater", "service", "battle.net", "ea app", "origin", "ubisoft connect", "rockstar games launcher", "epic games launcher", "steam" };

        static Dictionary<string, object> G(string name, string store, string exe) => new Dictionary<string, object> { { "name", name }, { "store", store }, { "exe", exe }, { "dir", Path.GetDirectoryName(exe) } };

        static RegistryKey Hklm32() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);

        /// <summary>Busca completa: GOG, Ubisoft, Xbox/PC Game Pass, programas de editoras de jogos e pastas comuns de jogos.</summary>
        public static object DeepScan()
        {
            var found = new List<Dictionary<string, object>>();
            // GOG Galaxy
            try
            {
                using (var gog = Hklm32().OpenSubKey(@"SOFTWARE\GOG.com\Games"))
                    if (gog != null)
                        foreach (var id in gog.GetSubKeyNames())
                            using (var k = gog.OpenSubKey(id))
                            {
                                string name = Convert.ToString(k?.GetValue("gameName") ?? ""), exe = Convert.ToString(k?.GetValue("exe") ?? "");
                                if (exe.Length == 0) exe = Path.Combine(Convert.ToString(k?.GetValue("path") ?? ""), Convert.ToString(k?.GetValue("exeFile") ?? ""));
                                if (name.Length > 0 && File.Exists(exe)) found.Add(G(name, "GOG", exe));
                            }
            }
            catch { }
            // Ubisoft Connect
            try
            {
                using (var ubi = Hklm32().OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs"))
                    if (ubi != null)
                        foreach (var id in ubi.GetSubKeyNames())
                            using (var k = ubi.OpenSubKey(id))
                            {
                                string dir = Convert.ToString(k?.GetValue("InstallDir") ?? "").Replace('/', '\\');
                                var exe = MainExe(dir);
                                if (exe != null) found.Add(G(new DirectoryInfo(dir.TrimEnd('\\')).Name, "Ubisoft", exe));
                            }
            }
            catch { }
            // programas instalados de editoras de jogos (Riot, EA, Blizzard, Rockstar, HoYoverse...)
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    try
                    {
                        using (var un = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                            if (un != null)
                                foreach (var sub in un.GetSubKeyNames())
                                    using (var k = un.OpenSubKey(sub))
                                    {
                                        string name = Convert.ToString(k?.GetValue("DisplayName") ?? ""), pub = Convert.ToString(k?.GetValue("Publisher") ?? ""), loc = Convert.ToString(k?.GetValue("InstallLocation") ?? "").Trim('"');
                                        if (name.Length == 0 || loc.Length == 0 || !GamePublishers.Any(p => pub.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                                        if (NotGames.Any(n => name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                                        var exe = MainExe(loc);
                                        if (exe != null) found.Add(G(name, pub.Split(' ')[0], exe));
                                    }
                    }
                    catch { }
            // pastas comuns em todas as unidades (inclui XboxGames do PC Game Pass)
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                foreach (var folder in new[] { "Games", "Jogos", "XboxGames", "Riot Games", "GOG Games", "Epic Games", "SteamLibrary\\steamapps\\common", "Program Files\\Epic Games", "Program Files (x86)\\GOG Galaxy\\Games", "Program Files\\Riot Games" })
                {
                    string root = Path.Combine(drive.RootDirectory.FullName, folder);
                    if (!Directory.Exists(root)) continue;
                    string[] subs; try { subs = Directory.GetDirectories(root); } catch { continue; }
                    foreach (var sub in subs)
                    {
                        string name = new DirectoryInfo(sub).Name;
                        if (NotGames.Any(n => name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        var dir = folder == "XboxGames" && Directory.Exists(Path.Combine(sub, "Content")) ? Path.Combine(sub, "Content") : sub;
                        var exe = MainExe(dir);
                        // jogo de verdade costuma ter executável grande; evita pegar utilitários soltos
                        try { if (exe == null || new FileInfo(exe).Length < 5 * 1024 * 1024) continue; } catch { continue; }
                        found.Add(G(name, folder == "XboxGames" ? "Xbox" : "Pasta", exe));
                    }
                }
            var unique = found.GroupBy(g => ((string)g["exe"]).ToLowerInvariant()).Select(g => g.First()).ToList();
            Settings.Set("scannedGames", unique.Select(g => (object)new Dictionary<string, object> { { "name", g["name"] }, { "store", g["store"] }, { "exe", g["exe"] } }).ToArray());
            var r = (Dictionary<string, object>)List();
            r["scanned"] = unique.Count;
            return r;
        }

        static IEnumerable<Dictionary<string, object>> Scanned()
        {
            var v = Settings.Get<object>("scannedGames") as IEnumerable;
            if (v == null) yield break;
            foreach (var o in v)
            {
                var d = o as Dictionary<string, object>;
                if (d == null || !File.Exists(Convert.ToString(d["exe"]))) continue;
                yield return G(Convert.ToString(d["name"]), Convert.ToString(d["store"]), Convert.ToString(d["exe"]));
            }
        }

        static List<string> Custom()
        {
            var v = Settings.Get<object>("customGames") as IEnumerable;
            return v == null ? new List<string>() : v.Cast<object>().Select(Convert.ToString).Where(File.Exists).ToList();
        }

        static Dictionary<string, object> Profiles()
        {
            return Settings.Get<object>("gameProfiles") as Dictionary<string, object> ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        public static object List()
        {
            var list = new List<Dictionary<string, object>>();
            try { list.AddRange(Steam()); } catch { }
            try { list.AddRange(Epic()); } catch { }
            try { list.AddRange(Scanned()); } catch { }
            foreach (var c in Custom())
                list.Add(new Dictionary<string, object> { { "name", Path.GetFileNameWithoutExtension(c) }, { "store", "Manual" }, { "exe", c }, { "dir", Path.GetDirectoryName(c) } });
            var profiles = Profiles();
            bool nv = false; try { nv = NvProfiles.Available; } catch { }
            foreach (var g in list)
            {
                string exe = (string)g["exe"];
                g.Merge(State(exe));
                object p; profiles.TryGetValue(exe, out p);
                var pd = p as Dictionary<string, object>;
                g["autoTurbo"] = pd != null && pd.ContainsKey("autoTurbo") && Convert.ToBoolean(pd["autoTurbo"]);
                g["running"] = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).Length > 0;
            }
            return new Dictionary<string, object> {
                { "games", list.GroupBy(g => ((string)g["exe"]).ToLowerInvariant()).Select(x => (object)x.First()).OrderBy(g => (string)((Dictionary<string, object>)g)["name"]).ToList() },
                { "nvidia", nv }, { "admin", Program.IsAdmin },
                { "nvOptions", NvProfiles.Options.Select(o => (object)new Dictionary<string, object> { { "key", o.Key }, { "title", o.Title }, { "desc", o.Desc }, { "values", o.Values.Keys.ToArray() } }).ToList() } };
        }

        static void Merge(this Dictionary<string, object> a, Dictionary<string, object> b) { foreach (var kv in b) a[kv.Key] = kv.Value; }

        static RegistryKey Lm() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

        /// <summary>Estado real lido do registro.</summary>
        static Dictionary<string, object> State(string exe)
        {
            string gpu, fso;
            using (var k = Registry.CurrentUser.OpenSubKey(GpuPref)) gpu = Convert.ToString(k?.GetValue(exe) ?? "");
            using (var k = Registry.CurrentUser.OpenSubKey(Layers)) fso = Convert.ToString(k?.GetValue(exe) ?? "");
            int prio;
            using (var k = Lm().OpenSubKey(Ifeo + Path.GetFileName(exe) + @"\PerfOptions")) prio = Convert.ToInt32(k?.GetValue("CpuPriorityClass") ?? 0);
            return new Dictionary<string, object> {
                { "gpuHigh", gpu.Contains("GpuPreference=2") }, { "noFso", fso.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE") }, { "highPriority", prio == 3 } };
        }

        /// <summary>Liga/desliga uma otimização para um jogo. Todas são reversíveis.</summary>
        public static object Set(string exe, string what, bool on)
        {
            switch (what)
            {
                case "gpuHigh": // Configurações > Sistema > Tela > Gráficos > Alto desempenho
                    using (var k = Registry.CurrentUser.CreateSubKey(GpuPref))
                        if (on) k.SetValue(exe, "GpuPreference=2;"); else k.DeleteValue(exe, false);
                    break;
                case "noFso": // Propriedades do exe > Compatibilidade > Desabilitar otimizações de tela inteira
                    using (var k = Registry.CurrentUser.CreateSubKey(Layers))
                    {
                        var flags = Convert.ToString(k.GetValue(exe) ?? "").Replace("~", "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(f => f != "DISABLEDXMAXIMIZEDWINDOWEDMODE").ToList();
                        if (on) flags.Add("DISABLEDXMAXIMIZEDWINDOWEDMODE");
                        if (flags.Count == 0) k.DeleteValue(exe, false); else k.SetValue(exe, "~ " + string.Join(" ", flags));
                    }
                    break;
                case "highPriority": // o Windows aplica prioridade Alta toda vez que o jogo abre
                    if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
                    string key = Ifeo + Path.GetFileName(exe) + @"\PerfOptions";
                    if (on) using (var k = Lm().CreateSubKey(key)) { k.SetValue("CpuPriorityClass", 3, RegistryValueKind.DWord); k.SetValue("IoPriority", 3, RegistryValueKind.DWord); }
                    else Lm().DeleteSubKeyTree(key, false);
                    break;
                case "autoTurbo":
                    var profiles = Profiles();
                    var p = profiles.ContainsKey(exe) ? (Dictionary<string, object>)profiles[exe] : new Dictionary<string, object>();
                    p["autoTurbo"] = on; profiles[exe] = p;
                    Settings.Set("gameProfiles", profiles);
                    Watcher.Refresh();
                    break;
                default: throw new ArgumentException("Opção desconhecida");
            }
            return State(exe);
        }

        /// <summary>Aplica tudo de uma vez ("Otimizar este jogo").</summary>
        public static object OptimizeAll(string exe)
        {
            Set(exe, "gpuHigh", true);
            Set(exe, "noFso", true);
            if (Program.IsAdmin) Set(exe, "highPriority", true);
            Set(exe, "autoTurbo", true);
            if (Program.IsAdmin && NvProfiles.Available)
                try { NvProfiles.Apply(exe, new Dictionary<string, object> { { "power", "max" }, { "latency", "on" }, { "texture", "highperf" } }); } catch { }
            return State(exe);
        }

        public static object AddCustom(string exe)
        {
            if (!File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Escolha um arquivo .exe");
            var list = Custom(); if (!list.Contains(exe, StringComparer.OrdinalIgnoreCase)) list.Add(exe);
            Settings.Set("customGames", list.ToArray());
            return List();
        }

        public static object RemoveCustom(string exe)
        {
            Settings.Set("customGames", Custom().Where(c => !c.Equals(exe, StringComparison.OrdinalIgnoreCase)).ToArray());
            return List();
        }

        /// <summary>Executáveis de todos os jogos conhecidos (cache de 10 min: a busca nas lojas lê o disco).</summary>
        static List<string> _known; static DateTime _knownAt;
        internal static List<string> KnownExes()
        {
            if (_known == null || (DateTime.Now - _knownAt).TotalMinutes > 10)
            {
                try { _known = ((IEnumerable)((Dictionary<string, object>)List())["games"]).Cast<Dictionary<string, object>>().Select(g => (string)g["exe"]).ToList(); }
                catch { _known = _known ?? new List<string>(); }
                _knownAt = DateTime.Now;
            }
            return _known;
        }

        /// <summary>
        /// Verifica a cada 5 s se um jogo abriu: Turbo automático (por jogo), Modo Jogo automático e gravação de FPS (todos os jogos).
        /// </summary>
        internal static class Watcher
        {
            static Timer _timer;
            static string _activeExe;
            static bool _turbo;

            static List<string> TurboTargets() => Profiles().Where(kv => kv.Value is Dictionary<string, object> d && d.ContainsKey("autoTurbo") && Convert.ToBoolean(d["autoTurbo"])).Select(kv => kv.Key).ToList();
            static bool AllGames() => GameMode.Enabled || FpsLog.Enabled;

            public static void Refresh()
            {
                if (TurboTargets().Count == 0 && !AllGames()) { _timer?.Dispose(); _timer = null; return; }
                if (_timer == null) _timer = new Timer(_ => Tick(), null, 2000, 5000);
            }

            static void Tick()
            {
                try
                {
                    var turbo = TurboTargets();
                    var targets = new HashSet<string>(turbo, StringComparer.OrdinalIgnoreCase);
                    if (AllGames()) foreach (var e in KnownExes()) targets.Add(e);
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in Process.GetProcesses()) using (p) names.Add(p.ProcessName);
                    string running = _activeExe != null && names.Contains(Path.GetFileNameWithoutExtension(_activeExe)) ? _activeExe
                        : targets.FirstOrDefault(t => names.Contains(Path.GetFileNameWithoutExtension(t)));
                    if (running != null && _activeExe == null)
                    {
                        _activeExe = running;
                        _turbo = turbo.Contains(running, StringComparer.OrdinalIgnoreCase);
                        if (_turbo && !Boost.Active)
                        {
                            var apps = Settings.Get<object>("boostApps") as IEnumerable;
                            Boost.Start(apps?.Cast<object>().Select(Convert.ToString).ToArray());
                            Program.Window?.Notify("boost", Boost.Status());
                        }
                        if (GameMode.Enabled) GameMode.Enter(!_turbo);
                        else MemoryCleaner.OnGameStart();
                        if (FpsLog.Enabled) FpsLog.Begin(running, 0);
                        Program.Window?.Notify("gameStarted", Path.GetFileNameWithoutExtension(running));
                    }
                    else if (running == null && _activeExe != null)
                    {
                        _activeExe = null;
                        if (_turbo && Boost.Active) { Boost.Stop(); Program.Window?.Notify("boost", Boost.Status()); }
                        _turbo = false;
                        GameMode.Exit();
                        FpsLog.End();
                    }
                }
                catch (Exception ex) { Program.Log(ex); }
            }
        }
    }
}
