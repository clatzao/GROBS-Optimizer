using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Grobs
{
    /// <summary>
    /// Saúde dos discos no estilo CrystalDiskInfo: tabela S.M.A.R.T. completa (SATA/ATA) e log de saúde NVMe,
    /// com status Bom/Cuidado/Ruim, % de vida, temperatura, horas ligado e total gravado. Requer administrador.
    /// </summary>
    internal static class Smart
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int ret, IntPtr ov);

        const uint SMART_RCV_DRIVE_DATA = 0x0007C088, IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

        static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            { 0x01, "Taxa de erros de leitura" }, { 0x02, "Desempenho de transferência" }, { 0x03, "Tempo para girar" }, { 0x04, "Partidas/paradas do motor" },
            { 0x05, "Setores realocados" }, { 0x07, "Taxa de erros de busca" }, { 0x08, "Desempenho do tempo de busca" }, { 0x09, "Horas ligado" },
            { 0x0A, "Novas tentativas de giro" }, { 0x0B, "Recalibrações" }, { 0x0C, "Ciclos de energia" }, { 0x0D, "Erros leves de leitura" },
            { 0x16, "Nível de hélio" }, { 0x64, "Ciclos de apagamento/gravação" }, { 0xA0, "Setores incorrigíveis em leitura" }, { 0xA1, "Blocos reservas válidos" },
            { 0xA3, "Blocos inválidos iniciais" }, { 0xA4, "Contagem total de apagamentos" }, { 0xA5, "Máximo de apagamentos" }, { 0xA6, "Mínimo de apagamentos" },
            { 0xA7, "Média de apagamentos" }, { 0xA8, "Máximo de apagamentos (especificação)" }, { 0xA9, "Vida útil restante" }, { 0xAA, "Blocos reservas disponíveis" },
            { 0xAB, "Falhas de programação" }, { 0xAC, "Falhas de apagamento" }, { 0xAD, "Nivelamento de desgaste" }, { 0xAE, "Desligamentos inesperados" },
            { 0xAF, "Falhas de programação (pior caso)" }, { 0xB0, "Falhas de apagamento (chip)" }, { 0xB1, "Nivelamento de desgaste" }, { 0xB2, "Blocos reservas usados (chip)" },
            { 0xB3, "Blocos reservas usados" }, { 0xB4, "Blocos reservas não usados" }, { 0xB5, "Falhas de programação" }, { 0xB6, "Falhas de apagamento" },
            { 0xB7, "Erros de execução (SATA)" }, { 0xB8, "Erros de ponta a ponta" }, { 0xBB, "Erros incorrigíveis reportados" }, { 0xBC, "Tempo limite de comando" },
            { 0xBD, "Gravações altas" }, { 0xBE, "Temperatura do fluxo de ar" }, { 0xBF, "Erros por impacto (G-sense)" }, { 0xC0, "Desligamentos de emergência" },
            { 0xC1, "Ciclos de carga/descarga" }, { 0xC2, "Temperatura" }, { 0xC3, "Recuperações ECC" }, { 0xC4, "Eventos de realocação" },
            { 0xC5, "Setores pendentes" }, { 0xC6, "Setores incorrigíveis" }, { 0xC7, "Erros de CRC (cabo/porta)" }, { 0xC8, "Taxa de erros de gravação" },
            { 0xC9, "Erros leves de leitura" }, { 0xCA, "Vida útil usada / dados endereçados" }, { 0xCC, "Correções ECC por software" }, { 0xCE, "Altura de voo" },
            { 0xD2, "Falhas de verificação RAIN" }, { 0xDC, "Deslocamento do disco" }, { 0xDF, "Tentativas de carga" }, { 0xE0, "Fricção de carga" },
            { 0xE1, "Ciclos de carga" }, { 0xE2, "Tempo em carga" }, { 0xE6, "Amplitude do cabeçote" }, { 0xE7, "Vida útil restante do SSD" },
            { 0xE8, "Reserva disponível" }, { 0xE9, "Indicador de desgaste da mídia" }, { 0xEA, "Média/máximo de apagamentos" }, { 0xEB, "Blocos bons / reserva" },
            { 0xF0, "Horas de voo do cabeçote" }, { 0xF1, "Total gravado" }, { 0xF2, "Total lido" }, { 0xF3, "Total gravado (alto)" },
            { 0xF4, "Total lido (alto)" }, { 0xF5, "Vida útil restante (fabricante)" }, { 0xF6, "Total gravado pelo host" }, { 0xF7, "Páginas gravadas pelo host" },
            { 0xF8, "Páginas gravadas pela controladora" }, { 0xF9, "Gravações em NAND (GiB)" }, { 0xFA, "Erros de nova tentativa de leitura" }, { 0xFE, "Proteção contra queda" },
        };

        static SafeFileHandle Open(int n) => CreateFile(@"\\.\PhysicalDrive" + n, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);

        static byte[] AtaSmart(SafeFileHandle h, int drive, byte feature)
        {
            var inp = new byte[33 + 512];
            BitConverter.GetBytes(512).CopyTo(inp, 0);
            inp[4] = feature; inp[5] = 1; inp[6] = 1; inp[7] = 0x4F; inp[8] = 0xC2; inp[9] = (byte)(0xA0 | ((drive & 1) << 4)); inp[10] = 0xB0;
            inp[12] = (byte)drive;
            var outp = new byte[16 + 512];
            int ret;
            if (!DeviceIoControl(h, SMART_RCV_DRIVE_DATA, inp, 33, outp, outp.Length, out ret, IntPtr.Zero)) return null;
            var d = new byte[512]; Array.Copy(outp, 16, d, 0, 512);
            return d.Any(b => b != 0) ? d : null;
        }

        static byte[] NvmeHealth(SafeFileHandle h)
        {
            var buf = new byte[8 + 40 + 512];
            BitConverter.GetBytes(50).CopyTo(buf, 0);  // StorageDeviceProtocolSpecificProperty
            BitConverter.GetBytes(0).CopyTo(buf, 4);   // PropertyStandardQuery
            BitConverter.GetBytes(3).CopyTo(buf, 8);   // ProtocolTypeNvme
            BitConverter.GetBytes(2).CopyTo(buf, 12);  // NVMeDataTypeLogPage
            BitConverter.GetBytes(2).CopyTo(buf, 16);  // log 02h: SMART / Health Information
            BitConverter.GetBytes(40).CopyTo(buf, 28); // ProtocolDataOffset
            BitConverter.GetBytes(512).CopyTo(buf, 32);// ProtocolDataLength
            var outp = new byte[buf.Length];
            int ret;
            if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, buf, buf.Length, outp, outp.Length, out ret, IntPtr.Zero)) return null;
            int off = 8 + BitConverter.ToInt32(outp, 8 + 20);
            if (off + 512 > outp.Length) return null;
            var d = new byte[512]; Array.Copy(outp, off, d, 0, 512);
            return d;
        }

        static double U128(byte[] d, int off) { double v = 0; for (int i = 15; i >= 0; i--) v = v * 256 + d[off + i]; return v; }
        static long Raw(byte[] a, int off) { long v = 0; for (int i = 5; i >= 0; i--) v = (v << 8) | a[off + 5 + i]; return v; }

        static List<Dictionary<string, object>> Disks()
        {
            var list = new List<Dictionary<string, object>>();
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, SerialNumber, FirmwareVersion, MediaType, BusType, Size, SpindleSpeed FROM MSFT_PhysicalDisk"))
                    foreach (ManagementObject o in s.Get())
                        using (o)
                        {
                            int n; if (!int.TryParse(Convert.ToString(o["DeviceId"]), out n)) continue;
                            int media = Convert.ToInt32(o["MediaType"] ?? 0), bus = Convert.ToInt32(o["BusType"] ?? 0);
                            list.Add(new Dictionary<string, object> {
                                { "index", n }, { "model", Convert.ToString(o["FriendlyName"]).Trim() }, { "serial", Convert.ToString(o["SerialNumber"] ?? "").Trim() },
                                { "firmware", Convert.ToString(o["FirmwareVersion"] ?? "").Trim() }, { "size", Convert.ToDouble(o["Size"] ?? 0) },
                                { "bus", bus == 17 ? "NVMe" : bus == 11 ? "SATA" : bus == 7 ? "USB" : bus == 8 ? "RAID" : bus == 3 ? "ATA" : "" },
                                { "type", bus == 17 ? "NVMe" : media == 4 ? "SSD" : media == 3 ? "HDD" : "Disco" },
                                { "rpm", Convert.ToInt64(o["SpindleSpeed"] ?? 0) } });
                        }
            }
            catch { }
            if (list.Count == 0) // Windows 7
                using (var s = new ManagementObjectSearcher("SELECT Index, Model, SerialNumber, FirmwareRevision, InterfaceType, Size FROM Win32_DiskDrive"))
                    foreach (ManagementObject o in s.Get())
                        using (o)
                        {
                            string model = Convert.ToString(o["Model"]).Trim();
                            list.Add(new Dictionary<string, object> {
                                { "index", Convert.ToInt32(o["Index"]) }, { "model", model }, { "serial", Convert.ToString(o["SerialNumber"] ?? "").Trim() },
                                { "firmware", Convert.ToString(o["FirmwareRevision"] ?? "").Trim() }, { "size", Convert.ToDouble(o["Size"] ?? 0) },
                                { "bus", Convert.ToString(o["InterfaceType"]) }, { "type", model.IndexOf("SSD", StringComparison.OrdinalIgnoreCase) >= 0 ? "SSD" : "Disco" }, { "rpm", 0L } });
                        }
            return list.OrderBy(d => (int)d["index"]).ToList();
        }

        /// <summary>Unidade (×bytes) do atributo F1/F6 "total gravado" — varia por fabricante.</summary>
        static double WriteUnit(string model)
        {
            string m = model.ToUpperInvariant();
            if (m.Contains("SAMSUNG") || m.Contains("CRUCIAL") || m.Contains("MICRON") || m.Contains("CT") && m.Contains("SSD")) return 512;
            if (m.Contains("INTEL")) return 32.0 * 1024 * 1024;
            if (m.Contains("KINGSTON") || m.Contains("SANDISK") || m.Contains("WDC WDS") || m.Contains("WD ") || m.Contains("ADATA") || m.Contains("LEXAR") || m.Contains("PNY") || m.Contains("GOODRAM")) return 1024.0 * 1024 * 1024;
            return 0;
        }

        public static object Read()
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            var result = new List<object>();
            foreach (var d in Disks())
            {
                int n = (int)d["index"];
                try
                {
                    using (var h = Open(n))
                    {
                        if (h.IsInvalid) { d["status"] = "unknown"; d["error"] = "Sem acesso ao disco"; result.Add(d); continue; }
                        if ((string)d["bus"] == "NVMe") ReadNvme(h, d);
                        else ReadAta(h, n, d);
                    }
                }
                catch (Exception ex) { d["status"] = "unknown"; d["error"] = ex.Message; }
                result.Add(d);
            }
            return result;
        }

        static void ReadNvme(SafeFileHandle h, Dictionary<string, object> d)
        {
            var b = NvmeHealth(h);
            if (b == null) { d["status"] = "unknown"; d["error"] = "O driver deste NVMe não informa o log de saúde"; return; }
            int warn = b[0], spare = b[3], spareTh = b[4], used = b[5];
            double tempK = BitConverter.ToUInt16(b, 1);
            double mediaErr = U128(b, 160);
            var attrs = new List<object>();
            Action<string, string, object, string> add = (id, name, v, st) => attrs.Add(new Dictionary<string, object> { { "id", id }, { "name", name }, { "raw", v }, { "status", st } });
            add("01", "Aviso crítico", "0x" + warn.ToString("X2"), warn == 0 ? "ok" : "bad");
            add("02", "Temperatura", tempK > 0 ? Math.Round(tempK - 273) + " °C" : "—", "ok");
            add("03", "Reserva disponível", spare + " %", spare < spareTh ? "bad" : "ok");
            add("04", "Limite da reserva", spareTh + " %", "ok");
            add("05", "Vida útil usada", used + " %", used >= 100 ? "bad" : used >= 90 ? "warn" : "ok");
            add("06", "Dados lidos", Tb(U128(b, 32) * 512000), "ok");
            add("07", "Dados gravados", Tb(U128(b, 48) * 512000), "ok");
            add("08", "Comandos de leitura", U128(b, 64).ToString("N0"), "ok");
            add("09", "Comandos de gravação", U128(b, 80).ToString("N0"), "ok");
            add("0A", "Tempo ocupado da controladora", U128(b, 96).ToString("N0") + " min", "ok");
            add("0B", "Ciclos de energia", U128(b, 112).ToString("N0"), "ok");
            add("0C", "Horas ligado", U128(b, 128).ToString("N0") + " h", "ok");
            add("0D", "Desligamentos inesperados", U128(b, 144).ToString("N0"), "ok");
            add("0E", "Erros de integridade de dados", mediaErr.ToString("N0"), mediaErr > 0 ? "warn" : "ok");
            add("0F", "Entradas no log de erros", U128(b, 176).ToString("N0"), "ok");
            d["attributes"] = attrs; d["kind"] = "nvme";
            d["temp"] = tempK > 0 ? Math.Round(tempK - 273) : -1;
            d["hours"] = U128(b, 128); d["cycles"] = U128(b, 112);
            d["written"] = U128(b, 48) * 512000; d["read"] = U128(b, 32) * 512000;
            d["health"] = Math.Max(0, 100 - used);
            d["status"] = (warn & 0x0C) != 0 || spare < spareTh || used >= 100 ? "bad" : warn != 0 || mediaErr > 0 || used >= 90 ? "warn" : "ok";
        }

        static string Tb(double bytes) => bytes >= 1e12 ? (bytes / 1e12).ToString("0.00") + " TB" : (bytes / 1e9).ToString("0.0") + " GB";

        static void ReadAta(SafeFileHandle h, int n, Dictionary<string, object> d)
        {
            var data = AtaSmart(h, n, 0xD0);
            var th = data != null ? AtaSmart(h, n, 0xD1) : null;
            if (data == null) WmiFallback(n, out data, out th);
            if (data == null) { d["status"] = "unknown"; d["error"] = (string)d["bus"] == "USB" ? "Discos USB geralmente não repassam o S.M.A.R.T." : "Este disco/controladora não informa o S.M.A.R.T."; return; }
            var thr = new Dictionary<int, int>();
            if (th != null) for (int i = 0; i < 30; i++) { int o = 2 + i * 12; if (th[o] != 0) thr[th[o]] = th[o + 1]; }
            var attrs = new List<object>();
            var raw = new Dictionary<int, long>(); var cur = new Dictionary<int, int>();
            string worst = "ok";
            for (int i = 0; i < 30; i++)
            {
                int o = 2 + i * 12, id = data[o];
                if (id == 0) continue;
                int value = data[o + 3], worstV = data[o + 4];
                long r = Raw(data, o);
                int t; thr.TryGetValue(id, out t);
                string st = t > 0 && value <= t && value > 0 ? "bad" : (id == 0x05 || id == 0xC5 || id == 0xC6) && (r & 0xFFFFFFFF) > 0 ? "warn" : "ok";
                if (st == "bad") worst = "bad"; else if (st == "warn" && worst == "ok") worst = "warn";
                raw[id] = r; cur[id] = value;
                string name; if (!Names.TryGetValue(id, out name)) name = "Específico do fabricante";
                attrs.Add(new Dictionary<string, object> { { "id", id.ToString("X2") }, { "name", name }, { "value", value }, { "worst", worstV }, { "threshold", t }, { "raw", r.ToString("X12") }, { "rawDec", r }, { "status", st } });
            }
            d["attributes"] = attrs; d["kind"] = "ata";
            long v;
            d["temp"] = raw.TryGetValue(0xC2, out v) ? (v & 0xFF) : raw.TryGetValue(0xBE, out v) ? (v & 0xFF) : -1;
            d["hours"] = raw.TryGetValue(0x09, out v) ? (v & 0xFFFFFFFF) : -1;
            d["cycles"] = raw.TryGetValue(0x0C, out v) ? (v & 0xFFFFFFFF) : -1;
            double unit = WriteUnit((string)d["model"]);
            if (unit > 0 && (raw.TryGetValue(0xF1, out v) || raw.TryGetValue(0xF6, out v))) d["written"] = v * unit;
            // % de vida do SSD: primeiro atributo de desgaste que existir (valor normalizado)
            bool ssd = (string)d["type"] != "HDD" && (raw.ContainsKey(0xE7) || raw.ContainsKey(0xA9) || raw.ContainsKey(0xB1) || raw.ContainsKey(0xE9) || raw.ContainsKey(0xAD) || (string)d["type"] == "SSD");
            int life = -1;
            foreach (int id in new[] { 0xE7, 0xA9, 0xB1, 0xE9, 0xCA, 0xAD })
                if (cur.ContainsKey(id) && cur[id] <= 100)
                {
                    // controladoras baratas (SiliconMotion, Maxio...) deixam o valor fixo em 100 e põem o % de vida no bruto
                    long rv = raw[id] & 0xFFFF;
                    life = id == 0xA9 && cur[id] == 100 && rv > 0 && rv <= 100 ? (int)rv : cur[id];
                    break;
                }
            long bad = (raw.ContainsKey(0x05) ? raw[0x05] & 0xFFFFFFFF : 0) + (raw.ContainsKey(0xC5) ? raw[0xC5] & 0xFFFFFFFF : 0) + (raw.ContainsKey(0xC6) ? raw[0xC6] & 0xFFFFFFFF : 0);
            d["badSectors"] = bad;
            d["health"] = ssd && life >= 0 ? life : worst == "ok" ? 100 : worst == "warn" ? Math.Max(10, 100 - (int)Math.Min(90, bad)) : 5;
            d["status"] = worst;
            if (ssd && life >= 0 && life <= 10 && worst == "ok") d["status"] = "warn";
        }

        static void WmiFallback(int index, out byte[] data, out byte[] th)
        {
            data = null; th = null;
            try
            {
                string pnp = null;
                using (var s = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_DiskDrive WHERE Index=" + index))
                    foreach (ManagementObject o in s.Get()) using (o) pnp = Convert.ToString(o["PNPDeviceID"]);
                if (pnp == null) return;
                Func<string, byte[]> get = cls =>
                {
                    using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, VendorSpecific FROM " + cls))
                        foreach (ManagementObject o in s.Get())
                            using (o)
                                if (Convert.ToString(o["InstanceName"]).StartsWith(pnp, StringComparison.OrdinalIgnoreCase)) return (byte[])o["VendorSpecific"];
                    return null;
                };
                data = get("MSStorageDriver_FailurePredictData");
                th = get("MSStorageDriver_FailurePredictThresholds");
            }
            catch { }
        }

        // ---------------------------------------------------------------- alertas na bandeja

        static Timer _timer;

        public static void StartAlerts()
        {
            if (!Program.IsAdmin || _timer != null) return;
            _timer = new Timer(_ => CheckAlerts(), null, 60000, 3600 * 1000);
        }

        static void CheckAlerts()
        {
            if (Settings.Get<object>("diskAlerts") is bool b && !b) return;
            try
            {
                var prev = Settings.Get<object>("diskHealth") as Dictionary<string, object> ?? new Dictionary<string, object>();
                var next = new Dictionary<string, object>();
                foreach (Dictionary<string, object> d in (List<object>)Read())
                {
                    string key = (string)d["model"] + "|" + (string)d["serial"], st = Convert.ToString(d.ContainsKey("status") ? d["status"] : "unknown");
                    if (st == "unknown") continue;
                    long bad = d.ContainsKey("badSectors") ? Convert.ToInt64(d["badSectors"]) : 0;
                    double temp = d.ContainsKey("temp") ? Convert.ToDouble(d["temp"]) : -1;
                    next[key] = new Dictionary<string, object> { { "status", st }, { "bad", bad } };
                    var p = prev.ContainsKey(key) ? prev[key] as Dictionary<string, object> : null;
                    string ps = p != null ? Convert.ToString(p["status"]) : "ok";
                    long pb = p != null ? Convert.ToInt64(p["bad"]) : 0;
                    int rank(string s) => s == "bad" ? 2 : s == "warn" ? 1 : 0;
                    string model = (string)d["model"];
                    if (rank(st) > rank(ps)) Program.Window?.Balloon("Saúde do disco: " + (st == "bad" ? "RUIM" : "CUIDADO"), model + " — faça backup dos seus arquivos e veja os detalhes em Discos.", true);
                    else if (bad > pb) Program.Window?.Balloon("Disco com setores ruins", model + ": " + bad + " setor(es) realocado(s)/pendente(s).", true);
                    double limit = (string)d["type"] == "HDD" ? 55 : 70;
                    if (temp >= limit) Program.Window?.Balloon("Disco quente: " + temp + " °C", model + " passou de " + limit + " °C. Verifique a ventilação.", true);
                }
                Settings.Set("diskHealth", next);
            }
            catch (Exception ex) { Program.Log(ex); }
        }
    }
}
