using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Grobs
{
    internal static class Schtask
    {
        public static string Run(string args) { try { return Power.Run(Power.Sys("schtasks.exe"), args, 20000); } catch { return ""; } }

        public static string NextRun(string name)
        {
            string o = Run("/Query /TN \"" + name + "\" /FO LIST");
            if (string.IsNullOrWhiteSpace(o) || o.IndexOf(name.Split('\\').Last(), StringComparison.OrdinalIgnoreCase) < 0) return null;
            var next = o.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains(":") && (l.IndexOf("próxima", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("pr¢xima", StringComparison.OrdinalIgnoreCase) >= 0 || l.StartsWith("Next Run", StringComparison.OrdinalIgnoreCase)));
            return next != null ? next.Substring(next.IndexOf(':') + 1).Trim() : "";
        }

        public static string Quote(string s) => s.Replace("\"", "\\\"");
    }

    /// <summary>Prioridade de CPU fixa por programa (Image File Execution Options\PerfOptions): o Windows aplica sempre que o programa abre.</summary>
    internal static class AppPriority
    {
        const string Ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
        static readonly Dictionary<int, string> Levels = new Dictionary<int, string> { { 1, "idle" }, { 5, "below" }, { 2, "normal" }, { 6, "above" }, { 3, "high" } };

        static RegistryKey Lm() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

        public static object List()
        {
            var r = new List<object>();
            using (var k = Lm().OpenSubKey(Ifeo))
                foreach (var exe in k?.GetSubKeyNames() ?? new string[0])
                    using (var p = k.OpenSubKey(exe + @"\PerfOptions"))
                    {
                        if (p == null) continue;
                        int c = Convert.ToInt32(p.GetValue("CpuPriorityClass") ?? 0);
                        string lvl; if (!Levels.TryGetValue(c, out lvl)) continue;
                        r.Add(new Dictionary<string, object> { { "exe", exe }, { "level", lvl }, { "io", p.GetValue("IoPriority") != null } });
                    }
            return r;
        }

        /// <summary>level: idle | below | normal | above | high | remove.</summary>
        public static object Set(string exe, string level)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            exe = Path.GetFileName(exe ?? "");
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || exe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Informe um programa .exe");
            string key = Ifeo + "\\" + exe + @"\PerfOptions";
            if (level == "remove") { Lm().DeleteSubKeyTree(key, false); return List(); }
            int v = Levels.First(x => x.Value == level).Key;
            using (var k = Lm().CreateSubKey(key))
            {
                k.SetValue("CpuPriorityClass", v, RegistryValueKind.DWord);
                if (v == 3 || v == 6) k.SetValue("IoPriority", 3, RegistryValueKind.DWord); else k.DeleteValue("IoPriority", false);
            }
            return List();
        }
    }

    /// <summary>Atraso na inicialização: desativa a entrada normal e cria uma tarefa "ao fazer logon" com atraso.</summary>
    internal static class StartupDelay
    {
        const string Folder = @"GROBS\Atraso\";

        static Dictionary<string, object> Map() => Settings.Get<object>("startupDelays") as Dictionary<string, object> ?? new Dictionary<string, object>();

        public static object List() => Map();

        public static object Set(string id, int seconds)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            var map = Map();
            var entry = ((List<object>)Startup.List()).Cast<Dictionary<string, object>>().FirstOrDefault(e => (string)e["id"] == id);
            object old; map.TryGetValue(id, out old);
            string task = Folder + new string(id.Split('|').Last().Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_').ToArray()).Trim();
            Schtask.Run("/Delete /TN \"" + task + "\" /F");
            if (seconds <= 0)
            {
                map.Remove(id);
                if (entry != null) Startup.SetEnabled(id, true);
            }
            else
            {
                if (entry == null) throw new InvalidOperationException("Item de inicialização não encontrado");
                string cmd = Convert.ToString(entry["command"]);
                string exe = Convert.ToString(entry["path"]);
                // pasta Inicializar: o comando é o próprio atalho/arquivo
                string tr = cmd.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || File.Exists(cmd) ? "\"" + (File.Exists(exe) ? exe : cmd) + "\"" : cmd;
                if (tr.Length > 250) throw new InvalidOperationException("Comando longo demais para uma tarefa agendada");
                string user = Environment.UserDomainName + "\\" + Environment.UserName;
                string r = Schtask.Run("/Create /TN \"" + task + "\" /TR \"" + Schtask.Quote(tr) + "\" /SC ONLOGON /RU \"" + user + "\" /IT /DELAY " + (seconds / 60).ToString("0000") + ":" + (seconds % 60).ToString("00") + " /F");
                if (Schtask.NextRun(task) == null) throw new InvalidOperationException("Não foi possível criar a tarefa. " + r.Trim());
                Startup.SetEnabled(id, false);
                map[id] = new Dictionary<string, object> { { "seconds", seconds }, { "task", task }, { "name", entry["name"] } };
            }
            Settings.Set("startupDelays", map);
            return map;
        }
    }

    /// <summary>Pontos de restauração: listar, criar (automático antes de ajustes de risco) e restaurar.</summary>
    internal static class RestorePoints
    {
        static bool _sessionDone;

        public static object List()
        {
            var r = new List<object>();
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\default", "SELECT * FROM SystemRestore"))
                    foreach (ManagementObject o in s.Get())
                        using (o)
                        {
                            DateTime t = ManagementDateTimeConverter.ToDateTime(Convert.ToString(o["CreationTime"]));
                            r.Add(new Dictionary<string, object> { { "seq", Convert.ToInt64(o["SequenceNumber"]) }, { "desc", Convert.ToString(o["Description"]) }, { "date", t.ToString("dd/MM/yyyy HH:mm") }, { "ts", t.Ticks } });
                        }
            }
            catch { }
            return r.Cast<Dictionary<string, object>>().OrderByDescending(x => (long)x["ts"]).Cast<object>().ToList();
        }

        public static object Status() => new Dictionary<string, object> { { "points", List() }, { "auto", Auto }, { "admin", Program.IsAdmin } };

        static bool Auto => !(Settings.Get<object>("autoRestorePoint") is bool b) || b;

        /// <summary>Uma vez por sessão, antes do primeiro ajuste de risco moderado/avançado.</summary>
        public static void Ensure()
        {
            if (_sessionDone || !Auto || !Program.IsAdmin) return;
            _sessionDone = true;
            try { Tools.CreateRestorePoint("GROBS Optimizer — antes de ajustes"); Program.Window?.Notify("restorePoint", true); }
            catch (Exception ex) { Program.Log(ex); }
        }

        public static object Create(string desc)
        {
            Tools.CreateRestorePoint(string.IsNullOrWhiteSpace(desc) ? "GROBS Optimizer" : desc);
            _sessionDone = true;
            return Status();
        }

        public static object EnableProtection()
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            string drive = Path.GetPathRoot(Environment.SystemDirectory);
            Power.Run(Power.Sys(@"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -Command \"Enable-ComputerRestore -Drive '" + drive + "'\"", 60000);
            return Status();
        }

        /// <summary>Restaura e reinicia o PC em 15 s (a restauração acontece durante a reinicialização).</summary>
        public static object Restore(long seq)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            using (var c = new ManagementClass(@"\\.\root\default", "SystemRestore", null))
            {
                var p = c.GetMethodParameters("Restore");
                p["SequenceNumber"] = (uint)seq;
                var r = c.InvokeMethod("Restore", p, null);
                uint code = Convert.ToUInt32(r["ReturnValue"]);
                if (code != 0) throw new InvalidOperationException("O Windows recusou a restauração (" + code + ")");
            }
            Process.Start(new ProcessStartInfo(Power.Sys("shutdown.exe"), "/r /t 15 /c \"GROBS Optimizer: restaurando o sistema\"") { CreateNoWindow = true, UseShellExecute = false });
            return true;
        }
    }

    /// <summary>Limpeza agendada geral (temporários, caches): roda o próprio exe com --task clean no horário escolhido.</summary>
    internal static class CleanSchedule
    {
        const string Task = @"GROBS\Limpeza agendada";

        public static object Status()
        {
            var cfg = Settings.Get<object>("cleanSchedule") as Dictionary<string, object> ?? new Dictionary<string, object> { { "freq", "off" }, { "time", "12:00" }, { "ids", new object[0] } };
            cfg["next"] = Schtask.NextRun(Task);
            cfg["admin"] = Program.IsAdmin;
            cfg["last"] = Settings.Get<string>("cleanLast");
            return cfg;
        }

        public static object Set(string freq, string time, string[] ids)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            if (!System.Text.RegularExpressions.Regex.IsMatch(time ?? "", @"^\d{2}:\d{2}$")) time = "12:00";
            Schtask.Run("/Delete /TN \"" + Task + "\" /F");
            if (freq != "off")
            {
                string sc = freq == "daily" ? "/SC DAILY" : freq == "monthly" ? "/SC MONTHLY /D 1" : "/SC WEEKLY /D SUN";
                // na conta do usuário (as pastas temporárias e caches são do perfil dele), com privilégios elevados
                string r = Schtask.Run("/Create /TN \"" + Task + "\" /TR \"\\\"" + Application.ExecutablePath + "\\\" --task clean\" " + sc + " /ST " + time + " /RL HIGHEST /F");
                if (Schtask.NextRun(Task) == null) throw new InvalidOperationException("Não foi possível criar a tarefa agendada. " + r.Trim());
            }
            Settings.Set("cleanSchedule", new Dictionary<string, object> { { "freq", freq }, { "time", time }, { "ids", ids } });
            return Status();
        }

        /// <summary>Modo --task clean.</summary>
        public static void RunTask()
        {
            var cfg = Settings.Get<object>("cleanSchedule") as Dictionary<string, object>;
            var ids = (cfg?["ids"] as IEnumerable)?.Cast<object>().Select(Convert.ToString).ToArray() ?? new string[0];
            if (ids.Length == 0) ids = new[] { "usertemp", "wintemp", "thumbs", "wer", "dumps" };
            var r = (Dictionary<string, object>)Cleaner.Clean(ids, new System.Threading.CancellationToken());
            Settings.Set("cleanLast", DateTime.Now.ToString("dd/MM/yyyy HH:mm") + " · " + Math.Round(Convert.ToDouble(r["freed"]) / 1048576) + " MB");
        }
    }

    internal static class BatteryReport
    {
        public static object Open()
        {
            string file = Path.Combine(Program.DataDir, "relatorio-bateria.html");
            Power.Run(Power.Sys("powercfg.exe"), "/batteryreport /output \"" + file + "\"", 30000);
            if (!File.Exists(file)) throw new InvalidOperationException("O Windows não gerou o relatório (este PC tem bateria?)");
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            return true;
        }
    }

    /// <summary>Exporta/importa os ajustes do GROBS (configurações, ajustes do Windows, serviços desativados, efeitos visuais) para outro PC.</summary>
    internal static class Backup
    {
        static readonly string[] SkipKeys = { "modules", "diskHealth", "updateChecked", "scannedGames", "customGames", "startupDelays", "profileUndo", "cleanLast", "parkingPrev", "benchHistory" };

        public static string Build()
        {
            var settings = Settings.All().Where(kv => !SkipKeys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            var tweaks = ((List<object>)Tweaks.List()).Cast<Dictionary<string, object>>().Where(t => Convert.ToBoolean(t["on"])).Select(t => t["id"]).ToArray();
            var services = ((List<object>)Services.List()).Cast<Dictionary<string, object>>().Where(s => Convert.ToBoolean(s["disabled"])).Select(s => s["name"]).ToArray();
            var vfx = ((List<object>)VisualFx.List()).Cast<Dictionary<string, object>>().ToDictionary(f => (string)f["id"], f => f["on"]);
            var data = new Dictionary<string, object> { { "app", "GROBS Optimizer" }, { "version", Program.Version }, { "date", DateTime.Now.ToString("s") }, { "machine", Environment.MachineName },
                { "settings", settings }, { "tweaks", tweaks }, { "services", services }, { "vfx", vfx } };
            return new JavaScriptSerializer().Serialize(data);
        }

        public static object Import(string file)
        {
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
            if (Convert.ToString(d.ContainsKey("app") ? d["app"] : "") != "GROBS Optimizer") throw new InvalidOperationException("Arquivo não é um backup do GROBS Optimizer");
            int changed = 0, skipped = 0;
            if (d["settings"] is Dictionary<string, object> s)
                foreach (var kv in s) if (!SkipKeys.Contains(kv.Key)) Settings.Set(kv.Key, kv.Value);
            var wantOn = new HashSet<string>(((d["tweaks"] as IEnumerable) ?? new object[0]).Cast<object>().Select(Convert.ToString));
            foreach (Dictionary<string, object> t in (List<object>)Tweaks.List())
            {
                bool on = Convert.ToBoolean(t["on"]), want = wantOn.Contains((string)t["id"]);
                if (on == want) continue;
                if (Convert.ToBoolean(t["locked"])) { skipped++; continue; }
                try { Tweaks.Set((string)t["id"], want); changed++; } catch { skipped++; }
            }
            if (Program.IsAdmin)
            {
                var svc = new HashSet<string>(((d["services"] as IEnumerable) ?? new object[0]).Cast<object>().Select(Convert.ToString), StringComparer.OrdinalIgnoreCase);
                foreach (Dictionary<string, object> x in (List<object>)Services.List())
                {
                    bool dis = Convert.ToBoolean(x["disabled"]), want = svc.Contains((string)x["name"]);
                    if (dis != want) try { Services.Set((string)x["name"], want); changed++; } catch { skipped++; }
                }
            }
            else skipped += ((d["services"] as IEnumerable) ?? new object[0]).Cast<object>().Count();
            if (d["vfx"] is Dictionary<string, object> v)
                foreach (Dictionary<string, object> f in (List<object>)VisualFx.List())
                {
                    object want; if (!v.TryGetValue((string)f["id"], out want)) continue;
                    if (Convert.ToBoolean(want) != Convert.ToBoolean(f["on"])) try { VisualFx.SetOne((string)f["id"], Convert.ToBoolean(want)); changed++; } catch { skipped++; }
                }
            return new Dictionary<string, object> { { "changed", changed }, { "skipped", skipped }, { "from", d.ContainsKey("machine") ? d["machine"] : "" }, { "date", d.ContainsKey("date") ? d["date"] : "" } };
        }
    }

    /// <summary>Perfis de 1 clique. Cada perfil guarda o estado anterior e pode ser desfeito.</summary>
    internal static class Profiles
    {
        sealed class P { public string Id, Plan, Vfx; public string[] Tweaks; public bool Ram, Parking; }

        static readonly P[] All = {
            new P { Id = "game", Plan = "max", Tweaks = new[] { "gamemode", "gamedvr", "mouseaccel", "lat_mmcss", "lat_netthrottle", "lat_powerthrottle" }, Ram = true },
            new P { Id = "work", Plan = "balanced", Vfx = "balanced", Tweaks = new[] { "suggestions", "tips", "lockads", "welcome", "bing", "feedback" } },
            new P { Id = "laptop", Plan = "balanced", Vfx = "performance", Tweaks = new[] { "transparency", "animations", "bgapps", "edgeboost", "bgpolicy" } },
            new P { Id = "max", Plan = "max", Vfx = "performance", Tweaks = new[] { "transparency", "animations", "bgapps", "gamedvr", "menudelay", "lat_mmcss", "lat_netthrottle", "lat_powerthrottle", "edgeboost" }, Ram = true, Parking = true },
        };

        public static object Status()
        {
            var u = Settings.Get<object>("profileUndo") as Dictionary<string, object>;
            return new Dictionary<string, object> { { "active", u != null ? u["profile"] : null }, { "admin", Program.IsAdmin } };
        }

        public static object Apply(string id)
        {
            var p = All.First(x => x.Id == id);
            if (Settings.Get<object>("profileUndo") != null) Undo();
            RestorePoints.Ensure();
            var tw = ((List<object>)Tweaks.List()).Cast<Dictionary<string, object>>().ToDictionary(t => (string)t["id"], t => t);
            var vfx = ((List<object>)VisualFx.List()).Cast<Dictionary<string, object>>().ToDictionary(f => (string)f["id"], f => f["on"]);
            var changed = new List<object>(); int skipped = 0;
            foreach (var t in p.Tweaks)
            {
                Dictionary<string, object> cur;
                if (!tw.TryGetValue(t, out cur) || Convert.ToBoolean(cur["on"])) continue;
                if (Convert.ToBoolean(cur["locked"])) { skipped++; continue; }
                try { Tweaks.Set(t, true); changed.Add(t); } catch { skipped++; }
            }
            string plan = Power.Active().ToString();
            if (p.Plan == "max")
            {
                var plans = ((List<object>)Power.List()).Cast<Dictionary<string, object>>().ToList();
                if (!plans.Any(x => (string)x["kind"] == "ultimate")) try { plans = ((List<object>)Power.UnlockUltimate()).Cast<Dictionary<string, object>>().ToList(); } catch { }
                var pick = plans.FirstOrDefault(x => (string)x["kind"] == "ultimate") ?? plans.FirstOrDefault(x => (string)x["kind"] == "high");
                if (pick != null) Power.SetActive((string)pick["id"]);
            }
            else Power.SetActive(Power.Balanced.ToString());
            if (p.Vfx != null) VisualFx.Preset(p.Vfx);
            bool parking = false;
            if (p.Parking && Program.IsAdmin) { try { parking = !Convert.ToBoolean(((Dictionary<string, object>)Latency.ParkingStatus())["off"]); if (parking) Latency.ParkingSet(true); } catch { parking = false; } }
            if (p.Ram && Program.IsAdmin) try { MemoryCleaner.Clean(false); } catch { }
            Settings.Set("profileUndo", new Dictionary<string, object> { { "profile", id }, { "tweaks", changed.ToArray() }, { "plan", plan }, { "vfx", p.Vfx != null ? vfx : null }, { "parking", parking } });
            return new Dictionary<string, object> { { "active", id }, { "changed", changed.Count }, { "skipped", skipped } };
        }

        public static object Undo()
        {
            var u = Settings.Get<object>("profileUndo") as Dictionary<string, object>;
            if (u == null) return Status();
            foreach (var t in ((u["tweaks"] as IEnumerable) ?? new object[0]).Cast<object>().Select(Convert.ToString)) try { Tweaks.Set(t, false); } catch { }
            try { Power.SetActive(Convert.ToString(u["plan"])); } catch { }
            if (u["vfx"] is Dictionary<string, object> v)
                foreach (Dictionary<string, object> f in (List<object>)VisualFx.List())
                {
                    object want; if (v.TryGetValue((string)f["id"], out want) && Convert.ToBoolean(want) != Convert.ToBoolean(f["on"])) try { VisualFx.SetOne((string)f["id"], Convert.ToBoolean(want)); } catch { }
                }
            if (Convert.ToBoolean(u["parking"])) try { Latency.ParkingSet(false); } catch { }
            Settings.Set("profileUndo", null);
            return Status();
        }
    }
}
