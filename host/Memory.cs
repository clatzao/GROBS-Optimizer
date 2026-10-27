using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>
    /// Liberador de RAM no estilo ISLC/Mem Reduct: esvazia a lista "standby" (cache que o Windows às vezes demora
    /// a devolver aos jogos) e, opcionalmente, os working sets. Automático por limite de uso e ao abrir um jogo.
    /// </summary>
    internal static class MemoryCleaner
    {
        const int SystemMemoryListInformation = 80;
        const int MemoryEmptyWorkingSets = 2, MemoryFlushModifiedList = 3, MemoryPurgeStandbyList = 4, MemoryPurgeLowPriorityStandbyList = 5;

        static Timer _timer;
        static DateTime _last = DateTime.MinValue;
        static int _busy;

        public static Dictionary<string, object> Config()
        {
            var d = Settings.Get<object>("ramAuto") as Dictionary<string, object> ?? new Dictionary<string, object>();
            if (!d.ContainsKey("enabled")) d["enabled"] = false;
            if (!d.ContainsKey("threshold")) d["threshold"] = 80;
            if (!d.ContainsKey("onGame")) d["onGame"] = true;
            if (!d.ContainsKey("workingSets")) d["workingSets"] = false;
            return d;
        }

        public static void StartAuto()
        {
            bool on = Convert.ToBoolean(Config()["enabled"]);
            if (!on) { _timer?.Dispose(); _timer = null; return; }
            if (_timer == null) _timer = new Timer(_ => Tick(), null, 30000, 30000);
        }

        static void Tick()
        {
            try
            {
                var c = Config();
                var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
                if (!Native.GlobalMemoryStatusEx(ref m)) return;
                if (m.dwMemoryLoad >= Convert.ToInt32(c["threshold"]) && (DateTime.Now - _last).TotalMinutes >= 2)
                    Clean(Convert.ToBoolean(c["workingSets"]), true);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        /// <summary>Chamado pelo Modo Jogo automático.</summary>
        public static void OnGameStart()
        {
            if (Program.IsAdmin && Convert.ToBoolean(Config()["onGame"])) try { Clean(Convert.ToBoolean(Config()["workingSets"]), true); } catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_MEMORY_LIST_INFORMATION
        {
            public UIntPtr ZeroPageCount, FreePageCount, ModifiedPageCount, ModifiedNoWritePageCount, BadPageCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public UIntPtr[] PageCountByPriority;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public UIntPtr[] RepurposedPagesByPriority;
            public UIntPtr ModifiedPageCountPageFile;
        }

        public static Dictionary<string, object> Status()
        {
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            Native.GlobalMemoryStatusEx(ref m);
            var r = new Dictionary<string, object> { { "total", (double)m.ullTotalPhys }, { "available", (double)m.ullAvailPhys }, { "load", m.dwMemoryLoad },
                { "admin", Program.IsAdmin }, { "config", Config() }, { "last", _last == DateTime.MinValue ? "" : _last.ToString("HH:mm:ss") } };
            if (Program.IsAdmin)
            {
                Native.EnablePrivilege("SeProfileSingleProcessPrivilege");
                int size = Marshal.SizeOf(typeof(SYSTEM_MEMORY_LIST_INFORMATION));
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    int ret;
                    if (Native.NtQuerySystemInformation(SystemMemoryListInformation, buf, size, out ret) == 0)
                    {
                        var s = (SYSTEM_MEMORY_LIST_INFORMATION)Marshal.PtrToStructure(buf, typeof(SYSTEM_MEMORY_LIST_INFORMATION));
                        double page = Environment.SystemPageSize;
                        r["standby"] = s.PageCountByPriority.Sum(x => (double)x.ToUInt64()) * page;
                        r["modified"] = (s.ModifiedPageCount.ToUInt64()) * page;
                        r["free"] = (s.FreePageCount.ToUInt64() + s.ZeroPageCount.ToUInt64()) * page;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return r;
        }

        static void Cmd(int c)
        {
            int r = Native.NtSetSystemInformation(SystemMemoryListInformation, ref c, 4);
            if (r != 0) throw new InvalidOperationException("O Windows recusou a limpeza de memória (0x" + r.ToString("X8") + ").");
        }

        /// <summary>workingSets: também reduz a memória dos programas abertos (eles recarregam do disco o que precisarem).</summary>
        public static object Clean(bool workingSets, bool auto = false)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            if (Interlocked.Exchange(ref _busy, 1) == 1) return Status();
            try
            {
                var before = Status();
                Native.EnablePrivilege("SeProfileSingleProcessPrivilege");
                Native.EnablePrivilege("SeIncreaseQuotaPrivilege");
                if (workingSets) { Cmd(MemoryEmptyWorkingSets); Cmd(MemoryFlushModifiedList); }
                Cmd(MemoryPurgeLowPriorityStandbyList);
                Cmd(MemoryPurgeStandbyList);
                _last = DateTime.Now;
                var after = Status();
                // a lista "em espera" já conta como disponível: o ganho real aparece na memória livre
                string k = before.ContainsKey("free") && after.ContainsKey("free") ? "free" : "available";
                after["freed"] = Math.Max(0, Convert.ToDouble(after[k]) - Convert.ToDouble(before[k]));
                if (auto) Program.Window?.Notify("ramCleaned", after);
                return after;
            }
            finally { _busy = 0; }
        }
    }

    /// <summary>Resolução do timer do Windows (0,5 ms) — menos input lag e frametime mais estável em alguns jogos.</summary>
    internal static class TimerRes
    {
        [DllImport("ntdll.dll")] static extern int NtSetTimerResolution(uint desired, bool set, out uint current);
        [DllImport("ntdll.dll")] static extern int NtQueryTimerResolution(out uint min, out uint max, out uint current);
        const string Kernel = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
        static bool _held;

        static int Build()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            { int b; return int.TryParse(Convert.ToString(k?.GetValue("CurrentBuildNumber")), out b) ? b : 0; }
        }

        /// <summary>Windows 10 2004+ aplica o pedido só ao próprio processo; o Windows 11 volta a ser global com GlobalTimerResolutionRequests.</summary>
        public static string Support()
        {
            int b = Build();
            if (b >= 22000) return "global";
            if (b >= 19041) return "perProcess";
            return "global";
        }

        public static void Restore() { if (Settings.Get<bool>("timerRes")) Hold(true); }

        static void Hold(bool on)
        {
            uint cur;
            if (on && !_held) { NtSetTimerResolution(5000, true, out cur); _held = true; }
            else if (!on && _held) { NtSetTimerResolution(5000, false, out cur); _held = false; }
        }

        public static object Status()
        {
            uint min, max, cur; NtQueryTimerResolution(out min, out max, out cur);
            bool reg;
            using (var k = Registry.LocalMachine.OpenSubKey(Kernel)) reg = Convert.ToInt32(k?.GetValue("GlobalTimerResolutionRequests") ?? 0) == 1;
            return new Dictionary<string, object> { { "current", cur / 10000.0 }, { "best", max / 10000.0 }, { "default", min / 10000.0 },
                { "on", Settings.Get<bool>("timerRes") }, { "support", Support() }, { "registry", reg }, { "admin", Program.IsAdmin } };
        }

        public static object Set(bool on)
        {
            if (Build() >= 22000)
            {
                if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
                using (var k = Registry.LocalMachine.CreateSubKey(Kernel))
                    if (on) k.SetValue("GlobalTimerResolutionRequests", 1, RegistryValueKind.DWord); else k.DeleteValue("GlobalTimerResolutionRequests", false);
            }
            Settings.Set("timerRes", on);
            Hold(on);
            return Status();
        }
    }

    /// <summary>
    /// Modo MSI (Message Signaled Interrupts) para GPU e placas de rede, e estacionamento de núcleos (core parking).
    /// Os dois mexem em configuração de hardware: a interface cria ponto de restauração antes.
    /// </summary>
    internal static class Latency
    {
        const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}", NetClass = "{4d36e972-e325-11ce-bfc1-08002be10318}";
        const string MsiSub = @"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";

        static RegistryKey Lm() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

        public static object MsiList()
        {
            var r = new List<object>();
            using (var pci = Lm().OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI"))
            {
                if (pci == null) return r;
                foreach (var dev in pci.GetSubKeyNames())
                    using (var dk = pci.OpenSubKey(dev))
                        foreach (var inst in dk?.GetSubKeyNames() ?? new string[0])
                            using (var ik = dk.OpenSubKey(inst))
                            {
                                string cls = Convert.ToString(ik?.GetValue("ClassGUID") ?? "").ToLowerInvariant();
                                if (cls != DisplayClass && cls != NetClass) continue;
                                if (ik.OpenSubKey("Control") == null) continue; // dispositivo não presente
                                string name = Convert.ToString(ik.GetValue("FriendlyName") ?? ik.GetValue("DeviceDesc") ?? dev);
                                if (name.Contains(";")) name = name.Substring(name.LastIndexOf(';') + 1);
                                bool on;
                                using (var mk = ik.OpenSubKey(MsiSub)) on = Convert.ToInt32(mk?.GetValue("MSISupported") ?? 0) == 1;
                                r.Add(new Dictionary<string, object> { { "id", dev + "\\" + inst }, { "name", name }, { "kind", cls == DisplayClass ? "gpu" : "net" }, { "on", on } });
                            }
            }
            return r;
        }

        public static object MsiSet(string id, bool on)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            if (id.Contains("..")) throw new ArgumentException();
            using (var k = Lm().CreateSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI\" + id + "\\" + MsiSub))
                k.SetValue("MSISupported", on ? 1 : 0, RegistryValueKind.DWord);
            return true;
        }

        const string SubProc = "54533251-82be-4824-96c1-47b60b740d00", CpMinCores = "0cc5b647-c1df-4637-891a-dec35c318583";

        public static object ParkingStatus()
        {
            string o = Power.Run(Power.Sys("powercfg.exe"), "/qh scheme_current " + SubProc + " " + CpMinCores);
            // "Índice da Configuração de Energia CA Atual: 0x00000064"
            var vals = o.Split('\n').Where(l => l.Contains("0x")).Select(l => l.Substring(l.LastIndexOf("0x") + 2).Trim()).ToList();
            int ac = -1;
            if (vals.Count >= 2) { try { ac = Convert.ToInt32(vals[vals.Count - 2], 16); } catch { } }
            return new Dictionary<string, object> { { "minCores", ac }, { "off", ac == 100 }, { "admin", Program.IsAdmin } };
        }

        /// <summary>off=true: todos os núcleos sempre acordados (no plano atual, na tomada). false: volta ao padrão do plano.</summary>
        public static object ParkingSet(bool off)
        {
            string pc = Power.Sys("powercfg.exe");
            Power.Run(pc, "-attributes " + SubProc + " " + CpMinCores + " -ATTRIB_HIDE");
            int cur = Convert.ToInt32(((Dictionary<string, object>)ParkingStatus())["minCores"]);
            if (off && cur >= 0 && cur != 100) Settings.Set("parkingPrev", cur); // valor original do plano, para desfazer
            int prev = Settings.Get<int>("parkingPrev");
            Power.Run(pc, "/setacvalueindex scheme_current " + SubProc + " " + CpMinCores + " " + (off ? 100 : prev > 0 && prev < 100 ? prev : 10));
            Power.Run(pc, "/setactive scheme_current");
            return ParkingStatus();
        }
    }
}
