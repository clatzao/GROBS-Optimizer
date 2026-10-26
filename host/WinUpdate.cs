using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>Controle do Windows Update: pausar, adiar, desativar (reversível) e limpar o cache — manual ou agendado.</summary>
    internal static class WinUpdate
    {
        const string UX = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        const string Pol = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        const string AU = Pol + @"\AU";
        const string TaskName = @"GROBS\Limpar cache do Windows Update";
        static readonly string[] Tasks = {
            @"\Microsoft\Windows\WindowsUpdate\Scheduled Start",
            @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan",
            @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan Static Task",
            @"\Microsoft\Windows\UpdateOrchestrator\USO_UxBroker",
        };

        static RegistryKey Lm() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

        static object Val(string path, string name) { using (var k = Lm().OpenSubKey(path)) return k?.GetValue(name); }

        static bool Exists(string svc) => ServiceController.GetServices().Any(s => s.ServiceName.Equals(svc, StringComparison.OrdinalIgnoreCase));

        static int SvcStart(string svc) { using (var k = Lm().OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + svc)) return Convert.ToInt32(k?.GetValue("Start") ?? 3); }

        static bool Win10Plus => Environment.OSVersion.Version.Major >= 10;

        public static object Status()
        {
            DateTime? paused = null;
            var exp = Val(UX, "PauseUpdatesExpiryTime") as string;
            DateTime d;
            if (exp != null && DateTime.TryParse(exp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d) && d > DateTime.UtcNow) paused = d.ToLocalTime();
            bool disabled = Convert.ToInt32(Val(AU, "NoAutoUpdate") ?? 0) == 1 || SvcStart("wuauserv") == 4;
            int deferFeature = Convert.ToInt32(Val(Pol, "DeferFeatureUpdatesPeriodInDays") ?? 0);
            int deferQuality = Convert.ToInt32(Val(Pol, "DeferQualityUpdatesPeriodInDays") ?? 0);
            long cache = 0;
            try
            {
                var dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");
                if (Directory.Exists(dl)) cache = new DirectoryInfo(dl).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => { try { return f.Length; } catch { return 0L; } });
            }
            catch { }
            return new Dictionary<string, object> {
                { "pausedUntil", paused.HasValue ? paused.Value.ToString("dd/MM/yyyy") : null }, { "disabled", disabled },
                { "deferFeature", deferFeature }, { "deferQuality", deferQuality }, { "cache", (double)cache },
                { "scheduled", ScheduleInfo() }, { "canPause", Win10Plus }, { "admin", Program.IsAdmin } };
        }

        static void Need() { if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador"); }

        /// <summary>Pausa por N dias (o Windows aceita até 35).</summary>
        public static object Pause(int days)
        {
            Need();
            using (var k = Lm().CreateSubKey(UX))
            {
                if (days <= 0)
                {
                    foreach (var n in new[] { "PauseUpdatesExpiryTime", "PauseUpdatesStartTime", "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime", "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime" })
                        k.DeleteValue(n, false);
                }
                else
                {
                    string start = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    string end = DateTime.UtcNow.AddDays(Math.Min(35, days)).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    k.SetValue("PauseUpdatesStartTime", start); k.SetValue("PauseUpdatesExpiryTime", end);
                    k.SetValue("PauseFeatureUpdatesStartTime", start); k.SetValue("PauseFeatureUpdatesEndTime", end);
                    k.SetValue("PauseQualityUpdatesStartTime", start); k.SetValue("PauseQualityUpdatesEndTime", end);
                }
            }
            return Status();
        }

        /// <summary>Adia atualizações de recursos (até 365 dias) e de qualidade (até 30) por política.</summary>
        public static object Defer(int featureDays, int qualityDays)
        {
            Need();
            using (var k = Lm().CreateSubKey(Pol))
            {
                if (featureDays > 0) { k.SetValue("DeferFeatureUpdates", 1, RegistryValueKind.DWord); k.SetValue("DeferFeatureUpdatesPeriodInDays", Math.Min(365, featureDays), RegistryValueKind.DWord); }
                else { k.DeleteValue("DeferFeatureUpdates", false); k.DeleteValue("DeferFeatureUpdatesPeriodInDays", false); }
                if (qualityDays > 0) { k.SetValue("DeferQualityUpdates", 1, RegistryValueKind.DWord); k.SetValue("DeferQualityUpdatesPeriodInDays", Math.Min(30, qualityDays), RegistryValueKind.DWord); }
                else { k.DeleteValue("DeferQualityUpdates", false); k.DeleteValue("DeferQualityUpdatesPeriodInDays", false); }
            }
            return Status();
        }

        static void SetSvc(string svc, int start, bool delayed = false)
        {
            if (!Exists(svc)) return;
            try
            {
                using (var k = Lm().OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + svc, true))
                {
                    if (k == null) return;
                    k.SetValue("Start", start, RegistryValueKind.DWord);
                    if (delayed) k.SetValue("DelayedAutostart", 1, RegistryValueKind.DWord);
                }
            }
            catch { /* WaaSMedicSvc é protegido em algumas versões: segue sem ele */ }
        }

        /// <summary>Desativa totalmente (política + serviços + tarefas). Tudo é revertido por Enable().</summary>
        public static object Disable()
        {
            Need();
            using (var k = Lm().CreateSubKey(AU)) { k.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord); k.SetValue("AUOptions", 1, RegistryValueKind.DWord); }
            Services.StopTemporarily("wuauserv");
            SetSvc("wuauserv", 4);
            SetSvc("UsoSvc", 4);
            SetSvc("WaaSMedicSvc", 4);
            foreach (var t in Tasks) Schtasks("/Change /TN \"" + t + "\" /Disable");
            return Status();
        }

        public static object Enable()
        {
            Need();
            using (var k = Lm().OpenSubKey(AU, true)) { k?.DeleteValue("NoAutoUpdate", false); k?.DeleteValue("AUOptions", false); }
            SetSvc("wuauserv", 3);
            SetSvc("UsoSvc", Win10Plus ? 2 : 3, Win10Plus);
            SetSvc("WaaSMedicSvc", 3);
            foreach (var t in Tasks) Schtasks("/Change /TN \"" + t + "\" /Enable");
            Services.StartIf("wuauserv", true);
            return Status();
        }

        static string Schtasks(string args)
        {
            try { return Power.Run(Power.Sys("schtasks.exe"), args, 20000); } catch { return ""; }
        }

        /// <summary>Para os serviços, apaga os downloads já instalados e religa. Seguro: o Windows baixa de novo se precisar.</summary>
        public static object CleanCache()
        {
            Need();
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            bool wu = Services.StopTemporarily("wuauserv"), bits = Services.StopTemporarily("bits"), dosvc = Services.StopTemporarily("dosvc");
            long freed = 0; int files = 0;
            try
            {
                foreach (var dir in new[] { Path.Combine(win, @"SoftwareDistribution\Download"), Path.Combine(win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache") })
                {
                    if (!Directory.Exists(dir)) continue;
                    var root = new DirectoryInfo(dir);
                    if ((root.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    foreach (var f in root.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        try { long l = f.Length; f.Attributes = FileAttributes.Normal; f.Delete(); freed += l; files++; } catch { }
                    }
                    foreach (var d in root.GetDirectories()) { try { if ((d.Attributes & FileAttributes.ReparsePoint) == 0) d.Delete(true); } catch { } }
                }
            }
            finally
            {
                Services.StartIf("dosvc", dosvc); Services.StartIf("bits", bits); Services.StartIf("wuauserv", wu);
            }
            try { File.AppendAllText(Path.Combine(Program.DataDir, "limpeza-wu.log"), DateTime.Now.ToString("s") + " liberados " + freed + " bytes em " + files + " arquivos" + Environment.NewLine); } catch { }
            return new Dictionary<string, object> { { "freed", (double)freed }, { "files", files } };
        }

        static object ScheduleInfo()
        {
            string o = Schtasks("/Query /TN \"" + TaskName + "\" /FO LIST");
            if (string.IsNullOrWhiteSpace(o) || o.IndexOf(TaskName.Split('\\')[1], StringComparison.OrdinalIgnoreCase) < 0) return null;
            var next = o.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains(":") && (l.IndexOf("próxima", StringComparison.OrdinalIgnoreCase) >= 0 || l.StartsWith("Next Run", StringComparison.OrdinalIgnoreCase)));
            return new Dictionary<string, object> { { "next", next != null ? next.Substring(next.IndexOf(':') + 1).Trim() : "" } };
        }

        /// <summary>freq: daily | weekly | monthly | off. Roda o próprio exe com --task clean-wu, como administrador.</summary>
        public static object Schedule(string freq)
        {
            Need();
            Schtasks("/Delete /TN \"" + TaskName + "\" /F");
            if (freq != "off")
            {
                string sc = freq == "daily" ? "/SC DAILY" : freq == "monthly" ? "/SC MONTHLY /D 1" : "/SC WEEKLY /D SUN";
                string r = Schtasks("/Create /TN \"" + TaskName + "\" /TR \"\\\"" + Application.ExecutablePath + "\\\" --task clean-wu\" " + sc + " /ST 12:00 /RU SYSTEM /RL HIGHEST /F"); // SYSTEM: roda mesmo sem ninguém logado
                if (ScheduleInfo() == null) throw new InvalidOperationException("Não foi possível criar a tarefa agendada. " + r.Trim());
            }
            return Status();
        }
    }
}
