using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>
    /// Perfis de debloat de 1 clique (Seguro / Recomendado / Extremo). Detectam o tipo de PC e mantêm o que ele usa
    /// (impressora, Bluetooth, biometria, scanner, token A3, notebook, Xbox). Antes de aplicar mostram cada item com o
    /// risco; aplicam com ponto de restauração e guardam o que foi feito para desfazer o que é reversível.
    /// </summary>
    internal static class DebloatProfiles
    {
        static volatile bool _busy;
        static string _phase = "", _error, _message;
        static int _done, _total;

        public static object Status() => new Dictionary<string, object> {
            { "running", _busy }, { "phase", _phase }, { "error", _error }, { "message", _message }, { "done", _done }, { "total", _total },
            { "last", Settings.Get<object>("debloatLast") != null }, { "admin", Program.IsAdmin } };

        static void Push() => Program.Window?.Notify("debloatProfile", Status());

        // ------------------------------------------------------------------ tipo de PC
        static int Count(string wql) { try { using (var s = new ManagementObjectSearcher(wql)) return s.Get().Count; } catch { return 0; } }

        public static Dictionary<string, object> Detect()
        {
            bool printer = false;
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, PortName FROM Win32_Printer"))
                    foreach (ManagementObject o in s.Get())
                        using (o)
                        {
                            string n = Convert.ToString(o["Name"]), port = Convert.ToString(o["PortName"]);
                            if (!(n.Contains("PDF") || n.Contains("XPS") || n.Contains("OneNote") || n.Contains("Fax") || port.StartsWith("PORTPROMPT") || port == "nul:")) printer = true;
                        }
            }
            catch { }
            bool xbox = false;
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                xbox = k?.GetSubKeyNames().Any(n => n.StartsWith("Microsoft.GamingApp_", StringComparison.OrdinalIgnoreCase)) ?? false;
            return new Dictionary<string, object> {
                { "laptop", Count("SELECT * FROM Win32_Battery") > 0 },
                { "printer", printer },
                { "bluetooth", Count("SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth' AND Status = 'OK'") > 0 },
                { "biometric", Count("SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Biometric'") > 0 },
                { "scanner", Count("SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Image'") > 0 },
                { "smartcard", Count("SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'SmartCardReader'") > 0 },
                { "xbox", xbox } };
        }

        static readonly Dictionary<string, string> KeepWhy = new Dictionary<string, string> {
            { "printer", "você tem impressora" }, { "bluetooth", "o PC usa Bluetooth" }, { "biometric", "o PC tem leitor biométrico/Windows Hello" },
            { "scanner", "o PC tem scanner ou câmera de imagem" }, { "smartcard", "o PC tem leitor de cartão/token" }, { "laptop", "é um notebook" }, { "xbox", "o app Xbox está instalado" } };

        // ------------------------------------------------------------------ plano
        static readonly string[] TweakGroups = { "IA e assistentes", "Barra de tarefas", "Anúncios e sugestões", "Privacidade", "Telemetria", "Apps em segundo plano", "Explorador e menu", "Sistema", "Liberar espaço do sistema" };

        /// <summary>level: safe | recommended | extreme. Itens = { key, kind, id, title, group, risk, skip (motivo para manter) }.</summary>
        public static object Plan(string level)
        {
            int lv = level == "extreme" ? 3 : level == "recommended" ? 2 : 1;
            var pc = Detect();
            Func<string, bool> has = k => k != null && pc.ContainsKey(k) && (bool)pc[k];
            var items = new List<Dictionary<string, object>>();
            Action<string, string, string, string, string, string> add = (kind, id, title, group, risk, skip) =>
                items.Add(new Dictionary<string, object> { { "key", kind + ":" + id }, { "kind", kind }, { "id", id }, { "title", title }, { "group", group }, { "risk", risk }, { "skip", skip } });
            Func<string, int> rank = r => r == "low" || r == "safe" ? 1 : r == "medium" || r == "review" ? 2 : 3;

            // ajustes do registro (os que já estão ligados ficam de fora)
            foreach (Dictionary<string, object> t in (IEnumerable<object>)Tweaks.List(TweakGroups))
            {
                if ((bool)t["on"] || (bool)t["locked"]) continue;
                string risk = (string)t["risk"];
                if (rank(risk) > lv) continue;
                if ((string)t["id"] == "onedrive" && lv < 3) continue;
                add("tweak", (string)t["id"], (string)t["title"], (string)t["group"], risk, null);
            }
            // serviços
            foreach (Dictionary<string, object> s in (IEnumerable<object>)Services.List())
            {
                if ((bool)s["disabled"]) continue;
                var meta = Services.Catalog.First(x => x.Name == (string)s["name"]);
                if (rank(meta.Risk) > lv) continue;
                if (lv < 3 && (meta.Name == "WSearch" || meta.Name == "SysMain")) continue; // deixam o PC mais lento em muitos casos
                add("service", meta.Name, "Serviço: " + meta.Title, "Serviços", meta.Risk, has(meta.Keep) ? "mantido porque " + KeepWhy[meta.Keep] : null);
            }
            // apps pré-instalados
            foreach (Dictionary<string, object> a in (IEnumerable<object>)DeepDebloat.Apps())
            {
                string risk = (string)a["risk"];
                if (rank(risk) > lv) continue;
                string name = (string)a["name"];
                bool xboxApp = name.IndexOf("Xbox", StringComparison.OrdinalIgnoreCase) >= 0 || name == "Microsoft.GamingApp";
                add("app", name, "Remover app: " + a["title"], "Apps", risk == "safe" ? "low" : risk == "review" ? "medium" : "high", xboxApp && has("xbox") ? "mantido porque " + KeepWhy["xbox"] : null);
            }
            if (lv >= 3)
            {
                var st = DeepDebloat.Status() as Dictionary<string, object>;
                if ((bool)st["edge"]) add("edge", "edge", "Remover o Microsoft Edge (o WebView2 fica)", "Edge e OneDrive", "high", null);
                if ((bool)st["onedrive"]) add("onedrive", "onedrive", "Remover o OneDrive", "Edge e OneDrive", "high", null);
                var wu = WinUpdate.Status() as Dictionary<string, object>;
                if (Convert.ToString(wu["mode"]) == "auto") add("wu", "manual", "Windows Update só manual", "Windows Update", "high", null);
            }
            return new Dictionary<string, object> { { "pc", pc }, { "items", items } };
        }

        // ------------------------------------------------------------------ aplicar / desfazer
        public static object Apply(string[] keys)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            if (_busy) throw new InvalidOperationException("Já existe um perfil sendo aplicado.");
            _busy = true; _error = null; _message = null; _done = 0; _total = keys.Length;
            new Thread(() =>
            {
                var applied = new List<object>(); var fails = new List<string>();
                try
                {
                    _phase = "Criando ponto de restauração"; Push();
                    RestorePoints.Ensure();
                    var apps = keys.Where(k => k.StartsWith("app:")).Select(k => k.Substring(4)).ToArray();
                    foreach (var key in keys.Where(k => !k.StartsWith("app:")))
                    {
                        string kind = key.Substring(0, key.IndexOf(':')), id = key.Substring(key.IndexOf(':') + 1);
                        _phase = key; Push();
                        try
                        {
                            switch (kind)
                            {
                                case "tweak": Tweaks.Set(id, true); break;
                                case "service": Services.Set(id, true); break;
                                case "edge": DeepDebloat.RemoveEdge(); break;
                                case "onedrive": DeepDebloat.RemoveOneDrive(); break;
                                case "wu": WinUpdate.Manual(); break;
                            }
                            applied.Add(key);
                        }
                        catch (Exception ex) { fails.Add(id + ": " + ex.Message); }
                        _done++;
                    }
                    if (apps.Length > 0)
                    {
                        _phase = "Removendo " + apps.Length + " app(s)"; Push();
                        var r = DeepDebloat.RemoveApps(apps, true) as Dictionary<string, object>;
                        foreach (var a in (List<string>)r["done"]) applied.Add("app:" + a);
                        foreach (var a in (List<string>)r["failed"]) fails.Add(a);
                        _done += apps.Length;
                    }
                    Settings.Set("debloatLast", applied);
                    _message = applied.Count + " item(ns) aplicado(s)" + (fails.Count > 0 ? "; " + fails.Count + " o Windows recusou" : "") + ". Reinicie o PC para concluir.";
                    Tools.RestartExplorer();
                }
                catch (Exception ex) { _error = ex.Message; Program.Log(ex); }
                _busy = false; _phase = ""; Push();
            }) { IsBackground = true, Name = "DebloatProfile" }.Start();
            return Status();
        }

        /// <summary>Desfaz o último perfil: ajustes, serviços e Windows Update voltam ao padrão. Apps removidos voltam pela Microsoft Store.</summary>
        public static object Undo()
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            var last = (Settings.Get<object>("debloatLast") as System.Collections.IEnumerable)?.Cast<object>().Select(Convert.ToString).ToList() ?? new List<string>();
            int ok = 0;
            foreach (var key in last)
            {
                string kind = key.Substring(0, key.IndexOf(':')), id = key.Substring(key.IndexOf(':') + 1);
                try
                {
                    if (kind == "tweak") { Tweaks.Set(id, false); ok++; }
                    else if (kind == "service") { Services.Set(id, false); ok++; }
                    else if (kind == "wu") { WinUpdate.Enable(); ok++; }
                }
                catch (Exception ex) { Program.Log(ex); }
            }
            Settings.Set("debloatLast", null);
            Tools.RestartExplorer();
            _message = ok + " ajuste(s) desfeito(s). Apps removidos podem ser reinstalados pela Microsoft Store; Edge e OneDrive pelos sites oficiais.";
            Push();
            return Status();
        }

        // ------------------------------------------------------------------ espaço do sistema
        static volatile bool _cleanBusy;
        public static object CleanupComponents()
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            if (_cleanBusy || _busy) throw new InvalidOperationException("Aguarde a operação atual terminar.");
            _cleanBusy = true; _busy = true; _error = null; _message = null; _phase = "Limpando componentes antigos do Windows (pode levar de 10 a 30 minutos)"; Push();
            new Thread(() =>
            {
                try
                {
                    var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                    long before = drive.AvailableFreeSpace;
                    Power.Run(Power.Sys("dism.exe"), "/Online /Cleanup-Image /StartComponentCleanup /ResetBase", 3600000);
                    drive = new DriveInfo(drive.Name);
                    double freed = Math.Max(0, drive.AvailableFreeSpace - before) / 1073741824.0;
                    _message = "Componentes antigos removidos: " + freed.ToString("0.0") + " GB liberados.";
                }
                catch (Exception ex) { _error = ex.Message; }
                _cleanBusy = false; _busy = false; _phase = ""; Push();
            }) { IsBackground = true }.Start();
            return Status();
        }

        /// <summary>Idiomas instalados além do idioma do Windows e do inglês (pacotes de fala, escrita, OCR...).</summary>
        public static object Languages()
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            string ui = System.Globalization.CultureInfo.InstalledUICulture.Name;
            string o = Power.Run(Power.Sys(@"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -Command \"(Get-WindowsCapability -Online | Where-Object { $_.State -eq 'Installed' -and $_.Name -like 'Language.*' }).Name\"", 300000);
            return o.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l.Split('~').Length > 3)
                .GroupBy(l => l.Split('~')[3]).Where(g => g.Key != "" && !g.Key.Equals(ui, StringComparison.OrdinalIgnoreCase) && !g.Key.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                .Select(g => (object)new Dictionary<string, object> { { "locale", g.Key }, { "name", Lang(g.Key) }, { "caps", g.ToList() } }).ToList();
        }

        static string Lang(string loc) { try { return System.Globalization.CultureInfo.GetCultureInfo(loc).NativeName; } catch { return loc; } }

        public static object RemoveLanguage(string locale)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            foreach (Dictionary<string, object> l in (IEnumerable<object>)Languages())
                if ((string)l["locale"] == locale)
                    foreach (string cap in (List<string>)l["caps"])
                        Power.Run(Power.Sys(@"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -Command \"Remove-WindowsCapability -Online -Name '" + cap.Replace("'", "") + "' | Out-Null\"", 600000);
            return Languages();
        }
    }
}
