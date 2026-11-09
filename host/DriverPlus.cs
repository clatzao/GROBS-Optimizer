using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>
    /// Catálogo do Microsoft Update (catalog.update.microsoft.com): a busca completa por ID de hardware traz drivers
    /// assinados de todas as marcas (Realtek, Intel, Qualcomm, MediaTek...), inclusive versões que o Windows Update
    /// não oferece sozinho. Os arquivos vêm dos servidores da Microsoft.
    /// </summary>
    internal static class MsCatalog
    {
        public class Row { public string Guid, Title, Products, Cls, Version; public DateTime Date; public double Size; }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var w = new WebClient { Encoding = Encoding.UTF8 };
            w.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) GROBS-Optimizer/" + Program.Version;
            return w;
        }

        static string Cell(string html, string guid, int col)
        {
            var m = Regex.Match(html, "id=\"" + guid + "_C" + col + "_R\\d+\"[^>]*>(.*?)</td>", RegexOptions.Singleline);
            return m.Success ? WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", " ")).Trim() : "";
        }

        public static List<Row> Search(string q)
        {
            string html;
            using (var w = Client()) html = w.DownloadString("https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(q));
            var rows = new List<Row>();
            foreach (var guid in Regex.Matches(html, "id=\"([0-9a-f-]{36})_C1_R\\d+\"").Cast<Match>().Select(m => m.Groups[1].Value).Distinct())
            {
                var r = new Row { Guid = guid, Title = Regex.Replace(Cell(html, guid, 1), "\\s+", " "), Products = Regex.Replace(Cell(html, guid, 2), "\\s+", " "), Cls = Cell(html, guid, 3) };
                DateTime d; if (DateTime.TryParse(Cell(html, guid, 4), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d)) r.Date = d;
                var size = Regex.Matches(Cell(html, guid, 6), "\\d{4,}").Cast<Match>().LastOrDefault();
                if (size != null) r.Size = double.Parse(size.Value);
                var v = Regex.Match(r.Title, "(\\d+(?:\\.\\d+){1,3})\\s*$");
                r.Version = v.Success ? v.Groups[1].Value : "";
                rows.Add(r);
            }
            return rows;
        }

        public static List<string> Urls(string guid)
        {
            string body = "updateIDs=" + Uri.EscapeDataString("[{\"size\":0,\"languages\":\"\",\"uidInfo\":\"" + guid + "\",\"updateID\":\"" + guid + "\"}]");
            string html;
            using (var w = Client()) { w.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded"; html = w.UploadString("https://www.catalog.update.microsoft.com/DownloadDialog.aspx", body); }
            return Regex.Matches(html, "files\\[\\d+\\]\\.url\\s*=\\s*'([^']+)'").Cast<Match>().Select(m => m.Groups[1].Value)
                .Where(u => { Uri x; return Uri.TryCreate(u, UriKind.Absolute, out x) && (x.Host.EndsWith(".windowsupdate.com") || x.Host.EndsWith(".microsoft.com")); }).Distinct().ToList();
        }

        /// <summary>Serve para o Windows deste PC? (o 11 aceita drivers do 10; o 10 não aceita os só do 11)</summary>
        public static bool ForOs(Row r, bool win11)
        {
            string p = r.Products;
            if (p.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return win11 && p.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static Version Ver(string s) { Version v; return Version.TryParse((s ?? "").Trim(), out v) ? v : new Version(0, 0); }

        public static Row Best(IEnumerable<Row> rows, bool win11) =>
            rows.Where(r => ForOs(r, win11) && r.Version != "").OrderByDescending(r => Ver(r.Version)).ThenByDescending(r => r.Date).FirstOrDefault();

        /// <summary>Baixa e extrai um pacote do catálogo; devolve a pasta com os .inf.</summary>
        public static string Fetch(string guid, string dir, Action<string> progress)
        {
            Directory.CreateDirectory(dir);
            var urls = Urls(guid);
            if (urls.Count == 0) throw new InvalidOperationException("O catálogo não devolveu o arquivo deste driver.");
            using (var w = Client())
                foreach (var u in urls)
                {
                    string file = Path.Combine(dir, Path.GetFileName(new Uri(u).LocalPath));
                    progress?.Invoke("Baixando " + Path.GetFileName(file) + "…");
                    w.DownloadFile(u, file);
                    if (file.EndsWith(".cab", StringComparison.OrdinalIgnoreCase))
                    {
                        Power.Run(Power.Sys("expand.exe"), "-F:* \"" + file + "\" \"" + dir + "\"", 300000);
                        try { File.Delete(file); } catch { }
                    }
                }
            if (!Directory.EnumerateFiles(dir, "*.inf", SearchOption.AllDirectories).Any()) throw new InvalidOperationException("O pacote baixado não tem driver (.inf).");
            return dir;
        }
    }

    /// <summary>
    /// Drivers além do Windows Update: busca no Catálogo do Microsoft Update cada dispositivo importante (rede, vídeo,
    /// áudio, Bluetooth, armazenamento, câmera...) e os que estão sem driver. Antes de instalar faz ponto de
    /// restauração e copia os drivers atuais; cada instalação pode ser desfeita.
    /// </summary>
    internal static class DriverPlus
    {
        static readonly object _lock = new object();
        static volatile string _state = "idle"; // idle | searching | installing | undoing | gpu | cleaning | netpack
        static string _progress = "", _error, _when, _message;
        static List<Dictionary<string, object>> _items;

        static readonly string[] Classes = { "NET", "DISPLAY", "MEDIA", "BLUETOOTH", "HDC", "SCSIADAPTER", "CAMERA", "IMAGE", "BIOMETRIC", "SENSOR" };
        internal static bool Win11 => Environment.OSVersion.Version.Build >= 22000;
        static string Root => Path.Combine(Program.DataDir, "drivers");

        public class Dev { public string Name, Cls, Key, Version, Inf, HwId, DeviceId; public bool Missing; }

        public static object Status() => new Dictionary<string, object> {
            { "state", _state }, { "progress", _progress }, { "error", _error }, { "message", _message }, { "items", _items }, { "when", _when },
            { "sessions", Sessions().Select(s => (object)new Dictionary<string, object> { { "id", s["id"] }, { "date", s["date"] }, { "titles", s["titles"] } }).ToList() },
            { "admin", Program.IsAdmin } };

        static void Push() => Program.Window?.Notify("driverPlus", Status());
        static void Progress(string p) { _progress = p; Push(); }

        static void Run(string state, Action work)
        {
            lock (_lock)
            {
                if (_state != "idle") throw new InvalidOperationException("Aguarde a operação de drivers atual terminar.");
                _state = state; _error = null; _message = null;
            }
            new Thread(() =>
            {
                try { work(); }
                catch (Exception ex) { _error = (ex.InnerException ?? ex).Message; Program.Log(ex); }
                _state = "idle"; _progress = "";
                Push();
            }) { IsBackground = true, Name = "DriverPlus" }.Start();
        }

        /// <summary>Chave de busca no catálogo: PCI\VEN_x&DEV_y, USB\VID_x&PID_y, HDAUDIO\FUNC_01&VEN_x&DEV_y ou ACPI\ID.</summary>
        internal static string Key(string hw)
        {
            if (string.IsNullOrEmpty(hw)) return null;
            hw = hw.ToUpperInvariant();
            var m = Regex.Match(hw, @"^(PCI)\\(VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4})");
            if (m.Success) return m.Groups[1].Value + "\\" + m.Groups[2].Value;
            m = Regex.Match(hw, @"^(USB)\\(VID_[0-9A-F]{4}&PID_[0-9A-F]{4})");
            if (m.Success) return m.Groups[1].Value + "\\" + m.Groups[2].Value;
            m = Regex.Match(hw, @"^(HDAUDIO)\\(FUNC_[0-9A-F]{2}&VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4})");
            if (m.Success) return m.Groups[1].Value + "\\" + m.Groups[2].Value;
            m = Regex.Match(hw, @"^(ACPI)\\([A-Z0-9_]+)$");
            if (m.Success) return m.Groups[1].Value + "\\" + m.Groups[2].Value;
            return null;
        }

        internal static List<Dev> Devices()
        {
            var list = new List<Dev>();
            using (var s = new ManagementObjectSearcher("SELECT DeviceID, DeviceName, DeviceClass, DriverVersion, InfName, HardWareID FROM Win32_PnPSignedDriver"))
                foreach (ManagementObject o in s.Get())
                    using (o)
                    {
                        string cls = Convert.ToString(o["DeviceClass"]).ToUpperInvariant();
                        if (!Classes.Contains(cls)) continue;
                        string key = Key(Convert.ToString(o["HardWareID"]));
                        if (key == null) continue;
                        list.Add(new Dev { Name = Convert.ToString(o["DeviceName"]), Cls = cls, Key = key, Version = Convert.ToString(o["DriverVersion"]), Inf = Convert.ToString(o["InfName"]), HwId = Convert.ToString(o["HardWareID"]), DeviceId = Convert.ToString(o["DeviceID"]) });
                    }
            // sem driver (Gerenciador de Dispositivos com erro)
            using (var s = new ManagementObjectSearcher("SELECT Name, HardwareID, PNPClass, DeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 28 OR ConfigManagerErrorCode = 1 OR ConfigManagerErrorCode = 18 OR ConfigManagerErrorCode = 31 OR ConfigManagerErrorCode = 39"))
                foreach (ManagementObject o in s.Get())
                    using (o)
                    {
                        var ids = o["HardwareID"] as string[] ?? new string[0];
                        string key = ids.Select(Key).FirstOrDefault(k => k != null);
                        if (key == null) continue;
                        list.Add(new Dev { Name = Convert.ToString(o["Name"] ?? "Dispositivo desconhecido"), Cls = Convert.ToString(o["PNPClass"] ?? ""), Key = key, Version = "", Inf = "", HwId = ids.FirstOrDefault(), DeviceId = Convert.ToString(o["DeviceID"]), Missing = true });
                    }
            return list;
        }

        public static object Scan()
        {
            Run("searching", () =>
            {
                Progress("Lendo os dispositivos do PC…");
                var devs = Devices();
                var keys = devs.Select(d => d.Key).Distinct().ToList();
                var results = new Dictionary<string, List<MsCatalog.Row>>();
                int done = 0;
                Parallel.ForEach(keys, new ParallelOptions { MaxDegreeOfParallelism = 4 }, k =>
                {
                    List<MsCatalog.Row> rows;
                    try { rows = MsCatalog.Search(k); } catch { rows = new List<MsCatalog.Row>(); }
                    lock (results) results[k] = rows;
                    Progress("Consultando o Catálogo do Microsoft Update (" + Interlocked.Increment(ref done) + " de " + keys.Count + ")…");
                });
                var items = new Dictionary<string, Dictionary<string, object>>();
                foreach (var d in devs)
                {
                    var best = MsCatalog.Best(results[d.Key], Win11);
                    if (best == null) continue;
                    if (!d.Missing && MsCatalog.Ver(best.Version) <= MsCatalog.Ver(d.Version)) continue;
                    Dictionary<string, object> it;
                    if (!items.TryGetValue(best.Guid, out it))
                        items[best.Guid] = it = new Dictionary<string, object> {
                            { "id", best.Guid }, { "title", best.Title }, { "version", best.Version }, { "date", best.Date.Year > 2000 ? best.Date.ToString("dd/MM/yyyy") : "" },
                            { "size", best.Size }, { "cls", best.Cls }, { "devices", new List<object>() }, { "missing", false }, { "source", "Catálogo do Microsoft Update" } };
                    ((List<object>)it["devices"]).Add(new Dictionary<string, object> { { "name", d.Name }, { "current", d.Missing ? "sem driver" : d.Version } });
                    if (d.Missing) it["missing"] = true;
                }
                lock (_lock) { _items = items.Values.OrderByDescending(i => (bool)i["missing"]).ThenBy(i => (string)i["title"]).ToList(); _when = DateTime.Now.ToString("dd/MM/yyyy HH:mm"); }
                _message = _items.Count == 0 ? "Nenhum driver mais novo no Catálogo para este PC." : null;
            });
            return Status();
        }

        // ------------------------------------------------------------------ instalar / desfazer
        static string Pnp(string args, int timeout = 600000) => Power.Run(Power.Sys("pnputil.exe"), args, timeout);
        static HashSet<string> OemInfs() => new HashSet<string>(Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF"), "oem*.inf").Select(f => Path.GetFileName(f).ToLowerInvariant()));

        /// <summary>Copia os drivers atuais (oemNN.inf) dos dispositivos; devolve a pasta de backup.</summary>
        internal static string Backup(IEnumerable<string> infs)
        {
            string dir = Path.Combine(Root, "backup", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);
            foreach (var inf in infs.Where(i => !string.IsNullOrEmpty(i) && i.StartsWith("oem", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
                Pnp("/export-driver " + inf + " \"" + dir + "\"", 120000);
            return dir;
        }

        static List<Dictionary<string, object>> Sessions() =>
            ((Settings.Get<object>("driverSessions") as System.Collections.IEnumerable)?.Cast<object>() ?? Enumerable.Empty<object>()).OfType<Dictionary<string, object>>().ToList();

        static void SaveSession(string backup, IEnumerable<string> added, IEnumerable<string> titles)
        {
            var list = Sessions();
            list.Insert(0, new Dictionary<string, object> { { "id", DateTime.Now.Ticks.ToString() }, { "date", DateTime.Now.ToString("dd/MM/yyyy HH:mm") }, { "backup", backup },
                { "added", added.Cast<object>().ToList() }, { "titles", titles.Cast<object>().ToList() } });
            Settings.Set("driverSessions", list.Take(10).Cast<object>().ToList());
        }

        public static object Install(string[] ids)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador para instalar drivers.");
            var chosen = (_items ?? new List<Dictionary<string, object>>()).Where(i => ids.Contains((string)i["id"])).ToList();
            if (chosen.Count == 0) throw new InvalidOperationException("Nada selecionado.");
            Run("installing", () =>
            {
                Progress("Criando ponto de restauração…");
                RestorePoints.Ensure();
                var devs = Devices();
                Progress("Copiando os drivers atuais (backup)…");
                var names = new HashSet<string>(chosen.SelectMany(c => ((List<object>)c["devices"]).Cast<Dictionary<string, object>>().Select(d => (string)d["name"])));
                string backup = Backup(devs.Where(d => names.Contains(d.Name)).Select(d => d.Inf));
                var before = OemInfs();
                int ok = 0; var errors = new List<string>();
                foreach (var c in chosen)
                {
                    string title = (string)c["title"];
                    try
                    {
                        string dir = MsCatalog.Fetch((string)c["id"], Path.Combine(Root, "dl", (string)c["id"]), Progress);
                        Progress("Instalando " + title + "…");
                        Pnp("/add-driver \"" + Path.Combine(dir, "*.inf") + "\" /subdirs /install");
                        try { Directory.Delete(dir, true); } catch { }
                        ok++;
                    }
                    catch (Exception ex) { errors.Add(title + ": " + ex.Message); }
                }
                var added = OemInfs().Except(before).ToList();
                if (added.Count > 0) SaveSession(backup, added, chosen.Select(c => (string)c["title"]));
                // confere quem realmente trocou de versão
                var now = Devices();
                int changed = now.Count(d => names.Contains(d.Name) && devs.Any(o => o.DeviceId == d.DeviceId && o.Version != d.Version));
                lock (_lock) _items = _items?.Where(i => !ids.Contains((string)i["id"])).ToList();
                _message = changed + " dispositivo(s) com driver novo" + (ok > changed ? " · os demais mantiveram o driver atual (o Windows escolheu o mais adequado)" : "") + ". Reinicie o PC se algo não funcionar como antes." + (errors.Count > 0 ? " Falhas: " + string.Join("; ", errors) : "");
            });
            return Status();
        }

        public static object Undo(string id)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            var s = Sessions().FirstOrDefault(x => Convert.ToString(x["id"]) == id);
            if (s == null) throw new InvalidOperationException("Instalação não encontrada.");
            Run("undoing", () =>
            {
                foreach (var inf in ((System.Collections.IEnumerable)s["added"]).Cast<object>().Select(Convert.ToString))
                {
                    Progress("Removendo " + inf + "…");
                    Pnp("/delete-driver " + inf + " /uninstall /force", 180000);
                }
                string backup = Convert.ToString(s["backup"]);
                if (Directory.Exists(backup) && Directory.EnumerateFiles(backup, "*.inf", SearchOption.AllDirectories).Any())
                {
                    Progress("Reinstalando os drivers anteriores…");
                    Pnp("/add-driver \"" + Path.Combine(backup, "*.inf") + "\" /subdirs /install");
                }
                Pnp("/scan-devices", 120000);
                Settings.Set("driverSessions", Sessions().Where(x => Convert.ToString(x["id"]) != id).Cast<object>().ToList());
                _message = "Drivers anteriores restaurados. Reinicie o PC para concluir.";
            });
            return Status();
        }

        // ------------------------------------------------------------------ vídeo: travar e instalação limpa
        const string Restr = @"SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Restrictions";
        static RegistryKey Lm() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

        static List<Dev> Gpus() => Devices().Where(d => d.Cls == "DISPLAY" && d.Key.StartsWith("PCI\\") && !d.Missing).ToList();

        public static object GpuLockStatus()
        {
            var ids = LockedIds();
            var gpus = Gpus();
            return new Dictionary<string, object> { { "locked", gpus.Count > 0 && gpus.All(g => ids.Contains(g.HwId, StringComparer.OrdinalIgnoreCase)) },
                { "gpus", gpus.Select(g => (object)new Dictionary<string, object> { { "name", g.Name }, { "version", g.Version } }).ToList() }, { "admin", Program.IsAdmin } };
        }

        static List<string> LockedIds()
        {
            using (var k = Lm().OpenSubKey(Restr + @"\DenyDeviceIDs"))
                return k == null ? new List<string>() : k.GetValueNames().Select(n => Convert.ToString(k.GetValue(n))).ToList();
        }

        /// <summary>Impede o Windows (Update ou qualquer instalador) de trocar o driver das placas de vídeo.</summary>
        public static object GpuLock(bool on)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            SetLock(on ? Gpus().Select(g => g.HwId).ToList() : new List<string>());
            return GpuLockStatus();
        }

        static void SetLock(List<string> ids)
        {
            using (var lm = Lm())
            {
                var keep = LockedIds().Where(i => !Gpus().Any(g => g.HwId.Equals(i, StringComparison.OrdinalIgnoreCase))).ToList(); // bloqueios de outros dispositivos ficam
                var all = keep.Concat(ids).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                lm.DeleteSubKeyTree(Restr + @"\DenyDeviceIDs", false);
                using (var r = lm.CreateSubKey(Restr))
                {
                    if (all.Count == 0) { r.DeleteValue("DenyDeviceIDs", false); r.DeleteValue("DenyDeviceIDsRetroactive", false); return; }
                    r.SetValue("DenyDeviceIDs", 1, RegistryValueKind.DWord);
                    r.SetValue("DenyDeviceIDsRetroactive", 0, RegistryValueKind.DWord); // não mexe no driver já instalado
                }
                using (var d = lm.CreateSubKey(Restr + @"\DenyDeviceIDs"))
                    for (int i = 0; i < all.Count; i++) d.SetValue((i + 1).ToString(), all[i]);
            }
        }

        /// <summary>
        /// Instalação limpa do driver de vídeo (estilo DDU): baixa o driver novo primeiro, faz backup, remove todos os
        /// pacotes de driver de vídeo instalados e os caches de shader, e instala o novo. NVIDIA: pacote oficial em modo
        /// silencioso e limpo; Intel/AMD: driver mais novo do Catálogo do Microsoft Update.
        /// </summary>
        public static object GpuClean()
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            Run("gpu", () =>
            {
                var gpu = Gpus().OrderByDescending(g => g.Key.Contains("VEN_10DE") || g.Key.Contains("VEN_1002")).FirstOrDefault();
                if (gpu == null) throw new InvalidOperationException("Nenhuma placa de vídeo com driver encontrada.");
                string work = Path.Combine(Root, "gpu"); try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
                Directory.CreateDirectory(work);
                string nvExe = null, infDir = null;
                if (gpu.Key.Contains("VEN_10DE"))
                {
                    Progress("Procurando o driver mais novo no site da NVIDIA…");
                    var info = (GpuDrivers.Check() as System.Collections.IEnumerable).Cast<Dictionary<string, object>>().FirstOrDefault(g => Convert.ToString(g["vendor"]) == "nvidia" && g.ContainsKey("url"));
                    if (info == null) throw new InvalidOperationException("Não consegui o endereço do driver na NVIDIA. Tente de novo mais tarde.");
                    var u = new Uri(Convert.ToString(info["url"]));
                    nvExe = Path.Combine(work, Path.GetFileName(u.LocalPath));
                    Progress("Baixando o driver NVIDIA " + info["latest"] + " (centenas de MB)…");
                    using (var w = new WebClient()) { w.Headers[HttpRequestHeader.UserAgent] = "GROBS-Optimizer"; w.DownloadFile(u, nvExe); }
                }
                else
                {
                    Progress("Procurando o driver mais novo no Catálogo do Microsoft Update…");
                    var best = MsCatalog.Best(MsCatalog.Search(gpu.Key), Win11);
                    if (best == null) throw new InvalidOperationException("O Catálogo não tem driver para esta placa de vídeo neste Windows.");
                    infDir = MsCatalog.Fetch(best.Guid, Path.Combine(work, "pkg"), Progress);
                }

                Progress("Criando ponto de restauração e backup do driver atual…");
                RestorePoints.Ensure();
                string backup = Backup(Gpus().Select(g => g.Inf));
                var locked = LockedIds();
                bool wasLocked = Gpus().Any(g => locked.Contains(g.HwId, StringComparer.OrdinalIgnoreCase));
                if (wasLocked) SetLock(new List<string>());
                var before = OemInfs();
                try
                {
                    Progress("Removendo os drivers de vídeo antigos (a tela pode piscar)…");
                    foreach (var inf in DisplayPackages()) Pnp("/delete-driver " + inf + " /uninstall /force", 180000);
                    Progress("Apagando caches de shader…");
                    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    foreach (var c in new[] { @"NVIDIA\DXCache", @"NVIDIA\GLCache", @"AMD\DxCache", @"AMD\DxcCache", @"AMD\VkCache", @"AMD\GLCache", @"Intel\ShaderCache", "D3DSCache" })
                        try { var p = Path.Combine(local, c); if (Directory.Exists(p)) Directory.Delete(p, true); } catch { }
                    Progress("Instalando o driver novo (pode levar alguns minutos)…");
                    if (nvExe != null)
                        using (var p = Process.Start(new ProcessStartInfo(nvExe, "-s -clean -noreboot -noeula") { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit(30 * 60000);
                    else
                        Pnp("/add-driver \"" + Path.Combine(infDir, "*.inf") + "\" /subdirs /install");
                    Pnp("/scan-devices", 120000);
                }
                finally { if (wasLocked) SetLock(Gpus().Select(g => g.HwId).ToList()); }
                var added = OemInfs().Except(before).ToList();
                if (added.Count > 0) SaveSession(backup, added, new[] { "Instalação limpa: " + gpu.Name });
                try { Directory.Delete(work, true); } catch { }
                var now = Gpus().FirstOrDefault(g => g.DeviceId == gpu.DeviceId);
                _message = now != null ? "Driver de vídeo reinstalado do zero: " + now.Name + " " + now.Version + ". Reinicie o PC." : "Os drivers antigos foram removidos, mas o novo ainda não apareceu. Reinicie o PC; se a tela continuar em resolução baixa, use \"Desfazer\".";
            });
            return Status();
        }

        /// <summary>Pacotes de driver de vídeo (classe Display) de terceiros no repositório do Windows.</summary>
        static List<string> DisplayPackages() =>
            StorePackages().Where(p => string.Equals(p.Cls, "Display", StringComparison.OrdinalIgnoreCase)).Select(p => p.Inf).ToList();

        // ------------------------------------------------------------------ limpar drivers antigos
        public class Pkg { public string Inf, Original, Provider, Cls, Version, Date; public long Size; public bool InUse; }

        static List<Pkg> StorePackages()
        {
            string json = Power.Run(Power.Sys(@"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-WindowsDriver -Online | Select-Object Driver, OriginalFileName, ProviderName, ClassName, Version, @{n='Date';e={$_.Date.ToString('dd/MM/yyyy')}} | ConvertTo-Json -Compress\"", 300000);
            var list = new List<Pkg>();
            try
            {
                var parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json.Trim());
                var arr = parsed as object[] ?? (parsed is Dictionary<string, object> one ? new object[] { one } : new object[0]);
                foreach (Dictionary<string, object> d in arr)
                    list.Add(new Pkg { Inf = Convert.ToString(d["Driver"]), Original = Convert.ToString(d["OriginalFileName"]), Provider = Convert.ToString(d["ProviderName"]), Cls = Convert.ToString(d["ClassName"]), Version = Convert.ToString(d["Version"]), Date = Convert.ToString(d["Date"]) });
            }
            catch { }
            return list;
        }

        static long DirSize(string dir) { try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); } catch { return 0; } }

        /// <summary>Versões antigas e duplicadas de drivers: mantém sempre a que está em uso e a mais nova de cada driver.</summary>
        public static object OldPackages()
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var s = new ManagementObjectSearcher("SELECT InfName FROM Win32_PnPSignedDriver"))
                foreach (ManagementObject o in s.Get()) using (o) { var i = Convert.ToString(o["InfName"]); if (!string.IsNullOrEmpty(i)) used.Add(i); }
            var pk = StorePackages();
            foreach (var p in pk) p.InUse = used.Contains(p.Inf);
            var old = new List<Pkg>();
            foreach (var g in pk.GroupBy(p => Path.GetFileName(p.Original).ToLowerInvariant() + "|" + p.Provider.ToLowerInvariant() + "|" + p.Cls.ToLowerInvariant()))
            {
                if (g.Count() < 2) continue;
                var newest = g.OrderByDescending(p => MsCatalog.Ver(p.Version)).First();
                foreach (var p in g) if (!p.InUse && p != newest) { p.Size = DirSize(Path.GetDirectoryName(p.Original)); old.Add(p); }
            }
            return old.OrderByDescending(p => p.Size).Select(p => (object)new Dictionary<string, object> {
                { "inf", p.Inf }, { "name", Path.GetFileName(p.Original) }, { "provider", p.Provider }, { "cls", p.Cls }, { "version", p.Version }, { "date", p.Date }, { "size", (double)p.Size } }).ToList();
        }

        public static object DeletePackages(string[] infs)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            // só aceita o que a análise considera antigo (nunca o driver em uso nem o mais novo de cada um)
            var sizes = ((IEnumerable<object>)OldPackages()).Cast<Dictionary<string, object>>().ToDictionary(d => (string)d["inf"], d => (double)d["size"], StringComparer.OrdinalIgnoreCase);
            int ok = 0; double freed = 0;
            foreach (var inf in infs.Where(sizes.ContainsKey))
            {
                Pnp("/delete-driver " + inf, 120000); // sem /force: o Windows recusa se estiver em uso
                if (!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF", inf))) { ok++; freed += sizes[inf]; }
            }
            return new Dictionary<string, object> { { "done", ok }, { "freed", freed } };
        }

        // ------------------------------------------------------------------ pacote de drivers de rede para depois de formatar
        public const string NetFolder = "GROBS-Drivers-Rede";
        // chips de rede cabeada e Wi-Fi mais comuns (Realtek, Intel, Qualcomm Atheros, MediaTek, Killer)
        static readonly string[] NetKeys = {
            @"PCI\VEN_10EC&DEV_8168", @"PCI\VEN_10EC&DEV_8136", @"PCI\VEN_10EC&DEV_8125", @"PCI\VEN_8086&DEV_15B8", @"PCI\VEN_8086&DEV_1539", @"PCI\VEN_8086&DEV_15F3",
            @"PCI\VEN_8086&DEV_125C", @"PCI\VEN_8086&DEV_153B", @"PCI\VEN_8086&DEV_1503", @"PCI\VEN_1969&DEV_1091", @"PCI\VEN_1969&DEV_10A1", @"PCI\VEN_1969&DEV_E091",
            @"PCI\VEN_8086&DEV_2723", @"PCI\VEN_8086&DEV_2725", @"PCI\VEN_8086&DEV_2526", @"PCI\VEN_8086&DEV_24FD", @"PCI\VEN_8086&DEV_24F3", @"PCI\VEN_8086&DEV_095A",
            @"PCI\VEN_8086&DEV_3165", @"PCI\VEN_8086&DEV_24FB", @"PCI\VEN_8086&DEV_A0F0", @"PCI\VEN_8086&DEV_02F0", @"PCI\VEN_8086&DEV_51F0", @"PCI\VEN_8086&DEV_7AF0",
            @"PCI\VEN_10EC&DEV_C821", @"PCI\VEN_10EC&DEV_C822", @"PCI\VEN_10EC&DEV_B822", @"PCI\VEN_10EC&DEV_8852", @"PCI\VEN_10EC&DEV_B852", @"PCI\VEN_10EC&DEV_C852",
            @"PCI\VEN_10EC&DEV_D723", @"PCI\VEN_10EC&DEV_B723", @"PCI\VEN_10EC&DEV_8179", @"PCI\VEN_14C3&DEV_7961", @"PCI\VEN_14C3&DEV_0608", @"PCI\VEN_14C3&DEV_0616",
            @"PCI\VEN_168C&DEV_0042", @"PCI\VEN_168C&DEV_003E", @"PCI\VEN_168C&DEV_0036", @"PCI\VEN_168C&DEV_0032", @"PCI\VEN_168C&DEV_0034", @"PCI\VEN_168C&DEV_002B",
            @"USB\VID_0BDA&PID_8179", @"USB\VID_0BDA&PID_B812", @"USB\VID_0BDA&PID_C811", @"USB\VID_0BDA&PID_F179", @"USB\VID_0BDA&PID_818B", @"USB\VID_0BDA&PID_8812" };

        /// <summary>
        /// Baixa do Catálogo do Microsoft Update os drivers de rede mais comuns (Windows 10 e 11, 64 bits) para uma pasta,
        /// normalmente num pendrive. Depois de formatar, o instalador do GROBS (ou o próprio GROBS) instala dali, sem internet.
        /// </summary>
        public static object NetPack(string target)
        {
            if (string.IsNullOrEmpty(target) || !Directory.Exists(target)) throw new InvalidOperationException("Escolha uma pasta ou pendrive.");
            Run("netpack", () =>
            {
                string dir = Path.Combine(target, NetFolder);
                Directory.CreateDirectory(dir);
                var picks = new Dictionary<string, MsCatalog.Row>();
                int n = 0;
                Parallel.ForEach(NetKeys, new ParallelOptions { MaxDegreeOfParallelism = 4 }, k =>
                {
                    try
                    {
                        var best = MsCatalog.Best(MsCatalog.Search(k), false); // versão para Windows 10 (serve no 11)
                        if (best != null) lock (picks) picks[best.Guid] = best;
                    }
                    catch { }
                    Progress("Procurando drivers de rede no Catálogo (" + Interlocked.Increment(ref n) + " de " + NetKeys.Length + ")…");
                });
                int i = 0, ok = 0;
                foreach (var r in picks.Values.OrderBy(r => r.Title))
                {
                    i++;
                    string name = Regex.Replace(r.Title, @"[^\w\.\- ]", "").Trim();
                    if (name.Length > 70) name = name.Substring(0, 70).Trim();
                    try { MsCatalog.Fetch(r.Guid, Path.Combine(dir, name), s => Progress("(" + i + " de " + picks.Count + ") " + s)); ok++; }
                    catch { }
                }
                File.WriteAllText(Path.Combine(dir, "LEIA-ME.txt"),
                    "Drivers de rede cabeada e Wi-Fi mais comuns, baixados do Catálogo do Microsoft Update pelo GROBS Optimizer em " + DateTime.Now.ToString("dd/MM/yyyy") + ".\r\n\r\n" +
                    "Depois de formatar: deixe esta pasta no pendrive junto com o GROBS-Optimizer-Setup.exe (ou na raiz do pendrive) e rode o instalador;\r\n" +
                    "ele oferece \"Instalar drivers de rede\". Também dá para instalar pelo GROBS em Drivers > Rede para formatar.\r\n", Encoding.UTF8);
                _message = ok + " pacote(s) de driver de rede salvos em " + dir + " (" + (DirSize(dir) / 1048576.0).ToString("0") + " MB).";
            });
            return Status();
        }

        public static object NetInstall(string folder)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            string dir = Directory.Exists(Path.Combine(folder ?? "", NetFolder)) ? Path.Combine(folder, NetFolder) : folder;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || !Directory.EnumerateFiles(dir, "*.inf", SearchOption.AllDirectories).Any())
                throw new InvalidOperationException("Essa pasta não tem drivers de rede do GROBS.");
            Run("installing", () =>
            {
                Progress("Instalando drivers de rede…");
                Pnp("/add-driver \"" + Path.Combine(dir, "*.inf") + "\" /subdirs /install");
                Pnp("/scan-devices", 120000);
                _message = "Drivers de rede instalados. Se a internet não voltar, reinicie o PC.";
            });
            return Status();
        }

        // ------------------------------------------------------------------ GitHub: áudio Realtek genérico (pal1000/Realtek-UAD-generic)
        public static object RealtekInfo()
        {
            var dev = Devices().FirstOrDefault(d => d.Key.StartsWith(@"HDAUDIO\FUNC_01&VEN_10EC", StringComparison.OrdinalIgnoreCase));
            var r = new Dictionary<string, object> { { "device", dev?.Name }, { "current", dev?.Version } };
            try
            {
                using (var w = new WebClient())
                {
                    w.Headers[HttpRequestHeader.UserAgent] = "GROBS-Optimizer";
                    var rel = new JavaScriptSerializer().DeserializeObject(w.DownloadString("https://api.github.com/repos/pal1000/Realtek-UAD-generic/releases/latest")) as Dictionary<string, object>;
                    r["latest"] = rel["tag_name"];
                    // o pacote do driver (o outro arquivo da release é só o painel de controle)
                    r["url"] = (rel["assets"] as object[]).Cast<Dictionary<string, object>>().Where(x => Convert.ToString(x["name"]).StartsWith("Unofficial-Realtek-UAD-generic")).Select(x => Convert.ToString(x["browser_download_url"])).FirstOrDefault();
                    r["newer"] = dev != null && MsCatalog.Ver(Convert.ToString(rel["tag_name"])) > MsCatalog.Ver(dev.Version);
                }
            }
            catch (Exception ex) { r["error"] = "GitHub indisponível: " + ex.Message; }
            return r;
        }

        /// <summary>Baixa e extrai o pacote (com o 7zr oficial do 7-zip.org) e abre a pasta: a troca do driver Realtek é manual.</summary>
        public static object RealtekDownload()
        {
            Run("installing", () =>
            {
                var info = (Dictionary<string, object>)RealtekInfo();
                string url = Convert.ToString(info.ContainsKey("url") ? info["url"] : null);
                if (string.IsNullOrEmpty(url) || !url.StartsWith("https://github.com/pal1000/Realtek-UAD-generic/")) throw new InvalidOperationException("Não encontrei o pacote no GitHub.");
                string dir = Path.Combine(Root, "realtek-uad"); Directory.CreateDirectory(dir);
                string sz = Path.Combine(Root, "7zr.exe"), pkg = Path.Combine(dir, Path.GetFileName(new Uri(url).LocalPath));
                using (var w = new WebClient())
                {
                    w.Headers[HttpRequestHeader.UserAgent] = "GROBS-Optimizer";
                    if (!File.Exists(sz)) { Progress("Baixando o 7-Zip (7zr) do site oficial…"); w.DownloadFile("https://www.7-zip.org/a/7zr.exe", sz); }
                    Progress("Baixando o áudio Realtek genérico " + info["latest"] + " do GitHub…");
                    w.DownloadFile(url, pkg);
                }
                Progress("Extraindo…");
                string outDir = Path.Combine(dir, Path.GetFileNameWithoutExtension(pkg));
                Power.Run(sz, "x -y -o\"" + outDir + "\" \"" + pkg + "\"", 300000);
                try { File.Delete(pkg); } catch { }
                Process.Start("explorer.exe", "\"" + outDir + "\"");
                Process.Start(new ProcessStartInfo("https://github.com/pal1000/Realtek-UAD-generic#switching-from-other-realtek-hd-audio-drivers") { UseShellExecute = true });
                _message = "Pacote extraído em " + outDir + ". A troca é manual: siga o passo a passo que abriu no navegador (remover o driver Realtek atual e rodar o Setup do pacote).";
            });
            return Status();
        }
    }
}
