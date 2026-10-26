using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace Grobs
{
    internal static class SysInfo
    {
        static IEnumerable<ManagementObject> Wmi(string scope, string query)
        {
            var list = new List<ManagementObject>();
            try
            {
                using (var s = new ManagementObjectSearcher(scope, query))
                    foreach (ManagementObject o in s.Get()) list.Add(o);
            }
            catch { }
            return list;
        }

        static string S(object o) => o == null ? "" : o.ToString().Trim();

        public static Dictionary<string, object> Get()
        {
            var r = new Dictionary<string, object>();
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                string name = S(k?.GetValue("ProductName"));
                int build = 0; int.TryParse(S(k?.GetValue("CurrentBuildNumber")), out build);
                if (build >= 22000) name = name.Replace("Windows 10", "Windows 11");
                r["os"] = name;
                r["osVersion"] = S(k?.GetValue("DisplayVersion")) is string dv && dv.Length > 0 ? dv : S(k?.GetValue("ReleaseId"));
                r["osBuild"] = build + "." + S(k?.GetValue("UBR"));
            }
            r["arch64"] = Environment.Is64BitOperatingSystem;
            r["proc64"] = Environment.Is64BitProcess;
            r["machine"] = Environment.MachineName;
            r["user"] = Environment.UserName;
            r["admin"] = Program.IsAdmin;

            foreach (var o in Wmi(null, "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor").Take(1))
            {
                r["cpu"] = S(o["Name"]);
                r["cpuCores"] = o["NumberOfCores"];
                r["cpuThreads"] = o["NumberOfLogicalProcessors"];
                r["cpuMaxMhz"] = o["MaxClockSpeed"];
            }
            r["gpus"] = Wmi(null, "SELECT Name, DriverVersion, AdapterRAM, VideoModeDescription FROM Win32_VideoController")
                .Select(o => (object)new Dictionary<string, object> { { "name", S(o["Name"]) }, { "driver", S(o["DriverVersion"]) }, { "mode", S(o["VideoModeDescription"]) } }).ToArray();
            var ram = Wmi(null, "SELECT Capacity, Speed, ConfiguredClockSpeed, Manufacturer FROM Win32_PhysicalMemory").ToList();
            r["ramSticks"] = ram.Count;
            r["ramSpeed"] = ram.Select(o => Convert.ToInt32(o["ConfiguredClockSpeed"] ?? o["Speed"] ?? 0)).DefaultIfEmpty(0).Max();
            r["ramTotal"] = ram.Sum(o => Convert.ToDouble(o["Capacity"] ?? 0));
            foreach (var o in Wmi(null, "SELECT Manufacturer, Product FROM Win32_BaseBoard").Take(1))
                r["board"] = (S(o["Manufacturer"]) + " " + S(o["Product"])).Trim();
            foreach (var o in Wmi(null, "SELECT SMBIOSBIOSVersion FROM Win32_BIOS").Take(1))
                r["bios"] = S(o["SMBIOSBIOSVersion"]);
            return r;
        }

        /// <summary>Volumes + saúde dos discos físicos (Storage WMI; contadores SMART exigem admin).</summary>
        public static Dictionary<string, object> Disks()
        {
            var vols = DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                .Select(d => (object)new Dictionary<string, object> {
                    { "name", d.Name.TrimEnd('\\') }, { "label", d.VolumeLabel }, { "fs", d.DriveFormat },
                    { "total", (double)d.TotalSize }, { "free", (double)d.AvailableFreeSpace } }).ToArray();

            var rel = new Dictionary<string, ManagementObject>();
            foreach (var o in Wmi(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, Temperature, TemperatureMax, Wear, PowerOnHours, ReadErrorsTotal, WriteErrorsTotal FROM MSFT_StorageReliabilityCounter"))
                rel[S(o["DeviceId"])] = o;

            var phys = new List<object>();
            foreach (var o in Wmi(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus, Size, SpindleSpeed FROM MSFT_PhysicalDisk"))
            {
                int media = Convert.ToInt32(o["MediaType"] ?? 0), health = Convert.ToInt32(o["HealthStatus"] ?? 5), bus = Convert.ToInt32(o["BusType"] ?? 0);
                var d = new Dictionary<string, object>
                {
                    { "name", S(o["FriendlyName"]) },
                    { "type", media == 4 ? "SSD" : media == 3 ? "HDD" : bus == 17 ? "NVMe" : "Disco" },
                    { "bus", bus == 17 ? "NVMe" : bus == 11 ? "SATA" : bus == 7 ? "USB" : bus == 8 ? "RAID" : "" },
                    { "health", health == 0 ? "ok" : health == 1 ? "warn" : health == 2 ? "bad" : "unknown" },
                    { "size", Convert.ToDouble(o["Size"] ?? 0) },
                };
                if (bus == 17) d["type"] = "NVMe";
                ManagementObject c;
                if (rel.TryGetValue(S(o["DeviceId"]), out c))
                {
                    d["temp"] = c["Temperature"]; d["tempMax"] = c["TemperatureMax"]; d["wear"] = c["Wear"];
                    d["hours"] = c["PowerOnHours"]; d["readErrors"] = c["ReadErrorsTotal"]; d["writeErrors"] = c["WriteErrorsTotal"];
                }
                phys.Add(d);
            }
            // Windows 7 não tem a API Storage: usa Win32_DiskDrive (status "OK"/"Pred Fail")
            if (phys.Count == 0)
                foreach (var o in Wmi(null, "SELECT Model, Size, Status, InterfaceType, MediaType FROM Win32_DiskDrive"))
                {
                    string st = S(o["Status"]);
                    phys.Add(new Dictionary<string, object> {
                        { "name", S(o["Model"]) }, { "type", S(o["Model"]).IndexOf("SSD", StringComparison.OrdinalIgnoreCase) >= 0 ? "SSD" : "Disco" },
                        { "bus", S(o["InterfaceType"]) }, { "health", st == "OK" ? "ok" : st == "Pred Fail" ? "bad" : st.Length > 0 ? "warn" : "unknown" },
                        { "size", Convert.ToDouble(o["Size"] ?? 0) } });
                }
            // SMART: previsão de falha (admin)
            bool? predict = null;
            foreach (var o in Wmi(@"root\WMI", "SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus"))
                predict = (predict ?? false) || (bool)o["PredictFailure"];

            return new Dictionary<string, object> { { "volumes", vols }, { "physical", phys.ToArray() }, { "smartFailure", predict }, { "smartAvailable", rel.Count > 0 } };
        }

        public static Dictionary<string, object> Battery()
        {
            var r = new Dictionary<string, object>();
            double design = 0, full = 0;
            foreach (var o in Wmi(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData")) design += Convert.ToDouble(o["DesignedCapacity"]);
            foreach (var o in Wmi(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity")) full += Convert.ToDouble(o["FullChargedCapacity"]);
            foreach (var o in Wmi(@"root\WMI", "SELECT CycleCount FROM BatteryCycleCount")) r["cycles"] = o["CycleCount"];
            r["present"] = design > 0;
            if (design > 0) { r["design"] = design; r["full"] = full; r["health"] = Math.Round(Math.Min(100, full / design * 100), 1); }
            return r;
        }
    }
}
