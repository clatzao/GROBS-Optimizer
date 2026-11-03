using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;

namespace Grobs
{
    /// <summary>
    /// Mantém a janela em uso rápida quando o PC está ocupado (como o ProBalance do Process Lasso): programas em segundo plano
    /// que estão consumindo muita CPU ficam temporariamente com prioridade menor e voltam ao normal quando a carga passa.
    /// </summary>
    internal static class Responsive
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);

        static readonly HashSet<string> Exempt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Idle", "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "fontdrvhost", "Memory Compression", "MsMpEng", "NisSrv", "SecurityHealthService",
            "explorer", "audiodg", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "RuntimeBroker", "ctfmon", "sihost", "taskhostw", "conhost", "GROBS" };

        sealed class St { public string Name; public double Ticks; public int Hot, Cold; public bool Demoted; }
        static readonly Dictionary<int, St> _procs = new Dictionary<int, St>();
        static readonly object _lock = new object();
        static Timer _timer;
        static DateTime _last;
        static long _total;

        public static bool On => Settings.Get<bool>("responsive");

        public static void Restore() { if (On) Run(true); }

        static void Run(bool on)
        {
            lock (_lock)
            {
                if (on && _timer == null) { _last = DateTime.UtcNow; _timer = new Timer(_ => Tick(), null, 2000, 2000); }
                else if (!on && _timer != null) { _timer.Dispose(); _timer = null; RestoreAll(); }
            }
        }

        public static object Set(bool on) { Settings.Set("responsive", on); Run(on); return Status(); }

        public static object Status()
        {
            lock (_lock)
                return new Dictionary<string, object> { { "on", On }, { "total", _total },
                    { "demoted", _procs.Where(kv => kv.Value.Demoted).Select(kv => kv.Value.Name).Distinct().ToArray() } };
        }

        public static void Shutdown() { lock (_lock) { _timer?.Dispose(); _timer = null; RestoreAll(); } }

        static void RestoreAll()
        {
            foreach (var kv in _procs.Where(k => k.Value.Demoted).ToList()) Priority(kv.Key, ProcessPriorityClass.Normal);
            _procs.Clear();
        }

        static bool Priority(int pid, ProcessPriorityClass c)
        {
            try { using (var p = Process.GetProcessById(pid)) { p.PriorityClass = c; return true; } } catch { return false; }
        }

        static void Tick()
        {
            lock (_lock)
            {
                if (_timer == null) return;
                try
                {
                    var now = DateTime.UtcNow;
                    double ms = (now - _last).TotalMilliseconds * Environment.ProcessorCount;
                    _last = now;
                    int fg; GetWindowThreadProcessId(GetForegroundWindow(), out fg);
                    string fgName = null;
                    var seen = new HashSet<int>();
                    var usage = new Dictionary<int, double>();
                    double sum = 0;
                    var all = Process.GetProcesses();
                    foreach (var p in all)
                        using (p)
                        {
                            try
                            {
                                if (p.Id == fg) fgName = p.ProcessName;
                                if (p.Id == 0 || p.Id == 4 || Exempt.Contains(p.ProcessName)) continue;
                                double t = p.TotalProcessorTime.TotalMilliseconds;
                                seen.Add(p.Id);
                                St s;
                                if (!_procs.TryGetValue(p.Id, out s)) { _procs[p.Id] = new St { Name = p.ProcessName, Ticks = t }; continue; }
                                double u = Math.Max(0, t - s.Ticks) / ms * 100;
                                s.Ticks = t; usage[p.Id] = u; sum += u;
                            }
                            catch { } // processo protegido ou que acabou de fechar
                        }
                    foreach (var pid in _procs.Keys.Where(k => !seen.Contains(k)).ToList()) _procs.Remove(pid);
                    foreach (var kv in usage)
                    {
                        var s = _procs[kv.Key];
                        bool foreground = kv.Key == fg || (fgName != null && s.Name.Equals(fgName, StringComparison.OrdinalIgnoreCase));
                        if (s.Demoted)
                        {
                            s.Cold = kv.Value < 3 || sum < 35 ? s.Cold + 1 : 0;
                            if (foreground || s.Cold >= 3) { Priority(kv.Key, ProcessPriorityClass.Normal); s.Demoted = false; s.Hot = 0; s.Cold = 0; }
                        }
                        else if (!foreground && sum >= 50 && kv.Value >= 8)
                        {
                            if (++s.Hot >= 2)
                                try
                                {
                                    using (var p = Process.GetProcessById(kv.Key))
                                        if (p.PriorityClass == ProcessPriorityClass.Normal) { p.PriorityClass = ProcessPriorityClass.BelowNormal; s.Demoted = true; _total++; }
                                }
                                catch { }
                        }
                        else s.Hot = 0;
                    }
                }
                catch (Exception ex) { Program.Log(ex); }
            }
        }
    }

    /// <summary>Mede tarefas reais (abrir processos, mexer em arquivos pequenos) para provar se uma mudança ajudou. Sem pontuação inventada.</summary>
    internal static class SpeedTest
    {
        public static Dictionary<string, object> Run()
        {
            var spawn = new List<double>();
            string cmd = Power.Sys("cmd.exe");
            for (int i = 0; i < 30; i++)
            {
                var sw = Stopwatch.StartNew();
                using (var p = Process.Start(new ProcessStartInfo(cmd, "/c exit") { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit();
                spawn.Add(sw.Elapsed.TotalMilliseconds);
            }
            string dir = Path.Combine(Path.GetTempPath(), "grobs-speed-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            var buf = new byte[4096];
            var fw = Stopwatch.StartNew();
            try
            {
                for (int i = 0; i < 1000; i++) File.WriteAllBytes(Path.Combine(dir, i + ".bin"), buf);
                for (int i = 0; i < 1000; i++) File.ReadAllBytes(Path.Combine(dir, i + ".bin"));
                for (int i = 0; i < 1000; i++) File.Delete(Path.Combine(dir, i + ".bin"));
            }
            finally { fw.Stop(); try { Directory.Delete(dir, true); } catch { } }
            spawn.Sort();
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            Native.GlobalMemoryStatusEx(ref m);
            var st = Program.Window?.LatestStats();
            return new Dictionary<string, object> { { "spawnMs", Math.Round(spawn[spawn.Count / 2], 1) }, { "filesMs", fw.ElapsedMilliseconds }, { "ramFree", (double)m.ullAvailPhys },
                { "cpu", st != null && st.ContainsKey("cpu") ? st["cpu"] : -1 }, { "procs", Process.GetProcesses().Length }, { "date", DateTime.Now.ToString("dd/MM HH:mm") } };
        }
    }

    /// <summary>
    /// Análise do PC: mede a carga real por alguns segundos, lê o tempo de boot do Windows e o hardware, e gera achados com evidência.
    /// Cada achado tem no máximo uma ação de uma lista fechada: a IA só escolhe e explica, nunca executa comandos.
    /// </summary>
    internal static class Analyzer
    {
        static volatile bool _busy;
        static volatile string _phase = "";
        static Dictionary<string, object> _result;
        static string _error;

        static readonly HashSet<string> NoClose = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Idle", "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "fontdrvhost", "Memory Compression", "MsMpEng", "NisSrv", "SecurityHealthService", "SecurityHealthSystray",
            "explorer", "audiodg", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchIndexer", "RuntimeBroker", "ctfmon", "sihost", "taskhostw", "conhost", "spoolsv", "WmiPrvSE", "dllhost", "GROBS", "msedgewebview2", "TextInputHost", "LockApp", "SystemSettings" };

        public static object Status() => new Dictionary<string, object> { { "running", _busy }, { "phase", _phase }, { "result", _result }, { "error", _error } };
        public static Dictionary<string, object> Last => _result;

        public static object Start(int seconds)
        {
            if (_busy) return Status();
            _busy = true; _error = null;
            new Thread(() =>
            {
                try { _result = Collect(Math.Max(10, Math.Min(120, seconds))); }
                catch (Exception ex) { _error = ex.Message; Program.Log(ex); }
                _busy = false; _phase = "";
                Program.Window?.Notify("analysis", Status());
            }) { IsBackground = true, Name = "Analyzer" }.Start();
            return Status();
        }

        static void Phase(string p) { _phase = p; Program.Window?.Notify("analysis", Status()); }

        static Dictionary<string, object> F(string id, string impact, string title, string why, string evidence, string kind = null, string arg = null, string label = null, bool confirm = false)
        {
            var d = new Dictionary<string, object> { { "id", id }, { "impact", impact }, { "title", title }, { "why", why }, { "evidence", evidence } };
            if (kind != null) { d["kind"] = kind; d["arg"] = arg ?? ""; d["label"] = label; d["confirm"] = confirm; }
            return d;
        }

        static Dictionary<string, object> Boot()
        {
            var r = new Dictionary<string, object>();
            if (!Program.IsAdmin) return r;
            try
            {
                string log = "Microsoft-Windows-Diagnostics-Performance/Operational";
                var q = new EventLogQuery(log, PathType.LogName, "*[System[(EventID>=100 and EventID<=110)]]") { ReverseDirection = true };
                var slow = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                using (var rd = new EventLogReader(q))
                {
                    EventRecord e; int n = 0;
                    while ((e = rd.ReadEvent()) != null && n++ < 60)
                        using (e)
                        {
                            var x = XElement.Parse(e.ToXml());
                            var data = x.Descendants().Where(el => el.Name.LocalName == "Data").ToDictionary(el => (string)el.Attribute("Name") ?? "", el => el.Value, StringComparer.OrdinalIgnoreCase);
                            if (e.Id == 100 && !r.ContainsKey("ms"))
                            {
                                double v;
                                if (data.ContainsKey("BootTime") && double.TryParse(data["BootTime"], out v)) r["ms"] = v;
                                if (data.ContainsKey("MainPathBootTime") && double.TryParse(data["MainPathBootTime"], out v)) r["main"] = v;
                                if (data.ContainsKey("BootPostBootTime") && double.TryParse(data["BootPostBootTime"], out v)) r["post"] = v;
                                r["date"] = e.TimeCreated?.ToString("dd/MM/yyyy HH:mm");
                            }
                            else if (e.Id >= 101)
                            {
                                string name = data.ContainsKey("FriendlyName") && data["FriendlyName"].Length > 0 ? data["FriendlyName"] : data.ContainsKey("Name") ? data["Name"] : "";
                                double deg;
                                if (name.Length > 0 && data.ContainsKey("DegradationTime") && double.TryParse(data["DegradationTime"], out deg) && deg > 0)
                                    slow[name] = Math.Max(deg, slow.ContainsKey(name) ? slow[name] : 0);
                            }
                        }
                }
                r["slow"] = slow.OrderByDescending(k => k.Value).Take(6).Select(k => (object)new Dictionary<string, object> { { "name", k.Key }, { "ms", k.Value } }).ToArray();
            }
            catch (Exception ex) { Program.Log(ex); }
            return r;
        }

        static Dictionary<string, object> Collect(int seconds)
        {
            Phase("Medindo a carga real do PC");
            var first = new Dictionary<int, double>(); var last = new Dictionary<int, double>(); var names = new Dictionary<int, string>();
            var born = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Action<bool> snap = initial =>
            {
                foreach (var p in Process.GetProcesses())
                    using (p)
                        try
                        {
                            if (p.Id == 0) continue;
                            bool isNew = !names.ContainsKey(p.Id) || names[p.Id] != p.ProcessName;
                            if (isNew)
                            {
                                names[p.Id] = p.ProcessName; first[p.Id] = initial ? p.TotalProcessorTime.TotalMilliseconds : 0;
                                if (!initial) { int c; born.TryGetValue(p.ProcessName, out c); born[p.ProcessName] = c + 1; }
                            }
                            last[p.Id] = p.TotalProcessorTime.TotalMilliseconds;
                        }
                        catch { if (!names.ContainsKey(p.Id)) { names[p.Id] = p.ProcessName; if (!initial) { int c; born.TryGetValue(p.ProcessName, out c); born[p.ProcessName] = c + 1; } } }
            };
            snap(true);
            IntPtr q, cPages, cQueue;
            Native.PdhOpenQuery(null, IntPtr.Zero, out q);
            Native.PdhAddEnglishCounter(q, @"\Memory\Pages Input/sec", IntPtr.Zero, out cPages);
            Native.PdhAddEnglishCounter(q, @"\PhysicalDisk(_Total)\Avg. Disk Queue Length", IntPtr.Zero, out cQueue);
            Native.PdhCollectQueryData(q);
            var sw = Stopwatch.StartNew();
            var cpu = new List<double>(); var disk = new List<double>(); var mem = new List<double>(); var pages = new List<double>(); var dq = new List<double>();
            double tMax = -1, cTemp = -1;
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                Thread.Sleep(2000);
                snap(false);
                var st = Program.Window?.LatestStats();
                Native.PdhCollectQueryData(q);
                uint type; Native.PDH_FMT_COUNTERVALUE v;
                if (Native.PdhGetFormattedCounterValue(cPages, Native.PDH_FMT_DOUBLE | Native.PDH_FMT_NOCAP100, out type, out v) == 0 && v.CStatus <= 1) pages.Add(v.doubleValue);
                if (Native.PdhGetFormattedCounterValue(cQueue, Native.PDH_FMT_DOUBLE | Native.PDH_FMT_NOCAP100, out type, out v) == 0 && v.CStatus <= 1) dq.Add(v.doubleValue);
                if (st == null) continue;
                if (st.ContainsKey("cpu")) cpu.Add(Convert.ToDouble(st["cpu"]));
                if (st.ContainsKey("disk")) disk.Add(Convert.ToDouble(st["disk"]));
                if (st.ContainsKey("mem")) mem.Add(Convert.ToDouble(st["mem"]));
                if (st.ContainsKey("cpuTemp")) { cTemp = Convert.ToDouble(st["cpuTemp"]); tMax = Math.Max(tMax, cTemp); }
                Phase("Medindo a carga real do PC (" + (int)Math.Min(100, sw.Elapsed.TotalSeconds * 100 / seconds) + "%)");
            }
            Native.PdhCloseQuery(q);
            double elapsedMs = sw.Elapsed.TotalMilliseconds, cores = Environment.ProcessorCount;
            var groups = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase); // nome -> cpu%, RAM, qtd
            foreach (var p in Process.GetProcesses())
                using (p)
                    try
                    {
                        if (p.Id == 0) continue;
                        double[] g; if (!groups.TryGetValue(p.ProcessName, out g)) groups[p.ProcessName] = g = new double[3];
                        g[1] += p.WorkingSet64; g[2]++;
                    }
                    catch { }
            foreach (var kv in last)
            {
                double[] g; string nm = names[kv.Key];
                if (!groups.TryGetValue(nm, out g)) groups[nm] = g = new double[3]; // já fechou: conta a CPU que usou
                g[0] += Math.Max(0, kv.Value - first[kv.Key]) / elapsedMs / cores * 100;
            }
            var top = groups.OrderByDescending(g => g.Value[0] * 40 + g.Value[1] / 1e8).Take(12).Select(g => new Dictionary<string, object> {
                { "name", g.Key }, { "cpu", Math.Round(g.Value[0], 1) }, { "ramMB", Math.Round(g.Value[1] / 1048576) }, { "count", (int)g.Value[2] } }).ToList();

            Phase("Verificando boot, hardware e configuração");
            var boot = Boot();
            var sys = SysInfo.Get();
            var disks = SysInfo.Disks();
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            Native.GlobalMemoryStatusEx(ref m);
            double ramGb = m.ullTotalPhys / 1073741824.0, cpuAvg = cpu.Count > 0 ? cpu.Average() : 0, memAvg = mem.Count > 0 ? mem.Average() : m.dwMemoryLoad, diskAvg = disk.Count > 0 ? disk.Average() : 0;
            double pagesAvg = pages.Count > 0 ? pages.Average() : 0, dqAvg = dq.Count > 0 ? dq.Average() : 0;
            var fs = new List<Dictionary<string, object>>();

            // --- quem está pesando agora
            foreach (var t in top.Where(t => !NoClose.Contains((string)t["name"])))
            {
                double c = (double)t["cpu"], r = (double)t["ramMB"];
                string name = (string)t["name"];
                bool hotCpu = c >= 10, hotRam = r >= 1024 && memAvg >= 70;
                if (hotCpu || hotRam)
                    fs.Add(F("proc:" + name, c >= 20 || (hotRam && memAvg >= 85) ? "high" : "medium", name + " está pesando no PC",
                        "Se você não está usando este programa agora, fechá-lo devolve CPU e memória para o que você está fazendo. Trabalho não salvo nele é perdido.",
                        c + "% de CPU em média · " + r + " MB de RAM · " + t["count"] + " processo(s)", "close", name, "Fechar " + name, true));
            }
            var storm = born.Where(b => !NoClose.Contains(b.Key)).OrderByDescending(b => b.Value).Take(5).ToList();
            int stormTotal = storm.Sum(b => b.Value);
            if (stormTotal >= seconds / 2)
                fs.Add(F("storm", cpuAvg >= 50 ? "high" : "medium", "Algum trabalho está abrindo programas sem parar",
                    "Estes programas abrem e fecham repetidamente: costuma ser renderização de vídeo, compilação, testes automáticos ou um programa em erro. Se for um trabalho seu, deixe terminar: o PC fica lento até lá. Se não reconhecer, vale investigar.",
                    string.Join(", ", storm.Select(b => b.Key + " " + b.Value + "x")) + " em " + seconds + " s"));
            if (!Responsive.On)
                fs.Add(F("responsive", cpuAvg >= 40 ? "high" : "medium", "Manter a janela em uso sempre rápida",
                    "Quando o PC está ocupado, programas em segundo plano perdem prioridade e voltam ao normal depois. Não fecha nada. Medido: com programas gastando CPU sem parar, abrir um programa ficou de 5 a 15 vezes mais rápido; com carga variada, o ganho foi pequeno.",
                    "CPU média agora: " + Math.Round(cpuAvg) + "%", "responsive", "on", "Ligar"));

            // --- memória
            if (memAvg >= 85 || (memAvg >= 75 && pagesAvg >= 500))
                fs.Add(F("ram", "high", "Falta memória RAM", ramGb < 12 ? "O Windows está recorrendo ao disco. Feche programas ou, de verdade, instale mais RAM: é o upgrade mais barato com efeito real." : "O Windows está recorrendo ao disco. Feche os programas mais pesados da lista.",
                    Math.Round(memAvg) + "% em uso de " + Math.Round(ramGb) + " GB · " + Math.Round(pagesAvg) + " páginas lidas do disco/s"));
            if (ramGb < 8) fs.Add(F("ram-small", "high", "Pouca RAM instalada", "Com o Windows atual, menos de 8 GB deixa tudo lento. Nenhum programa substitui isso.", Math.Round(ramGb, 1) + " GB instalados"));
            int sticks = Convert.ToInt32(sys.ContainsKey("ramSticks") ? sys["ramSticks"] : 0);
            if (sticks == 1) fs.Add(F("ram-channel", "medium", "Memória em canal único", "Um só pente usa metade da velocidade de memória; isso pesa muito em vídeo integrado. Um segundo pente igual costuma dar ganho real.", "1 pente instalado"));

            // --- disco
            string sysDrive = Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\');
            var physical = ((object[])disks["physical"]).Cast<Dictionary<string, object>>().ToList();
            bool anySsd = physical.Any(d => Convert.ToString(d["type"]) != "HDD" && Convert.ToString(d["type"]) != "Disco");
            if (!anySsd && physical.Count > 0) fs.Add(F("hdd", "high", "O Windows está em HD", "Trocar por um SSD é o maior ganho possível em um PC antigo: liga e abre tudo várias vezes mais rápido.", string.Join(", ", physical.Select(d => d["name"]))));
            var di = new DriveInfo(sysDrive);
            double freePct = 100.0 * di.AvailableFreeSpace / di.TotalSize;
            long cleanable = 0; var cleanIds = new List<string>();
            try
            {
                foreach (Dictionary<string, object> c in (List<object>)Cleaner.Scan(new CancellationToken()))
                    if (Convert.ToBoolean(c["default"]) && !Convert.ToBoolean(c["locked"]) && !Convert.ToBoolean(c["running"]) && Convert.ToDouble(c["size"]) > 0) { cleanable += (long)Convert.ToDouble(c["size"]); cleanIds.Add((string)c["id"]); }
            }
            catch { }
            if (cleanable >= 500L << 20)
                fs.Add(F("clean", freePct < 15 ? "high" : "low", "Liberar espaço em disco", freePct < 15 ? "Disco quase cheio deixa o Windows e o SSD lentos." : "Libera espaço, mas não deixa o PC mais rápido enquanto houver folga no disco.",
                    (cleanable / 1073741824.0).ToString("0.0") + " GB limpáveis · " + Math.Round(freePct) + "% livres em " + sysDrive, "clean", string.Join(",", cleanIds), "Limpar"));
            if (freePct < 15) fs.Add(F("disk-full", "high", "Disco quase cheio", "Com menos de 15% livres o Windows e o SSD ficam mais lentos. Veja o que ocupa mais em Espaço em disco e mova ou apague arquivos grandes.", (di.AvailableFreeSpace / 1073741824.0).ToString("0.0") + " GB livres (" + Math.Round(freePct) + "%) em " + sysDrive));
            if (diskAvg >= 60 || dqAvg >= 2) fs.Add(F("disk-busy", "medium", "Disco muito ocupado", "O disco é o gargalo agora. Veja os programas da lista; downloads, sincronização e antivírus são os suspeitos comuns.", Math.Round(diskAvg) + "% de uso · fila de " + dqAvg.ToString("0.0")));
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_IDEController"))
                {
                    var ctrl = s.Get().Cast<ManagementObject>().Select(o => Convert.ToString(o["Name"])).ToList();
                    if (ctrl.Count > 0 && !ctrl.Any(n => n.IndexOf("AHCI", StringComparison.OrdinalIgnoreCase) >= 0) && anySsd)
                        fs.Add(F("ahci", "medium", "Controladora de disco sem AHCI", "Em modo IDE o SSD perde velocidade e TRIM. Mudar no BIOS exige preparar o Windows antes: procure um técnico ou um guia para a sua placa.", ctrl[0]));
                }
            }
            catch { }

            // --- inicialização
            var startup = ((List<object>)Startup.List()).Cast<Dictionary<string, object>>().Where(i => Convert.ToBoolean(i["enabled"])).ToList();
            var slowBoot = boot.ContainsKey("slow") ? ((object[])boot["slow"]).Cast<Dictionary<string, object>>().ToList() : new List<Dictionary<string, object>>();
            foreach (var i in startup)
            {
                string name = Convert.ToString(i["name"]), company = Convert.ToString(i["company"]), path = Convert.ToString(i["path"]);
                if (company.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0 || name.Equals("SecurityHealth", StringComparison.OrdinalIgnoreCase)) continue;
                if (company.IndexOf("Realtek", StringComparison.OrdinalIgnoreCase) >= 0 || company.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 || company.IndexOf("Advanced Micro", StringComparison.OrdinalIgnoreCase) >= 0 || company.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) continue; // áudio e vídeo
                if (Convert.ToBoolean(i["admin"]) && !Program.IsAdmin) continue;
                bool updater = System.Text.RegularExpressions.Regex.IsMatch(name, "updat|sched|helper|agent", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var sb = slowBoot.FirstOrDefault(b => Convert.ToString(b["name"]).IndexOf(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) >= 0);
                string evidence = (company.Length > 0 ? company : "Editor desconhecido") + (sb != null ? " · atrasou o boot em " + Math.Round(Convert.ToDouble(sb["ms"]) / 1000.0, 1) + " s" : " · abre em todo boot");
                if (updater) fs.Add(F("startup:" + i["id"], sb != null ? "medium" : "low", "Tirar " + name + " da inicialização", "Atualizador: não precisa abrir com o Windows.", evidence, "startup-off", (string)i["id"], "Desativar"));
                else if (Program.IsAdmin) fs.Add(F("startup:" + i["id"], sb != null ? "medium" : "low", "Abrir " + name + " 1 minuto depois do boot", "O programa continua abrindo sozinho, só depois de o Windows terminar de carregar.", evidence, "startup-delay", (string)i["id"], "Atrasar"));
            }
            if (boot.ContainsKey("ms") && Convert.ToDouble(boot["ms"]) >= 90000)
                fs.Add(F("boot", "medium", "Boot demorado", "Veja os itens de inicialização desta lista; eles somam no tempo até o PC ficar pronto.", "Último boot: " + Math.Round(Convert.ToDouble(boot["ms"]) / 1000) + " s"));
            foreach (var b in slowBoot.Where(s => !startup.Any(i => Convert.ToString(s["name"]).IndexOf(Path.GetFileName(Convert.ToString(i["path"])), StringComparison.OrdinalIgnoreCase) >= 0)).Where(s => !System.Text.RegularExpressions.Regex.IsMatch(Convert.ToString(s["name"]), "Microsoft|Windows", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).Take(3).Where(s => Convert.ToDouble(s["ms"]) >= 5000))
                fs.Add(F("slow:" + b["name"], "medium", b["name"] + " atrasa o boot", "O Windows registrou este item como causa de lentidão ao ligar. Se for um driver, atualize-o em Drivers; se for um programa, avalie removê-lo.", "Atraso de " + Math.Round(Convert.ToDouble(b["ms"]) / 1000.0, 1) + " s"));

            // --- temperatura, vídeo, sistema
            if (tMax >= 90) fs.Add(F("thermal", "high", "Processador superaquecendo", "Acima de 90 °C o processador reduz a própria velocidade. Limpar a poeira e trocar a pasta térmica dá ganho real.", "Até " + tMax + " °C durante a análise"));
            var gpus = sys.ContainsKey("gpus") ? (System.Collections.IEnumerable)sys["gpus"] : new object[0];
            foreach (Dictionary<string, object> g in gpus)
                if (Convert.ToString(g["name"]).IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0)
                    fs.Add(F("gpu-driver", "high", "Sem driver de vídeo", "O Windows está usando o driver genérico: tudo que é gráfico fica lento. Instale o driver na aba Drivers.", Convert.ToString(g["name"])));
            double upDays = Native.GetTickCount64() / 86400000.0;
            if (upDays >= 7) fs.Add(F("uptime", "low", "Reinicie o PC", "Ligado há muitos dias, programas e drivers vão acumulando memória. Inicialização Rápida não conta como reiniciar.", Math.Round(upDays) + " dias sem reiniciar"));
            var plans = ((List<object>)Power.List()).Cast<Dictionary<string, object>>().ToList();
            var active = plans.FirstOrDefault(p => Convert.ToBoolean(p["active"]));
            bool battery = Convert.ToBoolean(SysInfo.Battery()["present"]);
            if (active != null && (string)active["kind"] == "saver" && !battery)
                fs.Add(F("power", "medium", "Plano de energia em economia", "Em PC de mesa, o modo economia limita o processador sem vantagem.", "Plano atual: " + active["name"], "power", "", "Usar Alto desempenho"));

            int Rank(Dictionary<string, object> f) => (string)f["impact"] == "high" ? 0 : (string)f["impact"] == "medium" ? 1 : 2;
            var ordered = fs.OrderBy(Rank).Cast<object>().ToList();
            var hardware = new Dictionary<string, object> { { "cpu", sys.ContainsKey("cpu") ? sys["cpu"] : "" }, { "threads", (int)cores }, { "ramGB", Math.Round(ramGb) }, { "ramSticks", sticks },
                { "gpus", gpus.Cast<Dictionary<string, object>>().Select(g => g["name"]).ToArray() }, { "disks", physical.Select(d => d["name"] + " (" + d["type"] + ")").ToArray() }, { "os", sys.ContainsKey("os") ? sys["os"] : "" } };
            Phase("Medindo a velocidade atual");
            var speed = SpeedTest.Run();
            return new Dictionary<string, object> {
                { "date", DateTime.Now.ToString("dd/MM/yyyy HH:mm") }, { "seconds", seconds }, { "cpu", Math.Round(cpuAvg) }, { "mem", Math.Round(memAvg) }, { "disk", Math.Round(diskAvg) }, { "pages", Math.Round(pagesAvg) },
                { "temp", tMax }, { "freePct", Math.Round(freePct) }, { "top", top.Cast<object>().ToList() }, { "boot", boot }, { "findings", ordered }, { "hardware", hardware }, { "speed", speed }, { "admin", Program.IsAdmin } };
        }

        /// <summary>Executa a ação de um achado da última análise. Só ações geradas pelo próprio analisador passam: nada vem de fora.</summary>
        public static object Apply(string id)
        {
            var list = (_result?["findings"] as List<object>)?.Cast<Dictionary<string, object>>();
            var f = list?.FirstOrDefault(x => (string)x["id"] == id);
            if (f == null || !f.ContainsKey("kind")) throw new InvalidOperationException("Ação não encontrada. Rode a análise de novo.");
            string arg = (string)f["arg"];
            switch ((string)f["kind"])
            {
                case "close":
                    if (NoClose.Contains(arg)) throw new InvalidOperationException("Processo protegido.");
                    int n = Processes.KillByName(new[] { arg });
                    return new Dictionary<string, object> { { "text", n + " processo(s) de " + arg + " fechados" } };
                case "responsive": Responsive.Set(true); return new Dictionary<string, object> { { "text", "Ligado: a janela em uso terá prioridade quando o PC estiver ocupado" } };
                case "clean":
                    var r = (Dictionary<string, object>)Cleaner.Clean(arg.Split(','), new CancellationToken());
                    return new Dictionary<string, object> { { "text", (Convert.ToDouble(r["freed"]) / 1073741824.0).ToString("0.0") + " GB liberados" } };
                case "startup-off": Startup.SetEnabled(arg, false); return new Dictionary<string, object> { { "text", "Não abre mais com o Windows (dá para religar em Otimização)" } };
                case "startup-delay": StartupDelay.Set(arg, 60); return new Dictionary<string, object> { { "text", "Vai abrir 1 minuto depois do logon (dá para mudar em Otimização)" } };
                case "power":
                    var plans = ((List<object>)Power.List()).Cast<Dictionary<string, object>>().ToList();
                    var pick = plans.FirstOrDefault(p => (string)p["kind"] == "high") ?? plans.First(p => (string)p["kind"] == "balanced");
                    Power.SetActive((string)pick["id"]);
                    return new Dictionary<string, object> { { "text", "Plano ativo: " + pick["name"] } };
            }
            throw new InvalidOperationException("Ação desconhecida.");
        }

        public static object Measure() => SpeedTest.Run();
    }
}
