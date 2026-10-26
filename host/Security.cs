using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>Central de segurança usando o motor do Microsoft Defender (sem motor antivírus próprio).</summary>
    internal static class Security
    {
        static readonly object _lock = new object();
        static Process _scan;
        static string _scanType = "";
        static DateTime _scanStart;
        static Dictionary<string, object> _lastResult;

        static string S(object o) => o == null ? "" : o.ToString();

        static DateTime? WmiDate(object o)
        {
            try { return o == null ? (DateTime?)null : ManagementDateTimeConverter.ToDateTime(o.ToString()); } catch { return null; }
        }

        static string MpCmdRun()
        {
            try
            {
                var plat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows Defender\Platform");
                if (Directory.Exists(plat))
                {
                    var best = Directory.GetDirectories(plat).OrderByDescending(d => d).Select(d => Path.Combine(d, "MpCmdRun.exe")).FirstOrDefault(File.Exists);
                    if (best != null) return best;
                }
            }
            catch { }
            string pf = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return Path.Combine(pf, @"Windows Defender\MpCmdRun.exe");
        }

        static int RegInt(RegistryHive hive, string path, string name, int def)
        {
            using (var root = RegistryKey.OpenBaseKey(hive, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default))
            using (var k = root.OpenSubKey(path))
            {
                var v = k?.GetValue(name);
                return v == null ? def : Convert.ToInt32(v);
            }
        }

        public static object Status()
        {
            var r = new Dictionary<string, object>();
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpComputerStatus"))
                    foreach (ManagementObject o in s.Get())
                    {
                        r["defender"] = true;
                        r["service"] = o["AMServiceEnabled"];
                        r["antivirus"] = o["AntivirusEnabled"];
                        r["realtime"] = o["RealTimeProtectionEnabled"];
                        r["behavior"] = o["BehaviorMonitorEnabled"];
                        r["tamper"] = o["IsTamperProtected"];
                        r["sigVersion"] = S(o["AntivirusSignatureVersion"]);
                        r["sigAge"] = o["AntivirusSignatureAge"];
                        r["engine"] = S(o["AMEngineVersion"]);
                        r["quickAge"] = o["QuickScanAge"];
                        r["fullAge"] = o["FullScanAge"];
                        var q = WmiDate(o["QuickScanEndTime"]);
                        r["lastQuick"] = q.HasValue && q.Value.Year > 2000 ? q.Value.ToString("dd/MM/yyyy HH:mm") : "";
                        r["mode"] = S(o["AMRunningMode"]);
                    }
            }
            catch (Exception ex) { r["defender"] = false; r["defenderError"] = ex.Message; }

            var avs = new List<object>();
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct"))
                    foreach (ManagementObject o in s.Get())
                    {
                        int state = Convert.ToInt32(o["productState"]);
                        avs.Add(new Dictionary<string, object> { { "name", S(o["displayName"]) }, { "enabled", ((state >> 12) & 0xF) == 1 }, { "updated", ((state >> 4) & 0xF) == 0 } });
                    }
            }
            catch { }
            r["products"] = avs;

            const string fw = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\";
            r["firewall"] = new Dictionary<string, object> {
                { "domain", RegInt(RegistryHive.LocalMachine, fw + "DomainProfile", "EnableFirewall", 1) == 1 },
                { "private", RegInt(RegistryHive.LocalMachine, fw + "StandardProfile", "EnableFirewall", 1) == 1 },
                { "public", RegInt(RegistryHive.LocalMachine, fw + "PublicProfile", "EnableFirewall", 1) == 1 } };
            r["uac"] = RegInt(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 1) == 1;
            r["smartscreen"] = RegInt(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 1) != 0;
            lock (_lock) { r["scan"] = ScanStatusLocked(); }
            return r;
        }

        static Dictionary<string, object> ScanStatusLocked()
        {
            bool running = _scan != null && !_scan.HasExited;
            return new Dictionary<string, object> {
                { "running", running }, { "type", _scanType },
                { "elapsed", running ? (int)(DateTime.Now - _scanStart).TotalSeconds : 0 }, { "last", _lastResult } };
        }

        public static object ScanStatus() { lock (_lock) return ScanStatusLocked(); }

        public static object StartScan(string type, string path)
        {
            lock (_lock)
            {
                if (_scan != null && !_scan.HasExited) throw new InvalidOperationException("Já existe uma verificação em andamento");
                string args = type == "full" ? "-Scan -ScanType 2" : type == "custom" ? "-Scan -ScanType 3 -File \"" + path + "\"" : "-Scan -ScanType 1";
                if (type == "custom" && !Directory.Exists(path) && !File.Exists(path)) throw new ArgumentException("Caminho inválido");
                var psi = new ProcessStartInfo(MpCmdRun(), args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                _scan = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _scanType = type; _scanStart = DateTime.Now;
                var output = new System.Text.StringBuilder();
                _scan.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                _scan.Exited += (s, e) =>
                {
                    lock (_lock)
                    {
                        int code = -1; try { code = _scan.ExitCode; } catch { }
                        _lastResult = new Dictionary<string, object> {
                            { "type", _scanType }, { "code", code }, { "threats", code == 2 }, { "ok", code == 0 },
                            { "seconds", (int)(DateTime.Now - _scanStart).TotalSeconds }, { "when", DateTime.Now.ToString("dd/MM/yyyy HH:mm") },
                            { "output", output.ToString().Trim() } };
                    }
                    Program.Window?.Notify("scanDone", _lastResult);
                };
                _scan.Start();
                _scan.BeginOutputReadLine();
                return ScanStatusLocked();
            }
        }

        public static object CancelScan()
        {
            lock (_lock)
            {
                if (_scan != null && !_scan.HasExited) { try { _scan.Kill(); } catch { } }
                return true;
            }
        }

        public static object UpdateSignatures()
        {
            string o = Power.Run(MpCmdRun(), "-SignatureUpdate", 300000);
            return new Dictionary<string, object> { { "output", o.Trim() } };
        }

        public static object Threats()
        {
            var names = new Dictionary<string, Dictionary<string, object>>();
            var list = new List<object>();
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT ThreatID, ThreatName, SeverityID FROM MSFT_MpThreat"))
                    foreach (ManagementObject o in s.Get())
                        names[S(o["ThreatID"])] = new Dictionary<string, object> { { "name", S(o["ThreatName"]) }, { "severity", o["SeverityID"] } };
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT ThreatID, InitialDetectionTime, Resources, ActionSuccess FROM MSFT_MpThreatDetection"))
                    foreach (ManagementObject o in s.Get())
                    {
                        Dictionary<string, object> n; names.TryGetValue(S(o["ThreatID"]), out n);
                        var res = o["Resources"] as string[];
                        var when = WmiDate(o["InitialDetectionTime"]);
                        list.Add(new Dictionary<string, object> {
                            { "name", n != null ? n["name"] : "Ameaça " + S(o["ThreatID"]) }, { "severity", n != null ? n["severity"] : 0 },
                            { "when", when.HasValue ? when.Value.ToString("dd/MM/yyyy HH:mm") : "" },
                            { "resources", res != null ? string.Join("\n", res.Take(5)) : "" }, { "handled", o["ActionSuccess"] } });
                    }
                return new Dictionary<string, object> { { "ok", true }, { "items", list } };
            }
            catch (Exception ex) { return new Dictionary<string, object> { { "ok", false }, { "error", ex.Message }, { "items", list } }; }
        }

        /// <summary>Verificações próprias e rápidas de pontos comuns de ataque.</summary>
        public static object Audit()
        {
            var findings = new List<object>();
            Action<string, string, string> add = (level, title, detail) =>
                findings.Add(new Dictionary<string, object> { { "level", level }, { "title", title }, { "detail", detail } });

            // arquivo hosts
            try
            {
                string hosts = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
                var lines = File.ReadAllLines(hosts).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#"))
                    .Where(l => !l.Contains("localhost")).ToList();
                if (lines.Count > 0) add("warn", "Arquivo hosts modificado", lines.Count + " entrada(s): " + string.Join(" | ", lines.Take(3)));
                else add("ok", "Arquivo hosts limpo", "Nenhum redirecionamento de site");
            }
            catch { }

            // proxy
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
            {
                if (Convert.ToInt32(k?.GetValue("ProxyEnable") ?? 0) == 1)
                    add("warn", "Proxy ativo", "Todo o tráfego passa por " + k.GetValue("ProxyServer") + " — confirme se foi você que configurou");
                else if (!string.IsNullOrEmpty(Convert.ToString(k?.GetValue("AutoConfigURL"))))
                    add("warn", "Script de proxy automático", Convert.ToString(k.GetValue("AutoConfigURL")));
                else add("ok", "Sem proxy", "Conexão direta");
            }

            // processos rodando de pastas temporárias
            var tempRun = new List<string>();
            int self = Process.GetCurrentProcess().Id;
            foreach (Dictionary<string, object> p in (List<object>)Processes.List())
            {
                if ((int)p["pid"] == self) continue;
                string path = (string)p["path"];
                if (path.IndexOf(@"\AppData\Local\Temp\", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf(@"\Windows\Temp\", StringComparison.OrdinalIgnoreCase) >= 0)
                    tempRun.Add(p["name"] + " — " + path);
            }
            if (tempRun.Count > 0) add("warn", "Programas rodando da pasta Temp", string.Join("\n", tempRun.Take(5)) + "\nInstaladores fazem isso; malware também.");
            else add("ok", "Nenhum programa rodando de pastas temporárias", "");

            // inicialização sem assinatura
            var unsigned = ((List<object>)Startup.List()).Cast<Dictionary<string, object>>()
                // arquivos do Windows usam assinatura por catálogo (não embutida): ficam de fora
                .Where(i => (bool)i["enabled"] && (bool)i["exists"] && i["signer"] == null && ((string)i["path"]).EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && !((string)i["path"]).StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
                .Select(i => (string)i["name"]).ToList();
            if (unsigned.Count > 0) add("info", "Inicialização sem assinatura digital", string.Join(", ", unsigned) + " — verifique se reconhece esses programas");
            else add("ok", "Itens de inicialização assinados", "");

            if (RegInt(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 1) != 1)
                add("bad", "Controle de Conta de Usuário (UAC) desativado", "Qualquer programa pode obter privilégios de administrador");
            return findings;
        }
    }
}
