using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace Grobs
{
    /// <summary>Amostragem em tempo real. Só roda enquanto a janela está visível.</summary>
    internal sealed class Monitor : IDisposable
    {
        IntPtr _q;
        IntPtr _cpu, _cpuPerf, _cores, _diskR, _diskW, _diskIdle, _netR, _netS, _gpuEng, _gpuDed, _gpuShared, _thermal;
        readonly double _baseMhz;
        readonly List<uint> _kmtAdapters = new List<uint>();
        readonly List<IntPtr> _nvDevices = new List<IntPtr>();
        bool _lhm;
        int _tick;
        double _lhmCpuTemp = double.NaN;
        readonly object _lock = new object();

        public Monitor()
        {
            _baseMhz = Convert.ToDouble(Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", 0) ?? 0);

            Native.PdhOpenQuery(null, IntPtr.Zero, out _q);
            _cpu = Add(@"\Processor Information(_Total)\% Processor Utility");
            if (_cpu == IntPtr.Zero) _cpu = Add(@"\Processor(_Total)\% Processor Time");
            _cpuPerf = Add(@"\Processor Information(_Total)\% Processor Performance");
            _cores = Add(@"\Processor Information(*)\% Processor Utility");
            if (_cores == IntPtr.Zero) _cores = Add(@"\Processor(*)\% Processor Time");
            _diskR = Add(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
            _diskW = Add(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
            _diskIdle = Add(@"\PhysicalDisk(_Total)\% Idle Time");
            _netR = Add(@"\Network Interface(*)\Bytes Received/sec");
            _netS = Add(@"\Network Interface(*)\Bytes Sent/sec");
            _gpuEng = Add(@"\GPU Engine(*)\Utilization Percentage");
            _gpuDed = Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
            _gpuShared = Add(@"\GPU Adapter Memory(*)\Shared Usage");
            _thermal = Add(@"\Thermal Zone Information(*)\Temperature");
            Native.PdhCollectQueryData(_q);

            InitKmt();
            InitNvml();
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\LibreHardwareMonitor", "SELECT Name FROM Hardware"))
                    _lhm = s.Get().Count > 0;
            }
            catch { _lhm = false; }
        }

        IntPtr Add(string path)
        {
            IntPtr c;
            return Native.PdhAddEnglishCounter(_q, path, IntPtr.Zero, out c) == 0 ? c : IntPtr.Zero;
        }

        static double Value(IntPtr c, bool nocap = false)
        {
            if (c == IntPtr.Zero) return double.NaN;
            uint type; Native.PDH_FMT_COUNTERVALUE v;
            if (Native.PdhGetFormattedCounterValue(c, Native.PDH_FMT_DOUBLE | (nocap ? Native.PDH_FMT_NOCAP100 : 0), out type, out v) != 0 || v.CStatus > 1)
                return double.NaN;
            return v.doubleValue;
        }

        static List<KeyValuePair<string, double>> Array(IntPtr c, bool nocap = false)
        {
            var list = new List<KeyValuePair<string, double>>();
            if (c == IntPtr.Zero) return list;
            uint fmt = Native.PDH_FMT_DOUBLE | (nocap ? Native.PDH_FMT_NOCAP100 : 0);
            uint size = 0, count;
            if (Native.PdhGetFormattedCounterArray(c, fmt, ref size, out count, IntPtr.Zero) != Native.PDH_MORE_DATA || size == 0) return list;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Native.PdhGetFormattedCounterArray(c, fmt, ref size, out count, buf) != 0) return list;
                // PDH_FMT_COUNTERVALUE_ITEM: ponteiro do nome + valor; 24 bytes em x86 e x64, double no offset 16
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buf + i * 24;
                    uint status = (uint)Marshal.ReadInt32(item, 8);
                    if (status > 1) continue;
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                    double val = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                    list.Add(new KeyValuePair<string, double>(name, val));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return list;
        }

        void InitKmt()
        {
            try
            {
                var e = new Native.D3DKMT_ENUMADAPTERS2();
                if (Native.D3DKMTEnumAdapters2(ref e) != 0 || e.NumAdapters == 0) return;
                int sz = Marshal.SizeOf(typeof(Native.D3DKMT_ADAPTERINFO));
                e.pAdapters = Marshal.AllocHGlobal(sz * (int)e.NumAdapters);
                try
                {
                    if (Native.D3DKMTEnumAdapters2(ref e) != 0) return;
                    for (int i = 0; i < e.NumAdapters; i++)
                    {
                        var a = (Native.D3DKMT_ADAPTERINFO)Marshal.PtrToStructure(e.pAdapters + i * sz, typeof(Native.D3DKMT_ADAPTERINFO));
                        _kmtAdapters.Add(a.hAdapter);
                    }
                }
                finally { Marshal.FreeHGlobal(e.pAdapters); }
            }
            catch { }
        }

        /// <summary>Temperatura da GPU via D3DKMT_ADAPTER_PERFDATA (WDDM 2.4+, GPUs dedicadas).</summary>
        double KmtGpuTemp()
        {
            double best = double.NaN;
            if (_kmtAdapters.Count == 0) return best;
            IntPtr data = Marshal.AllocHGlobal(64);
            try
            {
                foreach (var h in _kmtAdapters)
                {
                    for (int k = 0; k < 64; k += 4) Marshal.WriteInt32(data, k, 0);
                    var q = new Native.D3DKMT_QUERYADAPTERINFO { hAdapter = h, Type = 62, pPrivateDriverData = data, PrivateDriverDataSize = 64 };
                    if (Native.D3DKMTQueryAdapterInfo(ref q) != 0) continue;
                    uint deci = (uint)Marshal.ReadInt32(data, 56);
                    if (deci > 0 && deci < 1300) best = double.IsNaN(best) ? deci / 10.0 : Math.Max(best, deci / 10.0);
                }
            }
            catch { }
            finally { Marshal.FreeHGlobal(data); }
            return best;
        }

        void InitNvml()
        {
            try
            {
                if (Native.nvmlInit() != 0) return;
                uint n; if (Native.nvmlDeviceGetCount(out n) != 0) return;
                for (uint i = 0; i < n; i++) { IntPtr d; if (Native.nvmlDeviceGetHandleByIndex(i, out d) == 0) _nvDevices.Add(d); }
            }
            catch { /* sem driver NVIDIA */ }
        }

        double LhmCpuTemp()
        {
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\LibreHardwareMonitor",
                    "SELECT Name, Value, Parent FROM Sensor WHERE SensorType='Temperature'"))
                {
                    double best = double.NaN;
                    foreach (ManagementObject o in s.Get())
                    {
                        string parent = (o["Parent"] ?? "").ToString(), name = (o["Name"] ?? "").ToString();
                        if (!parent.Contains("cpu")) continue;
                        double v = Convert.ToDouble(o["Value"]);
                        if (name.Contains("Package") || name.Contains("Tctl") || name.Contains("Core Max")) return v;
                        if (double.IsNaN(best) || v > best) best = v;
                    }
                    return best;
                }
            }
            catch { _lhm = false; return double.NaN; }
        }

        static double Clean(double v) => double.IsNaN(v) || double.IsInfinity(v) ? -1 : Math.Round(v, 1);

        Dictionary<string, object> _last;
        DateTime _lastAt;

        /// <summary>Última amostra se for recente; senão coleta outra. Interface e overlay compartilham a mesma coleta.</summary>
        public Dictionary<string, object> Latest(int maxAgeMs)
        {
            lock (_lock)
            {
                if (_last != null && (DateTime.UtcNow - _lastAt).TotalMilliseconds < maxAgeMs) return _last;
                return Sample();
            }
        }

        public Dictionary<string, object> Sample()
        {
            lock (_lock)
            {
                _last = SampleCore();
                _lastAt = DateTime.UtcNow;
                return _last;
            }
        }

        Dictionary<string, object> SampleCore()
        {
            lock (_lock)
            {
                Native.PdhCollectQueryData(_q);
                _tick++;
                var r = new Dictionary<string, object>();

                // CPU
                double cpu = Math.Min(100, Value(_cpu, true));
                r["cpu"] = Clean(cpu);
                double perf = Value(_cpuPerf, true);
                r["cpuMhz"] = _baseMhz > 0 && !double.IsNaN(perf) ? Math.Round(_baseMhz * perf / 100) : -1;
                var cores = Array(_cores, true)
                    .Where(k => !k.Key.Contains("_Total"))
                    .Select(k => new { k.Key, k.Value, idx = CoreIndex(k.Key) })
                    .OrderBy(k => k.idx)
                    .Select(k => (object)Math.Round(Math.Min(100, k.Value), 1)).ToArray();
                r["cores"] = cores;

                // memória
                var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
                Native.GlobalMemoryStatusEx(ref m);
                r["memTotal"] = (double)m.ullTotalPhys;
                r["memUsed"] = (double)(m.ullTotalPhys - m.ullAvailPhys);
                r["mem"] = m.dwMemoryLoad;
                Native.PERFORMANCE_INFORMATION pi;
                if (Native.GetPerformanceInfo(out pi, (uint)Marshal.SizeOf(typeof(Native.PERFORMANCE_INFORMATION))))
                {
                    double page = (double)pi.PageSize.ToUInt64();
                    r["commit"] = pi.CommitTotal.ToUInt64() * page;
                    r["commitLimit"] = pi.CommitLimit.ToUInt64() * page;
                    r["cached"] = pi.SystemCache.ToUInt64() * page;
                    r["procs"] = pi.ProcessCount;
                    r["threads"] = pi.ThreadCount;
                    r["handles"] = pi.HandleCount;
                }

                // disco
                r["diskRead"] = Clean(Value(_diskR, true));
                r["diskWrite"] = Clean(Value(_diskW, true));
                double idle = Value(_diskIdle, true);
                r["disk"] = double.IsNaN(idle) ? -1 : Math.Round(Math.Max(0, Math.Min(100, 100 - idle)), 1);

                // rede
                r["netDown"] = Math.Round(Array(_netR, true).Sum(k => k.Value));
                r["netUp"] = Math.Round(Array(_netS, true).Sum(k => k.Value));

                // GPU: mesma regra do Gerenciador de Tarefas (motor mais ocupado)
                var eng = new Dictionary<string, double>();
                foreach (var k in Array(_gpuEng, true))
                {
                    int li = k.Key.IndexOf("luid_", StringComparison.Ordinal), ei = k.Key.IndexOf("_eng_", StringComparison.Ordinal);
                    if (li < 0 || ei < 0) continue;
                    string key = k.Key.Substring(li, ei - li) + k.Key.Substring(ei);
                    double cur; eng.TryGetValue(key, out cur); eng[key] = cur + k.Value;
                }
                r["gpu"] = eng.Count > 0 ? Math.Round(Math.Min(100, eng.Values.Max()), 1) : -1;
                r["gpuMem"] = Array(_gpuDed, true).Sum(k => k.Value);
                r["gpuShared"] = Array(_gpuShared, true).Sum(k => k.Value);

                // temperaturas
                double cpuTemp = double.NaN; string cpuSrc = "";
                if (_lhm && _tick % 2 == 1) _lhmCpuTemp = LhmCpuTemp();
                if (_lhm && !double.IsNaN(_lhmCpuTemp)) { cpuTemp = _lhmCpuTemp; cpuSrc = "LibreHardwareMonitor"; }
                else
                {
                    var zones = Array(_thermal, true).Select(k => k.Value - 273.15).Where(v => v > 5 && v < 125).ToList();
                    if (zones.Count > 0) { cpuTemp = zones.Max(); cpuSrc = "ACPI"; }
                }
                r["cpuTemp"] = Clean(cpuTemp);
                r["cpuTempSrc"] = cpuSrc;

                double gpuTemp = double.NaN; string gpuSrc = "";
                foreach (var d in _nvDevices)
                {
                    uint t; if (Native.nvmlDeviceGetTemperature(d, 0, out t) == 0) { gpuTemp = double.IsNaN(gpuTemp) ? t : Math.Max(gpuTemp, t); gpuSrc = "NVIDIA"; }
                    uint fan; if (Native.nvmlDeviceGetFanSpeed(d, out fan) == 0) r["gpuFan"] = fan;
                    uint mw; if (Native.nvmlDeviceGetPowerUsage(d, out mw) == 0) r["gpuPower"] = Math.Round(mw / 1000.0, 1);
                }
                if (double.IsNaN(gpuTemp)) { gpuTemp = KmtGpuTemp(); if (!double.IsNaN(gpuTemp)) gpuSrc = "WDDM"; }
                r["gpuTemp"] = Clean(gpuTemp);
                r["gpuTempSrc"] = gpuSrc;

                // bateria
                Native.SYSTEM_POWER_STATUS ps;
                if (Native.GetSystemPowerStatus(out ps) && ps.BatteryFlag != 128 && ps.BatteryFlag != 255)
                {
                    r["battery"] = ps.BatteryLifePercent;
                    r["charging"] = ps.ACLineStatus == 1;
                }
                r["uptime"] = Native.GetTickCount64() / 1000;
                if (Sensors.Open) { try { Sensors.Merge(r); } catch { } }
                return r;
            }
        }

        static int CoreIndex(string name)
        {
            // "0,5" (Processor Information) ou "5" (Processor)
            var parts = name.Split(',');
            int a, b;
            if (parts.Length == 2 && int.TryParse(parts[0], out a) && int.TryParse(parts[1], out b)) return a * 1000 + b;
            return int.TryParse(name, out a) ? a : 99999;
        }

        public void Dispose()
        {
            if (_q != IntPtr.Zero) { Native.PdhCloseQuery(_q); _q = IntPtr.Zero; }
        }
    }
}
