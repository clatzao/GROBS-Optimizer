using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml.Linq;

namespace Grobs
{
    /// <summary>
    /// Versão mais recente do driver de vídeo direto do fabricante.
    /// NVIDIA: serviço de busca do site (o mesmo do formulário "Drivers"); Intel: Driver &amp; Support Assistant; AMD: autodetecção oficial.
    /// </summary>
    internal static class GpuDrivers
    {
        const string NvLookup = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3";
        const string NvDriver = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&psid={0}&pfid={1}&osID={2}&languageCode=1033&isWHQL=1&dch=1&sort1=0&numberOfResults=1";
        static volatile string _dlState = ""; static long _dlDone, _dlTotal;

        static WebClient Client()
        {
            var c = new WebClient();
            c.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) GROBS-Optimizer";
            return c;
        }

        static string Norm(string s) => Regex.Replace((s ?? "").ToLowerInvariant().Replace("nvidia", "").Replace("geforce", ""), @"[^a-z0-9]", "");

        /// <summary>"32.0.15.6094" -> "560.94" (formato que a NVIDIA divulga).</summary>
        static string NvVersion(string wmi)
        {
            var digits = (wmi ?? "").Replace(".", "");
            if (digits.Length < 5) return wmi;
            var last = digits.Substring(digits.Length - 5);
            return last.Substring(0, 3) + "." + last.Substring(3);
        }

        public static object Check()
        {
            var result = new List<object>();
            var gpus = new List<ManagementObject>();
            using (var s = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController"))
                foreach (ManagementObject o in s.Get()) gpus.Add(o);
            bool win11 = Environment.OSVersion.Version.Build >= 22000;
            foreach (var g in gpus)
            {
                string name = Convert.ToString(g["Name"]), pnp = Convert.ToString(g["PNPDeviceID"]), wmiVer = Convert.ToString(g["DriverVersion"]);
                DateTime? date = null; try { date = ManagementDateTimeConverter.ToDateTime(Convert.ToString(g["DriverDate"])); } catch { }
                var d = new Dictionary<string, object> { { "name", name }, { "installed", wmiVer }, { "date", date?.ToString("dd/MM/yyyy") ?? "" } };
                if (pnp.Contains("VEN_10DE"))
                {
                    d["vendor"] = "nvidia"; d["installed"] = NvVersion(wmiVer);
                    try
                    {
                        using (var c = Client())
                        {
                            var xml = XDocument.Parse(c.DownloadString(NvLookup));
                            string target = Norm(name);
                            var hit = xml.Descendants("LookupValue").FirstOrDefault(x => Norm((string)x.Element("Name")) == target)
                                   ?? xml.Descendants("LookupValue").FirstOrDefault(x => target.EndsWith(Norm((string)x.Element("Name"))) && Norm((string)x.Element("Name")).Length > 4);
                            if (hit == null) throw new InvalidOperationException("Modelo não encontrado no catálogo da NVIDIA");
                            string pfid = (string)hit.Element("Value"), psid = (string)hit.Attribute("ParentID") ?? (string)hit.Element("ParentID");
                            var json = c.DownloadString(string.Format(NvDriver, psid, pfid, win11 ? 135 : 57));
                            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                            var info = (Dictionary<string, object>)((Dictionary<string, object>)((object[])root["IDS"])[0])["downloadInfo"];
                            string latest = Uri.UnescapeDataString(Convert.ToString(info["Version"]));
                            d["latest"] = latest;
                            d["url"] = Uri.UnescapeDataString(Convert.ToString(info["DownloadURL"]));
                            d["released"] = Uri.UnescapeDataString(Convert.ToString(info.ContainsKey("ReleaseDateTime") ? info["ReleaseDateTime"] : ""));
                            double a, b;
                            d["outdated"] = double.TryParse(latest, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out a)
                                && double.TryParse((string)d["installed"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out b) && a > b;
                        }
                    }
                    catch (Exception ex) { d["error"] = ex.Message; d["page"] = "https://www.nvidia.com/pt-br/drivers/"; }
                }
                else if (pnp.Contains("VEN_1002"))
                {
                    d["vendor"] = "amd"; d["page"] = "https://www.amd.com/pt/support/download/drivers.html";
                    d["note"] = "A AMD não publica a versão por API: use o instalador de autodetecção oficial.";
                }
                else if (pnp.Contains("VEN_8086"))
                {
                    d["vendor"] = "intel"; d["tool"] = "https://dsadata.intel.com/installer"; d["page"] = "https://www.intel.com.br/content/www/br/pt/support/detect.html";
                    d["note"] = "O Intel Driver & Support Assistant detecta e instala os drivers de vídeo, Wi‑Fi e chipset Intel.";
                }
                else continue;
                result.Add(d);
            }
            return result;
        }

        public static object DownloadStatus() => new Dictionary<string, object> { { "state", _dlState }, { "done", (double)_dlDone }, { "total", (double)_dlTotal } };

        /// <summary>Baixa o instalador oficial (somente domínios dos fabricantes) e o executa.</summary>
        public static object DownloadAndRun(string url)
        {
            var u = new Uri(url);
            var allowed = new[] { "download.nvidia.com", "us.download.nvidia.com", "international.download.nvidia.com", "dsadata.intel.com" };
            if (u.Scheme != "https" || !allowed.Any(h => u.Host.Equals(h, StringComparison.OrdinalIgnoreCase))) throw new UnauthorizedAccessException("Endereço não permitido");
            if (_dlState == "downloading") throw new InvalidOperationException("Já existe um download em andamento");
            string dir = Path.Combine(Program.DataDir, "downloads"); Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, u.Host.Contains("intel") ? "Intel-Driver-and-Support-Assistant-Installer.exe" : Path.GetFileName(u.LocalPath));
            _dlState = "downloading"; _dlDone = 0; _dlTotal = 0;
            var t = new Thread(() =>
            {
                try
                {
                    using (var c = Client())
                    {
                        c.DownloadProgressChanged += (s, e) => { _dlDone = e.BytesReceived; _dlTotal = e.TotalBytesToReceive; };
                        c.DownloadFileTaskAsync(u, file).Wait();
                    }
                    _dlState = "running";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
                    _dlState = "done";
                }
                catch (Exception ex) { _dlState = "error: " + (ex.InnerException ?? ex).Message; }
                Program.Window?.Notify("gpuDownload", DownloadStatus());
            }) { IsBackground = true };
            t.Start();
            return DownloadStatus();
        }
    }
}
