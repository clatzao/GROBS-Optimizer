using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace Grobs
{
    /// <summary>Modo Jogo automático: ao abrir um jogo fecha apps escolhidos, pausa serviços pesados, ativa o plano máximo e libera RAM; desfaz ao fechar.</summary>
    internal static class GameMode
    {
        static readonly string[] HeavyServices = { "wuauserv", "UsoSvc", "WSearch", "SysMain", "BITS" };
        static readonly List<string> _paused = new List<string>();
        static Guid _plan = Guid.Empty;
        static bool _active;

        public static Dictionary<string, object> Config()
        {
            var d = Settings.Get<object>("gameMode") as Dictionary<string, object> ?? new Dictionary<string, object>();
            if (!d.ContainsKey("enabled")) d["enabled"] = false;
            if (!d.ContainsKey("closeApps")) d["closeApps"] = true;
            if (!d.ContainsKey("apps")) d["apps"] = new object[0];
            if (!d.ContainsKey("pauseServices")) d["pauseServices"] = true;
            if (!d.ContainsKey("maxPower")) d["maxPower"] = true;
            return d;
        }

        public static bool Enabled => Convert.ToBoolean(Config()["enabled"]);
        public static object Status() => new Dictionary<string, object> { { "config", Config() }, { "active", _active }, { "paused", _paused.ToArray() }, { "admin", Program.IsAdmin } };

        public static object SetConfig(Dictionary<string, object> cfg)
        {
            Settings.Set("gameMode", cfg);
            Games.Watcher.Refresh();
            return Status();
        }

        /// <summary>power=false quando o Turbo automático do jogo já trocou o plano.</summary>
        public static void Enter(bool power)
        {
            if (_active) return;
            _active = true;
            var c = Config();
            try
            {
                if (power && Convert.ToBoolean(c["maxPower"]))
                {
                    _plan = Power.Active();
                    var plans = ((List<object>)Power.List()).Cast<Dictionary<string, object>>().ToList();
                    var pick = plans.FirstOrDefault(p => (string)p["kind"] == "ultimate") ?? plans.FirstOrDefault(p => (string)p["kind"] == "high");
                    if (pick != null) Power.SetActive((string)pick["id"]); else _plan = Guid.Empty;
                }
                if (Convert.ToBoolean(c["closeApps"]) && c["apps"] is IEnumerable apps)
                    Processes.KillByName(apps.Cast<object>().Select(Convert.ToString).Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 4) : n));
                if (Program.IsAdmin && Convert.ToBoolean(c["pauseServices"]))
                    foreach (var s in HeavyServices) if (Services.StopTemporarily(s)) _paused.Add(s);
                MemoryCleaner.OnGameStart();
            }
            catch (Exception ex) { Program.Log(ex); }
            Program.Window?.Notify("gameMode", Status());
        }

        public static void Exit()
        {
            if (!_active) return;
            _active = false;
            if (_plan != Guid.Empty) { try { Power.SetActive(_plan.ToString()); } catch { } _plan = Guid.Empty; }
            foreach (var s in _paused) Services.StartIf(s, true);
            _paused.Clear();
            Program.Window?.Notify("gameMode", Status());
        }
    }

    /// <summary>
    /// Gravação de sessões de jogo: FPS, 1% / 0,1% low, frametime, CPU (total e núcleo mais usado), GPU e temperaturas a cada segundo,
    /// e análise de gargalo (CPU × GPU). Precisa do contador de FPS (ETW, modo administrador).
    /// </summary>
    internal static class FpsLog
    {
        static readonly string Dir = Path.Combine(Program.DataDir, "sessions");
        static readonly object _lock = new object();
        static Timer _timer;
        static int _pid, _durationSec;
        static string _exe, _name;
        static bool _startedCounter;
        static DateTime _start;
        static List<Dictionary<string, object>> _seconds;
        static List<double> _frames;

        public static bool Enabled => !(Settings.Get<object>("fpsLog") is bool b) || b;
        public static bool Recording => _timer != null;

        public static object Status() => new Dictionary<string, object> {
            { "enabled", Enabled }, { "recording", Recording }, { "game", _name }, { "admin", Program.IsAdmin },
            { "elapsed", Recording ? (int)(DateTime.Now - _start).TotalSeconds : 0 }, { "duration", _durationSec },
            { "live", Recording && _seconds.Count > 0 ? _seconds[_seconds.Count - 1] : null } };

        /// <summary>exe = jogo da lista; ou pid de um app em primeiro plano (análise manual) com durationSec > 0.</summary>
        public static void Begin(string exe, int pid, int durationSec = 0)
        {
            if (!Program.IsAdmin) return;
            lock (_lock)
            {
                if (Recording) return;
                if (!FpsCounter.Running) { _startedCounter = FpsCounter.Start(); }
                _exe = exe; _pid = pid; _durationSec = durationSec;
                _name = exe != null ? Path.GetFileNameWithoutExtension(exe) : "";
                if (pid > 0) try { using (var p = Process.GetProcessById(pid)) { _name = p.ProcessName; try { _exe = p.MainModule.FileName; } catch { } } } catch { }
                _start = DateTime.Now; _seconds = new List<Dictionary<string, object>>(); _frames = new List<double>();
                _timer = new Timer(_ => Sample(), null, 1000, 1000);
            }
            Program.Window?.Notify("fpsLog", Status());
        }

        /// <summary>Análise de gargalo sob demanda: grava o app com mais FPS agora (o jogo aberto) por N segundos.</summary>
        public static object Analyze(int seconds)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("A análise usa o contador de FPS, que precisa do modo administrador.");
            if (Recording) return Status();
            if (!FpsCounter.Running) { _startedCounter = FpsCounter.Start(); Thread.Sleep(2500); }
            int best = 0, bestFps = 0;
            foreach (var pid in FpsCounter.ActivePids())
            {
                string n = "";
                try { using (var p = Process.GetProcessById(pid)) n = p.ProcessName; } catch { continue; }
                if (n.Equals("dwm", StringComparison.OrdinalIgnoreCase) || n.StartsWith("msedgewebview", StringComparison.OrdinalIgnoreCase) || n.StartsWith("GROBS", StringComparison.OrdinalIgnoreCase)) continue;
                var f = FpsCounter.Get(pid);
                if (f != null && Convert.ToInt32(f["fps"]) > bestFps) { bestFps = Convert.ToInt32(f["fps"]); best = pid; }
            }
            if (best == 0) throw new InvalidOperationException("Nenhum jogo desenhando na tela agora. Abra o jogo, comece a jogar e clique em analisar.");
            Begin(null, best, Math.Max(15, seconds));
            return Status();
        }

        static int FindPid()
        {
            var active = FpsCounter.ActivePids();
            if (_pid > 0) return active.Contains(_pid) || ProcessAlive(_pid) ? _pid : 0;
            var mine = new HashSet<int>();
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_exe))) using (p) mine.Add(p.Id);
            if (mine.Count == 0) return 0;
            var hit = active.FirstOrDefault(mine.Contains);
            if (hit != 0) return hit;
            // jogos que desenham num subprocesso
            var parents = Processes.RawParents();
            foreach (var ap in active) { int par; if (parents.TryGetValue(ap, out par) && mine.Contains(par)) return ap; }
            return mine.First();
        }

        static bool ProcessAlive(int pid) { try { using (var p = Process.GetProcessById(pid)) return !p.HasExited; } catch { return false; } }

        static void Sample()
        {
            try
            {
                int pid = FindPid();
                if (_pid > 0 && pid == 0) { End(); return; } // app analisado fechou
                var frames = pid > 0 ? FpsCounter.TakeFrames(pid) : new List<double>();
                var st = Program.Window?.LatestStats() ?? new Dictionary<string, object>();
                Func<string, double> num = k => st.ContainsKey(k) && st[k] != null ? Convert.ToDouble(st[k]) : -1;
                double core = st.ContainsKey("cores") && st["cores"] is IEnumerable cs ? cs.Cast<object>().Select(Convert.ToDouble).DefaultIfEmpty(-1).Max() : -1;
                var sec = new Dictionary<string, object> {
                    { "t", _seconds.Count + 1 }, { "fps", frames.Count }, { "ft", frames.Count > 0 ? Math.Round(frames.Average(), 2) : 0 },
                    { "cpu", Math.Round(num("cpu")) }, { "core", Math.Round(core) }, { "gpu", Math.Round(num("gpu")) },
                    { "ct", num("cpuTemp") }, { "gt", num("gpuTemp") }, { "ram", num("mem") } };
                lock (_lock) { _seconds.Add(sec); _frames.AddRange(frames); }
                if (_durationSec > 0 && _seconds.Count >= _durationSec) { End(); return; }
                if (_seconds.Count % 5 == 0) Program.Window?.Notify("fpsLog", Status());
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        public static object Stop() { End(); return Status(); }

        public static void End()
        {
            List<Dictionary<string, object>> secs; List<double> frames; string name, exe; DateTime start; bool manual;
            lock (_lock)
            {
                if (_timer == null) return;
                _timer.Dispose(); _timer = null;
                secs = _seconds; frames = _frames; name = _name; exe = _exe; start = _start; manual = _durationSec > 0;
                if (_startedCounter && !Overlay.IsOn) { FpsCounter.Stop(); _startedCounter = false; }
            }
            try
            {
                var playing = secs.Where(s => Convert.ToInt32(s["fps"]) > 0).ToList();
                if (playing.Count >= 10 && frames.Count >= 100)
                {
                    var summary = Summarize(playing, frames);
                    summary["game"] = name; summary["exe"] = exe ?? ""; summary["manual"] = manual;
                    summary["date"] = start.ToString("dd/MM/yyyy HH:mm"); summary["duration"] = secs.Count;
                    string id = start.ToString("yyyyMMdd-HHmmss") + "-" + new string(name.Where(char.IsLetterOrDigit).Take(24).ToArray());
                    summary["id"] = id;
                    Directory.CreateDirectory(Dir);
                    // até 1 hora com resolução de 1 s; sessões maiores são reduzidas para caber no gráfico
                    int step = Math.Max(1, secs.Count / 3600);
                    var data = new Dictionary<string, object> { { "summary", summary }, { "seconds", secs.Where((s, i) => i % step == 0).ToList() } };
                    File.WriteAllText(Path.Combine(Dir, id + ".json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(data));
                    Program.Window?.Notify("sessionSaved", summary);
                }
            }
            catch (Exception ex) { Program.Log(ex); }
            Program.Window?.Notify("fpsLog", Status());
        }

        static Dictionary<string, object> Summarize(List<Dictionary<string, object>> secs, List<double> frames)
        {
            var sorted = frames.OrderByDescending(f => f).ToList();
            Func<double, double> low = pct => { int n = Math.Max(1, (int)(sorted.Count * pct)); return 1000.0 / sorted.Take(n).Average(); };
            Func<string, double> avg = k => { var v = secs.Select(s => Convert.ToDouble(s[k])).Where(x => x >= 0).ToList(); return v.Count > 0 ? v.Average() : -1; };
            Func<string, double> max = k => secs.Select(s => Convert.ToDouble(s[k])).DefaultIfEmpty(-1).Max();
            int gpuB = 0, cpuB = 0, lim = 0;
            foreach (var s in secs)
            {
                double g = Convert.ToDouble(s["gpu"]), c = Convert.ToDouble(s["cpu"]), core = Convert.ToDouble(s["core"]);
                if (g >= 90) gpuB++;
                else if (core >= 90 || c >= 90) cpuB++;
                else lim++;
            }
            int total = Math.Max(1, secs.Count);
            string verdict = gpuB * 2 >= total ? "gpu" : cpuB * 2 >= total ? "cpu" : lim * 2 >= total ? "limit" : "mixed";
            double avgFps = frames.Count / (frames.Sum() / 1000.0);
            return new Dictionary<string, object> {
                { "avgFps", Math.Round(avgFps, 1) }, { "low1", Math.Round(low(0.01), 1) }, { "low01", Math.Round(low(0.001), 1) },
                { "minFps", secs.Min(s => Convert.ToInt32(s["fps"])) }, { "maxFps", secs.Max(s => Convert.ToInt32(s["fps"])) },
                { "frametime", Math.Round(frames.Average(), 2) }, { "ft99", Math.Round(sorted[Math.Min(sorted.Count - 1, sorted.Count / 100)], 2) },
                { "cpu", Math.Round(avg("cpu")) }, { "core", Math.Round(avg("core")) }, { "gpu", Math.Round(avg("gpu")) },
                { "cpuTempMax", max("ct") }, { "gpuTempMax", max("gt") },
                { "gpuBound", Math.Round(100.0 * gpuB / total) }, { "cpuBound", Math.Round(100.0 * cpuB / total) }, { "limited", Math.Round(100.0 * lim / total) },
                { "verdict", verdict } };
        }

        public static object List()
        {
            var r = new List<object>();
            if (!Directory.Exists(Dir)) return r;
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (var f in Directory.GetFiles(Dir, "*.json").OrderByDescending(x => x).Take(100))
                try
                {
                    var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                    r.Add(d["summary"]);
                }
                catch { }
            return r;
        }

        static string FileOf(string id)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..")) throw new ArgumentException();
            return Path.Combine(Dir, id + ".json");
        }

        public static object Get(string id) => new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(FileOf(id)));
        public static object Delete(string id) { File.Delete(FileOf(id)); return List(); }

        public static object SetEnabled(bool on)
        {
            Settings.Set("fpsLog", on);
            Games.Watcher.Refresh();
            return Status();
        }
    }
}
