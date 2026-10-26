using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>
    /// Ping para jogos: mede latência, jitter e perda; acha o DNS mais rápido; ajusta o Nagle (jogos TCP);
    /// aponta o que atrapalha (Wi-Fi fraco, apps baixando). Ping de verdade depende da distância e do provedor —
    /// o app reduz o que está do lado do seu PC.
    /// </summary>
    internal static class NetBoost
    {
        static NetworkInterface Active()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Where(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .OrderByDescending(n => n.Speed).FirstOrDefault();
        }

        public static object Info()
        {
            var n = Active();
            var r = new Dictionary<string, object> { { "connected", n != null }, { "admin", Program.IsAdmin } };
            if (n == null) return r;
            bool wifi = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            r["name"] = n.Name; r["desc"] = n.Description; r["wifi"] = wifi; r["speed"] = n.Speed / 1000000;
            var ip = n.GetIPProperties();
            r["gateway"] = ip.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
            r["dns"] = ip.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray();
            r["dnsAuto"] = DnsIsAuto(n);
            r["nagle"] = NagleOff(n);
            if (wifi)
            {
                string o = Power.Run(Power.Sys("netsh.exe"), "wlan show interfaces", 8000);
                var sig = Regex.Match(o, @"(Sinal|Signal)\s*:\s*(\d+)%");
                var ssid = Regex.Match(o, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
                var band = Regex.Match(o, @"(Banda|Band)\s*:\s*(.+)$", RegexOptions.Multiline);
                if (sig.Success) r["signal"] = int.Parse(sig.Groups[2].Value);
                if (ssid.Success) r["ssid"] = ssid.Groups[1].Value.Trim();
                if (band.Success) r["band"] = band.Groups[2].Value.Trim();
            }
            // apps que costumam disputar a banda durante o jogo
            var hogs = new[] { "OneDrive", "steam", "EpicGamesLauncher", "qbittorrent", "uTorrent", "BitTorrent", "Dropbox", "GoogleDriveFS", "MEGAsync", "Battle.net", "EADesktop", "UbisoftConnect" };
            r["hogs"] = hogs.Where(h => { var ps = Process.GetProcessesByName(h); bool any = ps.Length > 0; foreach (var p in ps) p.Dispose(); return any; }).ToArray();
            return r;
        }

        /// <summary>count pings com 250 ms de intervalo: média, mín, máx, jitter e perda.</summary>
        public static object PingTest(string host, int count)
        {
            count = Math.Max(5, Math.Min(50, count));
            var times = new List<long>(); int lost = 0;
            using (var p = new Ping())
                for (int i = 0; i < count; i++)
                {
                    try { var r = p.Send(host, 1500); if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime); else lost++; } catch { lost++; }
                    Thread.Sleep(250);
                }
            double jitter = 0;
            for (int i = 1; i < times.Count; i++) jitter += Math.Abs(times[i] - times[i - 1]);
            jitter = times.Count > 1 ? jitter / (times.Count - 1) : 0;
            return new Dictionary<string, object> {
                { "host", host }, { "avg", times.Count > 0 ? Math.Round(times.Average(), 1) : -1 }, { "min", times.Count > 0 ? times.Min() : -1 },
                { "max", times.Count > 0 ? times.Max() : -1 }, { "jitter", Math.Round(jitter, 1) }, { "loss", Math.Round(lost * 100.0 / count, 1) }, { "samples", times.ToArray() } };
        }

        // ---------------------------------------------------------------- DNS

        static readonly Dictionary<string, string[]> Servers = new Dictionary<string, string[]>
        {
            { "Cloudflare", new[] { "1.1.1.1", "1.0.0.1" } }, { "Google", new[] { "8.8.8.8", "8.8.4.4" } },
            { "Quad9", new[] { "9.9.9.9", "149.112.112.112" } }, { "OpenDNS", new[] { "208.67.222.222", "208.67.220.220" } },
            { "AdGuard", new[] { "94.140.14.14", "94.140.15.15" } },
        };
        static readonly string[] Domains = { "google.com", "youtube.com", "steamcommunity.com", "discord.com", "riotgames.com", "epicgames.com", "microsoft.com", "twitch.tv" };

        /// <summary>Consulta DNS tipo A crua por UDP (mede o servidor, sem o cache do Windows).</summary>
        static double Query(string server, string domain)
        {
            var q = new List<byte> { (byte)Environment.TickCount, 0x42, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
            foreach (var part in domain.Split('.')) { q.Add((byte)part.Length); q.AddRange(System.Text.Encoding.ASCII.GetBytes(part)); }
            q.AddRange(new byte[] { 0, 0, 1, 0, 1 });
            using (var u = new UdpClient())
            {
                u.Client.ReceiveTimeout = 1500;
                var ep = new IPEndPoint(IPAddress.Parse(server), 53);
                var sw = Stopwatch.StartNew();
                u.Send(q.ToArray(), q.Count, ep);
                try { var resp = u.Receive(ref ep); sw.Stop(); return resp.Length > 12 ? sw.Elapsed.TotalMilliseconds : -1; }
                catch { return -1; }
            }
        }

        public static object DnsBenchmark()
        {
            var list = new Dictionary<string, string[]>(Servers);
            var n = Active();
            var cur = n?.GetIPProperties().DnsAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
            if (cur != null && !list.Values.Any(v => v[0] == cur)) list = new[] { new KeyValuePair<string, string[]>("Atual (" + cur + ")", new[] { cur }) }.Concat(list).ToDictionary(k => k.Key, k => k.Value);
            var results = new List<Dictionary<string, object>>();
            foreach (var s in list)
            {
                var times = new List<double>(); int fail = 0;
                foreach (var d in Domains) { double t = Query(s.Value[0], d); if (t >= 0) times.Add(t); else fail++; }
                times.Sort();
                results.Add(new Dictionary<string, object> {
                    { "name", s.Key }, { "ip", s.Value[0] }, { "ip2", s.Value.Length > 1 ? s.Value[1] : null },
                    { "median", times.Count > 0 ? Math.Round(times[times.Count / 2], 1) : -1 }, { "fail", fail }, { "current", s.Value[0] == cur } });
            }
            return results.OrderBy(r => (double)r["median"] < 0 ? 99999 : (double)r["median"]).Cast<object>().ToList();
        }

        static bool DnsIsAuto(NetworkInterface n)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + n.Id))
                return string.IsNullOrEmpty(Convert.ToString(k?.GetValue("NameServer") ?? ""));
        }

        /// <summary>primary vazio = voltar ao automático (DHCP do roteador).</summary>
        public static object SetDns(string primary, string secondary)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            var n = Active() ?? throw new InvalidOperationException("Sem conexão ativa");
            IPAddress tmp;
            if (!string.IsNullOrEmpty(primary) && !IPAddress.TryParse(primary, out tmp)) throw new ArgumentException("IP inválido");
            if (!string.IsNullOrEmpty(secondary) && !IPAddress.TryParse(secondary, out tmp)) throw new ArgumentException("IP inválido");
            string netsh = Power.Sys("netsh.exe");
            if (string.IsNullOrEmpty(primary)) Power.Run(netsh, "interface ip set dns name=\"" + n.Name + "\" source=dhcp", 15000);
            else
            {
                Power.Run(netsh, "interface ip set dns name=\"" + n.Name + "\" source=static address=" + primary + " register=primary validate=no", 15000);
                if (!string.IsNullOrEmpty(secondary)) Power.Run(netsh, "interface ip add dns name=\"" + n.Name + "\" address=" + secondary + " index=2 validate=no", 15000);
            }
            Native.DnsFlushResolverCache();
            return Info();
        }

        // ---------------------------------------------------------------- Nagle

        static bool NagleOff(NetworkInterface n)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + n.Id))
                return Convert.ToInt32(k?.GetValue("TcpAckFrequency") ?? 0) == 1 && Convert.ToInt32(k?.GetValue("TCPNoDelay") ?? 0) == 1;
        }

        /// <summary>Desliga o algoritmo de Nagle e o ACK atrasado na conexão ativa. Ajuda jogos que usam TCP (MMOs antigos); jogos UDP não mudam.</summary>
        public static object SetNagle(bool off)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            var n = Active() ?? throw new InvalidOperationException("Sem conexão ativa");
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default))
            using (var k = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + n.Id, true))
            {
                if (k == null) throw new InvalidOperationException("Interface não encontrada");
                if (off) { k.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord); k.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord); }
                else { k.DeleteValue("TcpAckFrequency", false); k.DeleteValue("TCPNoDelay", false); }
            }
            return Info();
        }
    }
}
