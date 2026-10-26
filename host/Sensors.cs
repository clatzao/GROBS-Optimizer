using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>
    /// Sensores completos via LibreHardwareMonitorLib: temperatura por núcleo, ventoinhas, voltagens, clocks, consumo,
    /// placa-mãe, SSD/HD, RAM e GPU. Leituras de CPU/placa-mãe exigem administrador + driver PawnIO.
    /// Só abre quando o usuário usa a aba de sensores (ou o overlay), para não pesar.
    /// </summary>
    internal static class Sensors
    {
        static Computer _pc;
        static readonly object _lock = new object();
        static DateTime _lastUpdate;
        static volatile bool _opening;
        public static string Error { get; private set; }

        public static bool Open { get { return _pc != null; } }

        public static string PawnIOVersion()
        {
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default))
            using (var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"))
                return k?.GetValue("DisplayVersion") as string;
        }

        public static void Start()
        {
            if (_pc != null || _opening) return;
            _opening = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var pc = new Computer
                    {
                        IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true,
                        IsControllerEnabled = true, IsStorageEnabled = true, IsBatteryEnabled = true, IsPsuEnabled = true,
                    };
                    pc.Open();
                    lock (_lock) _pc = pc;
                    Error = null;
                }
                catch (Exception ex) { Error = ex.Message; Program.Log(ex); }
                finally { _opening = false; }
            });
        }

        public static void Stop()
        {
            lock (_lock) { try { _pc?.Close(); } catch { } _pc = null; }
        }

        static void Update()
        {
            if (_pc == null || (DateTime.UtcNow - _lastUpdate).TotalMilliseconds < 1500) return;
            foreach (var h in _pc.Hardware) UpdateHw(h);
            _lastUpdate = DateTime.UtcNow;
        }

        static void UpdateHw(IHardware h)
        {
            try { h.Update(); } catch { }
            foreach (var s in h.SubHardware) UpdateHw(s);
        }

        static readonly Dictionary<SensorType, string> Units = new Dictionary<SensorType, string>
        {
            { SensorType.Temperature, "°C" }, { SensorType.Fan, "RPM" }, { SensorType.Voltage, "V" }, { SensorType.Clock, "MHz" },
            { SensorType.Load, "%" }, { SensorType.Power, "W" }, { SensorType.Control, "%" }, { SensorType.Level, "%" },
            { SensorType.Data, "GB" }, { SensorType.SmallData, "MB" }, { SensorType.Throughput, "B/s" }, { SensorType.Current, "A" },
            { SensorType.Frequency, "Hz" }, { SensorType.Energy, "mWh" },
        };

        static object HwToObj(IHardware h)
        {
            return new Dictionary<string, object> {
                { "name", h.Name }, { "type", h.HardwareType.ToString() },
                { "sensors", h.Sensors.Where(s => s.Value.HasValue && !(s.Value == 0 && (s.Max ?? 0) == 0 && s.SensorType != SensorType.Load && s.SensorType != SensorType.Control)) // zerados = sem permissão de leitura
                    .OrderBy(s => s.SensorType).ThenBy(s => s.Index).Select(s => (object)new Dictionary<string, object> {
                    { "name", s.Name }, { "type", s.SensorType.ToString() }, { "unit", Units.ContainsKey(s.SensorType) ? Units[s.SensorType] : "" },
                    { "value", Math.Round(s.Value ?? 0, 2) }, { "min", Math.Round(s.Min ?? 0, 2) }, { "max", Math.Round(s.Max ?? 0, 2) } }).ToList() },
                { "sub", h.SubHardware.Select(HwToObj).ToList() } };
        }

        public static object Tree()
        {
            Start();
            lock (_lock)
            {
                Update();
                return new Dictionary<string, object> {
                    { "open", _pc != null }, { "opening", _opening }, { "error", Error }, { "admin", Program.IsAdmin }, { "pawnio", PawnIOVersion() },
                    { "hardware", _pc == null ? new List<object>() : _pc.Hardware.Select(HwToObj).ToList() } };
            }
        }

        /// <summary>Melhores valores para o painel/overlay: temperatura real da CPU e da GPU.</summary>
        public static void Merge(Dictionary<string, object> stats)
        {
            if (_pc == null) return;
            lock (_lock)
            {
                Update();
                foreach (var h in _pc.Hardware)
                {
                    var temps = h.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value > 0 && s.Value < 125).ToList();
                    if (temps.Count == 0) continue;
                    if (h.HardwareType == HardwareType.Cpu)
                    {
                        var best = temps.FirstOrDefault(s => s.Name.Contains("Package") || s.Name.Contains("Tctl") || s.Name.Contains("Core Max")) ?? temps.OrderByDescending(s => s.Value).First();
                        stats["cpuTemp"] = Math.Round((double)best.Value.Value, 1); stats["cpuTempSrc"] = "sensor";
                    }
                    else if (h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd || h.HardwareType == HardwareType.GpuIntel)
                    {
                        var core = temps.FirstOrDefault(s => s.Name.Contains("Core")) ?? temps.First();
                        stats["gpuTemp"] = Math.Round((double)core.Value.Value, 1); stats["gpuTempSrc"] = "sensor";
                    }
                }
            }
        }

        static volatile string _pawnState = "";
        public static object PawnStatus() => new Dictionary<string, object> { { "state", _pawnState }, { "version", PawnIOVersion() } };

        /// <summary>Baixa o instalador oficial do PawnIO (GitHub de namazso) e instala em modo silencioso.</summary>
        public static object InstallPawnIO()
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            if (_pawnState == "working") return PawnStatus();
            _pawnState = "working";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string dir = Path.Combine(Program.DataDir, "downloads"); Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, "PawnIO_setup.exe");
                    using (var c = new WebClient()) { c.Headers[HttpRequestHeader.UserAgent] = "GROBS-Optimizer"; c.DownloadFile("https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe", file); }
                    using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, "-install -silent") { UseShellExecute = false, CreateNoWindow = true }))
                        p.WaitForExit(120000);
                    Stop(); Start(); // reabre com acesso aos sensores
                    _pawnState = PawnIOVersion() != null ? "done" : "error: o instalador não concluiu";
                }
                catch (Exception ex) { _pawnState = "error: " + ex.Message; }
                Program.Window?.Notify("pawnio", PawnStatus());
            });
            return PawnStatus();
        }
    }
}
