using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>Rede avançada: parâmetros globais do TCP, economia de energia da placa de rede e do Wi-Fi, limpeza de DNS/ARP.</summary>
    internal static class NetTune
    {
        const string NetClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
        const string WifiSub = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", WifiSet = "12bbebe6-58d6-4636-95bb-3217ef867c1a";

        static RegistryKey Lm() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

        static Dictionary<string, string> TcpGlobal()
        {
            string o = Power.Run(Power.Sys("netsh.exe"), "int tcp show global", 10000);
            var r = new Dictionary<string, string>();
            foreach (var line in o.Split('\n'))
            {
                int i = line.LastIndexOf(':'); if (i < 0) continue;
                string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim().ToLowerInvariant();
                if (k.Contains("Receive-Side Scaling") || k.Contains("Dimensionamento no Lado")) r["rss"] = v;
                else if (k.Contains("Auto-Tuning") || k.Contains("Ajuste Autom")) r["autotuning"] = v;
                else if (k.Contains("ECN")) r["ecn"] = v;
                else if (k.Contains("Segment Coalescing") || k.Contains("Segmento de Recep")) r["rsc"] = v;
                else if (k == "Fast Open") r["fastopen"] = v;
            }
            return r;
        }

        static IEnumerable<RegistryKey> AdapterKeys(bool write)
        {
            var ids = new HashSet<string>(NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || n.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet)
                .Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
            using (var cls = Lm().OpenSubKey(NetClass))
                foreach (var sub in cls?.GetSubKeyNames() ?? new string[0])
                {
                    if (sub.Length != 4) continue;
                    RegistryKey k = null;
                    try { k = cls.OpenSubKey(sub, write); } catch { }
                    if (k == null) continue;
                    // só placas físicas (NCF_PHYSICAL), sem adaptadores virtuais/loopback
                    bool physical = (Convert.ToInt32(k.GetValue("Characteristics") ?? 0) & 0x4) != 0 && !Convert.ToString(k.GetValue("ComponentId") ?? "").Equals("*msloop", StringComparison.OrdinalIgnoreCase);
                    if (physical && ids.Contains(Convert.ToString(k.GetValue("NetCfgInstanceId") ?? ""))) yield return k; else k.Dispose();
                }
        }

        public static object Status()
        {
            var tcp = TcpGlobal();
            var adapters = new List<object>();
            foreach (var k in AdapterKeys(false))
                using (k)
                {
                    int pnp = Convert.ToInt32(k.GetValue("PnPCapabilities") ?? 0);
                    string eee = Convert.ToString(k.GetValue("*EEE") ?? k.GetValue("EEELinkAdvertisement") ?? "");
                    adapters.Add(new Dictionary<string, object> { { "name", Convert.ToString(k.GetValue("DriverDesc")) }, { "powerSaveOff", (pnp & 0x18) == 0x18 }, { "eee", eee.Length == 0 ? null : (object)(eee != "0") } });
                }
            string wifi = Power.Run(Power.Sys("powercfg.exe"), "/q scheme_current " + WifiSub + " " + WifiSet, 8000);
            var vals = wifi.Split('\n').Where(l => l.Contains("0x")).Select(l => l.Substring(l.LastIndexOf("0x") + 2).Trim()).ToList();
            int wifiAc = -1;
            bool hasWifi = NetworkInterface.GetAllNetworkInterfaces().Any(x => x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
            if (hasWifi && vals.Count >= 2) try { wifiAc = Convert.ToInt32(vals[vals.Count - 2], 16); } catch { }
            return new Dictionary<string, object> {
                { "autotuning", tcp.ContainsKey("autotuning") ? tcp["autotuning"] : "" }, { "rss", tcp.ContainsKey("rss") ? tcp["rss"] : "" },
                { "ecn", tcp.ContainsKey("ecn") ? tcp["ecn"] : "" }, { "rsc", tcp.ContainsKey("rsc") ? tcp["rsc"] : "" }, { "fastopen", tcp.ContainsKey("fastopen") ? tcp["fastopen"] : "" },
                { "adapters", adapters }, { "wifiPower", wifiAc }, { "admin", Program.IsAdmin } };
        }

        /// <summary>Ajustes recomendados (iguais ao padrão do Windows bem configurado): autotuning normal, RSS ligado, ECN desligado.</summary>
        public static object Set(string what, bool on)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            string netsh = Power.Sys("netsh.exe"), pc = Power.Sys("powercfg.exe");
            switch (what)
            {
                case "autotuning": Power.Run(netsh, "int tcp set global autotuninglevel=" + (on ? "normal" : "disabled")); break;
                case "rss": Power.Run(netsh, "int tcp set global rss=" + (on ? "enabled" : "disabled")); break;
                case "ecn": Power.Run(netsh, "int tcp set global ecncapability=" + (on ? "enabled" : "disabled")); break;
                case "rsc": Power.Run(netsh, "int tcp set global rsc=" + (on ? "enabled" : "disabled")); break;
                case "fastopen": Power.Run(netsh, "int tcp set global fastopen=" + (on ? "enabled" : "disabled")); break;
                case "adapterPower":
                    // on = sem economia de energia: não deixa o Windows desligar a placa nem usar Ethernet "verde" (EEE)
                    foreach (var k in AdapterKeys(true))
                        using (k)
                        {
                            int pnp = Convert.ToInt32(k.GetValue("PnPCapabilities") ?? 0);
                            k.SetValue("PnPCapabilities", on ? pnp | 0x18 : pnp & ~0x18, RegistryValueKind.DWord);
                            foreach (var n in new[] { "*EEE", "EEELinkAdvertisement", "EnableGreenEthernet", "GreenEthernet", "PowerSavingMode" })
                                if (k.GetValue(n) != null) k.SetValue(n, on ? "0" : "1", RegistryValueKind.String);
                        }
                    break;
                case "wifiPower": // 0 = desempenho máximo
                    Power.Run(pc, "/setacvalueindex scheme_current " + WifiSub + " " + WifiSet + " " + (on ? 0 : 1));
                    Power.Run(pc, "/setdcvalueindex scheme_current " + WifiSub + " " + WifiSet + " " + (on ? 0 : 2));
                    Power.Run(pc, "/setactive scheme_current");
                    break;
                default: throw new ArgumentException("Opção desconhecida");
            }
            return Status();
        }

        public static object FlushAll()
        {
            Native.DnsFlushResolverCache();
            if (Program.IsAdmin) Power.Run(Power.Sys("netsh.exe"), "interface ip delete arpcache", 10000);
            Power.Run(Power.Sys("nbtstat.exe"), "-R", 10000);
            return true;
        }
    }
}
