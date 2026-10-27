using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;

namespace Grobs
{
    /// <summary>Otimização de unidades: TRIM no SSD / desfragmentação no HD (o mesmo "Otimizar unidades" do Windows), alinhamento e indexação.</summary>
    internal static class DriveOptimizer
    {
        static volatile string _running;
        static readonly StringBuilder _log = new StringBuilder();

        public static object Status()
        {
            var vols = new List<object>();
            var media = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // letra -> tipo de mídia (SSD/HDD) pela partição do disco físico
            try
            {
                var disks = new Dictionary<uint, string>();
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, MediaType, BusType FROM MSFT_PhysicalDisk"))
                    foreach (ManagementObject o in s.Get()) using (o)
                    {
                        uint id; if (!uint.TryParse(Convert.ToString(o["DeviceId"]), out id)) continue;
                        int m = Convert.ToInt32(o["MediaType"] ?? 0), b = Convert.ToInt32(o["BusType"] ?? 0);
                        disks[id] = b == 17 || m == 4 ? "SSD" : m == 3 ? "HDD" : "";
                    }
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
                    foreach (ManagementObject o in s.Get()) using (o)
                    {
                        char l = Convert.ToChar(o["DriveLetter"] ?? '\0');
                        string t;
                        if (l != '\0' && disks.TryGetValue(Convert.ToUInt32(o["DiskNumber"]), out t)) media[l + ":"] = t;
                    }
            }
            catch { }
            var aligned = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT DeviceID, StartingOffset FROM Win32_DiskPartition"))
                    foreach (ManagementObject p in s.Get()) using (p)
                    {
                        bool ok = Convert.ToUInt64(p["StartingOffset"]) % 4096 == 0;
                        foreach (ManagementObject ld in p.GetRelated("Win32_LogicalDisk")) using (ld) aligned[Convert.ToString(ld["DeviceID"])] = ok;
                    }
            }
            catch { }
            foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed))
            {
                string n = d.Name.TrimEnd('\\'); string m; media.TryGetValue(n, out m);
                bool al; bool hasAl = aligned.TryGetValue(n, out al);
                vols.Add(new Dictionary<string, object> { { "name", n }, { "label", d.VolumeLabel }, { "fs", d.DriveFormat }, { "media", m ?? "" },
                    { "total", (double)d.TotalSize }, { "free", (double)d.AvailableFreeSpace }, { "aligned", hasAl ? (object)al : null } });
            }
            string trim = Power.Run(Power.Sys("fsutil.exe"), "behavior query DisableDeleteNotify");
            bool trimOn = !trim.Contains("= 1"); // "NTFS DisableDeleteNotify = 0  (Desabilitado)" => TRIM ligado
            return new Dictionary<string, object> { { "volumes", vols }, { "trim", trimOn }, { "running", _running }, { "log", _log.ToString() },
                { "admin", Program.IsAdmin }, { "hibernation", Power.HibernationOn() }, { "indexing", ServiceRunning("WSearch") } };
        }

        static bool ServiceRunning(string n) { try { using (var sc = new System.ServiceProcess.ServiceController(n)) return sc.Status == System.ServiceProcess.ServiceControllerStatus.Running; } catch { return false; } }

        /// <summary>defrag X: /O = otimização adequada ao tipo de mídia (retrim no SSD, desfragmentação no HD).</summary>
        public static object Optimize(string drive)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            if (_running != null) return Status();
            drive = drive.TrimEnd('\\');
            if (drive.Length != 2 || drive[1] != ':') throw new ArgumentException();
            _running = drive; _log.Clear();
            new Thread(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo(Power.Sys("defrag.exe"), drive + " /O /U /V") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage) };
                    using (var p = Process.Start(psi))
                    {
                        string line;
                        while ((line = p.StandardOutput.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if (line.Length == 0) continue;
                            lock (_log) { _log.AppendLine(line); }
                            Program.Window?.Notify("driveOpt", new Dictionary<string, object> { { "drive", drive }, { "line", line } });
                        }
                        p.WaitForExit();
                    }
                }
                catch (Exception ex) { lock (_log) _log.AppendLine("Erro: " + ex.Message); }
                _running = null;
                Program.Window?.Notify("driveOpt", new Dictionary<string, object> { { "drive", drive }, { "done", true } });
            }) { IsBackground = true }.Start();
            return Status();
        }

        public static object SetTrim(bool on)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            Power.Run(Power.Sys("fsutil.exe"), "behavior set DisableDeleteNotify " + (on ? "0" : "1"));
            return Status();
        }

        /// <summary>Indexação da pesquisa (WSearch): desligar economiza disco e RAM, mas a busca do Iniciar fica mais lenta para arquivos.</summary>
        public static object SetIndexing(bool on)
        {
            Services.SetRaw("WSearch", on ? 2 : 4);
            return Status();
        }
    }

    /// <summary>Memória virtual (arquivo de paginação) e compressão de memória.</summary>
    internal static class Pagefile
    {
        public static object Status()
        {
            bool auto = true;
            using (var s = new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
                foreach (ManagementObject o in s.Get()) using (o) auto = Convert.ToBoolean(o["AutomaticManagedPagefile"]);
            var files = new List<object>();
            using (var s = new ManagementObjectSearcher("SELECT Name, AllocatedBaseSize, CurrentUsage, PeakUsage FROM Win32_PageFileUsage"))
                foreach (ManagementObject o in s.Get()) using (o)
                    files.Add(new Dictionary<string, object> { { "path", Convert.ToString(o["Name"]) }, { "size", Convert.ToDouble(o["AllocatedBaseSize"]) }, { "used", Convert.ToDouble(o["CurrentUsage"]) }, { "peak", Convert.ToDouble(o["PeakUsage"]) } });
            var settings = new List<object>();
            using (var s = new ManagementObjectSearcher("SELECT Name, InitialSize, MaximumSize FROM Win32_PageFileSetting"))
                foreach (ManagementObject o in s.Get()) using (o)
                    settings.Add(new Dictionary<string, object> { { "path", Convert.ToString(o["Name"]) }, { "initial", Convert.ToDouble(o["InitialSize"] ?? 0) }, { "max", Convert.ToDouble(o["MaximumSize"] ?? 0) } });
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            Native.GlobalMemoryStatusEx(ref m);
            double ramMb = m.ullTotalPhys / 1048576.0;
            // compressão de memória (Windows 10+): Get-MMAgent
            string mc = "";
            try { mc = Power.Run(Power.Sys(@"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -Command \"(Get-MMAgent).MemoryCompression\"", 20000).Trim(); } catch { }
            return new Dictionary<string, object> { { "auto", auto }, { "files", files }, { "settings", settings }, { "ramMb", Math.Round(ramMb) },
                { "compression", mc.Equals("True", StringComparison.OrdinalIgnoreCase) ? (object)true : mc.Equals("False", StringComparison.OrdinalIgnoreCase) ? (object)false : null },
                { "admin", Program.IsAdmin }, { "recommended", Recommended(ramMb) } };
        }

        /// <summary>Tamanho sugerido (MB): RAM pequena precisa de mais paginação para não faltar memória em jogos.</summary>
        static Dictionary<string, object> Recommended(double ramMb)
        {
            double init = ramMb <= 8192 ? Math.Round(ramMb * 1.5 / 1024) * 1024 : ramMb <= 16384 ? 8192 : 4096;
            double max = ramMb <= 8192 ? Math.Round(ramMb * 3 / 1024) * 1024 : ramMb <= 16384 ? 16384 : 16384;
            return new Dictionary<string, object> { { "initial", init }, { "max", max } };
        }

        /// <summary>auto=true: o Windows gerencia (recomendado). Senão, tamanho fixo no drive escolhido. Vale após reiniciar.</summary>
        public static object Set(bool auto, string drive, int initialMb, int maxMb)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            using (var s = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
                foreach (ManagementObject o in s.Get()) using (o) { o["AutomaticManagedPagefile"] = auto; o.Put(); }
            if (!auto)
            {
                drive = (drive ?? "C:").TrimEnd('\\');
                if (initialMb < 256 || maxMb < initialMb) throw new ArgumentException("Tamanhos inválidos");
                using (var s = new ManagementObjectSearcher("SELECT * FROM Win32_PageFileSetting"))
                    foreach (ManagementObject o in s.Get()) using (o) o.Delete();
                using (var cls = new ManagementClass("Win32_PageFileSetting"))
                using (var inst = cls.CreateInstance())
                {
                    inst["Name"] = drive + "\\pagefile.sys"; inst["InitialSize"] = (uint)initialMb; inst["MaximumSize"] = (uint)maxMb;
                    inst.Put();
                }
            }
            return Status();
        }

        public static object SetCompression(bool on)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            Power.Run(Power.Sys(@"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -Command \"" + (on ? "Enable" : "Disable") + "-MMAgent -MemoryCompression\"", 30000);
            return Status();
        }
    }
}
