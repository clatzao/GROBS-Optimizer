using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;

namespace Grobs
{
    /// <summary>
    /// Atualizador de drivers: procura no catálogo oficial do Windows Update (drivers assinados pela Microsoft)
    /// pela API do Windows Update Agent e instala os escolhidos. A verificação das GPUs fica em GpuDrivers.
    /// </summary>
    internal static class Drivers
    {
        static readonly object _lock = new object();
        static volatile string _state = "idle"; // idle | searching | installing
        static string _progress = "", _error;
        static List<object> _found;
        static DateTime? _when;
        static Dictionary<string, object> _lastInstall;

        public static object Status() => new Dictionary<string, object> {
            { "state", _state }, { "progress", _progress }, { "error", _error }, { "items", _found },
            { "when", _when?.ToString("dd/MM/yyyy HH:mm") }, { "lastInstall", _lastInstall }, { "admin", Program.IsAdmin } };

        public static object Search()
        {
            lock (_lock)
            {
                if (_state != "idle") return Status();
                _state = "searching"; _error = null; _progress = "Conectando ao Windows Update…";
            }
            var t = new Thread(() =>
            {
                try
                {
                    dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session"));
                    session.ClientApplicationID = "GROBS Optimizer";
                    dynamic searcher = session.CreateUpdateSearcher();
                    searcher.Online = true;
                    _progress = "Procurando drivers para o seu hardware (pode levar 1–2 minutos)…";
                    dynamic result = searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");
                    var list = new List<object>();
                    int n = result.Updates.Count;
                    for (int i = 0; i < n; i++)
                    {
                        dynamic u = result.Updates.Item(i);
                        string id = u.Identity.UpdateID;

                        DateTime? date = null; try { date = (DateTime)u.DriverVerDate; } catch { }
                        list.Add(new Dictionary<string, object> {
                            { "id", id }, { "title", (string)u.Title }, { "maker", Safe(() => (string)u.DriverManufacturer) },
                            { "model", Safe(() => (string)u.DriverModel) }, { "cls", Safe(() => (string)u.DriverClass) },
                            { "date", date?.ToString("dd/MM/yyyy") ?? "" }, { "size", Convert.ToDouble(Safe(() => (object)u.MaxDownloadSize) ?? 0) } });
                    }
                    lock (_lock) { _found = list; _when = DateTime.Now; }
                }
                catch (Exception ex)
                {
                    _error = ex.HResult == unchecked((int)0x8024001E) || ex.Message.Contains("0x80070422")
                        ? "O serviço do Windows Update está desativado. Reative-o na aba Windows Update para buscar drivers."
                        : "Falha na busca: " + ex.Message;
                }
                finally { _state = "idle"; _progress = ""; Program.Window?.Notify("drivers", Status()); }
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            return Status();
        }

        static T Safe<T>(Func<T> f) { try { return f(); } catch { return default(T); } }

        public static object Install(string[] ids)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            lock (_lock)
            {
                if (_state != "idle") throw new InvalidOperationException("Aguarde a operação atual terminar");
                _state = "installing"; _error = null;
            }
            var t = new Thread(() =>
            {
                try
                {
                    dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session"));
                    session.ClientApplicationID = "GROBS Optimizer";
                    dynamic coll = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl"));
                    _progress = "Preparando…";
                    dynamic searcher = session.CreateUpdateSearcher();
                    searcher.Online = true;
                    // objetos COM não podem atravessar threads STA: busca de novo pelos IDs escolhidos
                    dynamic found = searcher.Search(string.Join(" or ", ids.Select(id => "UpdateID='" + id.Replace("'", "") + "'"))).Updates;
                    for (int i = 0; i < found.Count; i++)
                    {
                        dynamic u = found.Item(i);
                        try { if (!u.EulaAccepted) u.AcceptEula(); } catch { }
                        coll.Add(u);
                    }
                    if (coll.Count == 0) throw new InvalidOperationException("Nada selecionado");
                    _progress = "Baixando " + coll.Count + " driver(s)…";
                    dynamic dl = session.CreateUpdateDownloader(); dl.Updates = coll; dl.Download();
                    _progress = "Instalando… não desligue o PC.";
                    dynamic inst = session.CreateUpdateInstaller(); inst.Updates = coll;
                    dynamic res = inst.Install();
                    int ok = 0;
                    for (int i = 0; i < coll.Count; i++) if ((int)res.GetUpdateResult(i).ResultCode == 2) ok++;
                    _lastInstall = new Dictionary<string, object> { { "ok", ok }, { "total", (int)coll.Count }, { "reboot", (bool)res.RebootRequired } };
                    lock (_lock) { _found = _found?.Cast<Dictionary<string, object>>().Where(f => !ids.Contains((string)f["id"])).Cast<object>().ToList(); }
                }
                catch (Exception ex) { _error = "Falha na instalação: " + ex.Message; }
                finally { _state = "idle"; _progress = ""; Program.Window?.Notify("drivers", Status()); }
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            return Status();
        }

        /// <summary>
        /// Dispositivos que precisam de driver (Gerenciador de Dispositivos com erro 1, 18, 28, 31, 37, 39...),
        /// com o ID de hardware para buscar o driver certo no Catálogo do Microsoft Update.
        /// </summary>
        public static object Missing()
        {
            var list = new List<object>();
            var codes = new[] { 1, 3, 10, 18, 24, 28, 31, 32, 37, 39, 40, 41, 48, 52 };
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, ConfigManagerErrorCode, HardwareID, PNPClass, DeviceID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
                    foreach (ManagementObject o in s.Get())
                    {
                        int code = Convert.ToInt32(o["ConfigManagerErrorCode"]);
                        if (!codes.Contains(code)) continue;
                        var ids = o["HardwareID"] as string[] ?? new string[0];
                        string hw = ids.FirstOrDefault() ?? Convert.ToString(o["DeviceID"]);
                        // forma curta e buscável: PCI\VEN_10EC&DEV_8168 / USB\VID_0BDA&PID_8179
                        var m = System.Text.RegularExpressions.Regex.Match(hw, @"(VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}|VID_[0-9A-F]{4}&PID_[0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        string key = m.Success ? m.Value.ToUpperInvariant() : hw;
                        list.Add(new Dictionary<string, object> {
                            { "name", Convert.ToString(o["Name"] ?? "Dispositivo desconhecido") }, { "code", code }, { "hwid", hw }, { "search", key },
                            { "cls", Convert.ToString(o["PNPClass"] ?? "") },
                            { "catalog", "https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(key) },
                            { "reason", code == 28 || code == 1 ? "Sem driver instalado" : code == 18 ? "Driver precisa ser reinstalado" : code == 10 ? "Não consegue iniciar" : code == 31 || code == 39 ? "Driver não carrega / corrompido" : "Erro " + code } });
                    }
            }
            catch { }
            return list;
        }

        /// <summary>Drivers de terceiros instalados (Win32_PnPSignedDriver), do mais antigo para o mais novo.</summary>
        public static object Installed()
        {
            var list = new List<Dictionary<string, object>>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT DeviceName, DriverProviderName, DriverVersion, DriverDate, DeviceClass FROM Win32_PnPSignedDriver WHERE DriverProviderName IS NOT NULL"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string prov = Convert.ToString(o["DriverProviderName"]);
                        string name = Convert.ToString(o["DeviceName"]);
                        if (string.IsNullOrEmpty(name) || prov.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) || prov.Equals("(Standard system devices)", StringComparison.OrdinalIgnoreCase)) continue;
                        DateTime? d = null;
                        try { d = ManagementDateTimeConverter.ToDateTime(Convert.ToString(o["DriverDate"])); } catch { }
                        list.Add(new Dictionary<string, object> {
                            { "name", name }, { "provider", prov }, { "version", Convert.ToString(o["DriverVersion"]) },
                            { "date", d?.ToString("dd/MM/yyyy") ?? "" }, { "ts", d?.Ticks ?? 0 }, { "cls", Convert.ToString(o["DeviceClass"]) },
                            { "years", d.HasValue ? Math.Round((DateTime.Now - d.Value).TotalDays / 365, 1) : 0 } });
                    }
            }
            catch { }
            return list.GroupBy(x => (string)x["name"] + x["version"]).Select(g => g.First()).OrderBy(x => (long)x["ts"]).Cast<object>().ToList();
        }
    }
}
