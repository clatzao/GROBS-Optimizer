using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>
    /// Remoção profunda: apps da Store para todos os usuários e para contas novas (provisionados),
    /// Microsoft Edge (o WebView2, usado pelo GROBS e por outros apps, fica), OneDrive e recursos opcionais do Windows.
    /// </summary>
    internal static class DeepDebloat
    {
        // nome do pacote -> (título, risco). "review" = útil para algumas pessoas.
        static readonly Dictionary<string, string[]> Catalog = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.BingNews", new[] { "Notícias", "safe" } }, { "Microsoft.News", new[] { "Notícias (novo)", "safe" } }, { "Microsoft.BingWeather", new[] { "Clima", "safe" } },
            { "Microsoft.BingSearch", new[] { "Pesquisa Bing", "safe" } }, { "Microsoft.BingFinance", new[] { "Finanças", "safe" } }, { "Microsoft.BingSports", new[] { "Esportes", "safe" } },
            { "Microsoft.BingTravel", new[] { "Viagens", "safe" } }, { "Microsoft.BingFoodAndDrink", new[] { "Comidas e Bebidas", "safe" } }, { "Microsoft.BingHealthAndFitness", new[] { "Saúde e Bem-estar", "safe" } },
            { "Microsoft.BingTranslator", new[] { "Tradutor", "safe" } }, { "Microsoft.GetHelp", new[] { "Obter Ajuda", "safe" } }, { "Microsoft.Getstarted", new[] { "Dicas", "safe" } },
            { "Microsoft.MicrosoftSolitaireCollection", new[] { "Solitaire Collection", "safe" } }, { "Microsoft.MicrosoftOfficeHub", new[] { "Microsoft 365 (Office Hub)", "safe" } },
            { "Microsoft.People", new[] { "Pessoas", "safe" } }, { "Microsoft.WindowsFeedbackHub", new[] { "Hub de Comentários", "safe" } }, { "Microsoft.ZuneVideo", new[] { "Filmes e TV", "safe" } },
            { "Microsoft.ZuneMusic", new[] { "Groove / Media Player", "review" } }, { "Microsoft.YourPhone", new[] { "Vincular ao Celular", "safe" } }, { "MicrosoftWindows.CrossDevice", new[] { "Dispositivos Conectados (Celular)", "safe" } },
            { "Microsoft.549981C3F5F10", new[] { "Cortana (app)", "safe" } }, { "Clipchamp.Clipchamp", new[] { "Clipchamp", "safe" } }, { "MicrosoftTeams", new[] { "Teams (pessoal)", "safe" } },
            { "MSTeams", new[] { "Teams", "review" } }, { "Microsoft.Todos", new[] { "Microsoft To Do", "review" } }, { "Microsoft.PowerAutomateDesktop", new[] { "Power Automate", "safe" } },
            { "Microsoft.WindowsMaps", new[] { "Mapas", "safe" } }, { "Microsoft.MixedReality.Portal", new[] { "Portal de Realidade Misturada", "safe" } }, { "Microsoft.SkypeApp", new[] { "Skype", "safe" } },
            { "Microsoft.Copilot", new[] { "Copilot (app)", "safe" } }, { "Microsoft.Windows.Ai.Copilot.Provider", new[] { "Copilot (provedor do sistema)", "safe" } },
            { "MicrosoftWindows.Client.WebExperience", new[] { "Widgets (Notícias e Interesses)", "safe" } }, { "Microsoft.WidgetsPlatformRuntime", new[] { "Widgets (plataforma)", "safe" } },
            { "Microsoft.OutlookForWindows", new[] { "Novo Outlook", "review" } }, { "Microsoft.WindowsCommunicationsApps", new[] { "Email e Calendário", "review" } },
            { "Microsoft.MicrosoftJournal", new[] { "Journal", "safe" } }, { "Microsoft.Microsoft3DViewer", new[] { "Visualizador 3D", "safe" } }, { "Microsoft.Print3D", new[] { "Print 3D", "safe" } },
            { "Microsoft.MSPaint", new[] { "Paint 3D", "safe" } }, { "Microsoft.Wallet", new[] { "Carteira", "safe" } }, { "Microsoft.Messaging", new[] { "Mensagens", "safe" } },
            { "Microsoft.OneConnect", new[] { "Planos Móveis", "safe" } }, { "Microsoft.Office.OneNote", new[] { "OneNote (app da Store)", "review" } }, { "Microsoft.Office.Sway", new[] { "Sway", "safe" } },
            { "Microsoft.Whiteboard", new[] { "Whiteboard", "safe" } }, { "Microsoft.MicrosoftPowerBIForWindows", new[] { "Power BI", "safe" } }, { "Microsoft.NetworkSpeedTest", new[] { "Teste de Velocidade", "safe" } },
            { "Microsoft.RemoteDesktop", new[] { "Área de Trabalho Remota (app)", "review" } }, { "MicrosoftCorporationII.MicrosoftFamily", new[] { "Family Safety", "safe" } },
            { "Microsoft.WindowsSoundRecorder", new[] { "Gravador de Som", "review" } }, { "Microsoft.WindowsAlarms", new[] { "Alarmes e Relógio", "review" } },
            { "Microsoft.WindowsCamera", new[] { "Câmera", "review" } }, { "Microsoft.MicrosoftStickyNotes", new[] { "Notas Autoadesivas", "review" } },
            { "Microsoft.XboxApp", new[] { "Xbox Console Companion", "safe" } }, { "Microsoft.GamingApp", new[] { "Xbox (app)", "review" } },
            { "Microsoft.XboxGamingOverlay", new[] { "Xbox Game Bar", "review" } }, { "Microsoft.XboxGameOverlay", new[] { "Xbox Game Overlay", "review" } },
            { "Microsoft.XboxSpeechToTextOverlay", new[] { "Xbox: voz para texto", "safe" } },
            { "MicrosoftCorporationII.QuickAssist", new[] { "Assistência Rápida", "review" } }, { "Microsoft.Windows.DevHome", new[] { "Dev Home", "safe" } },
            { "7EE7776C.LinkedInforWindows", new[] { "LinkedIn", "safe" } }, { "SpotifyAB.SpotifyMusic", new[] { "Spotify (pré-instalado)", "review" } },
            { "Disney.37853FC22B2CE", new[] { "Disney+", "safe" } }, { "Facebook.Facebook", new[] { "Facebook", "safe" } }, { "Facebook.InstagramBeta", new[] { "Instagram", "safe" } },
            { "BytedancePte.Ltd.TikTok", new[] { "TikTok", "safe" } }, { "AmazonVideo.PrimeVideo", new[] { "Prime Video", "safe" } }, { "4DF9E0F8.Netflix", new[] { "Netflix", "review" } },
            { "AdobeSystemsIncorporated.AdobePhotoshopExpress", new[] { "Photoshop Express", "safe" } }, { "Flipboard.Flipboard", new[] { "Flipboard", "safe" } },
            { "9E2F88E3.Twitter", new[] { "X (Twitter)", "safe" } }, { "ShazamEntertainmentLtd.Shazam", new[] { "Shazam", "safe" } }, { "D5EA27B7.Duolingo-LearnLanguagesforFree", new[] { "Duolingo", "safe" } },
        };
        // jogos e apps promocionais que vêm com prefixo de editora variável
        static readonly string[] PromoParts = { "CandyCrush", "BubbleWitch", "MarchofEmpires", "HiddenCity", "Asphalt", "FarmVille", "RoyalRevolt", "Sudoku", "Minecraft", "DisneyMagicKingdoms", "COOKINGFEVER", "PandoraMediaInc", "Plex", "Wunderlist", "EclipseManager", "ActiproSoftware" };

        static string[] Info(string name)
        {
            string[] v;
            if (Catalog.TryGetValue(name, out v)) return v;
            var part = PromoParts.FirstOrDefault(p => name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
            return part != null ? new[] { name.Contains(".") ? name.Substring(name.IndexOf('.') + 1) : name, "safe" } : null;
        }

        static string Ps(string script, int timeout = 600000) =>
            Power.Run(Power.Sys(@"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + script.Replace("\"", "\\\"") + "\"", timeout);

        static object[] Json(string s)
        {
            try
            {
                s = s.Trim(); if (s == "") return new object[0];
                var parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(s);
                return parsed as object[] ?? (parsed is Dictionary<string, object> one ? new object[] { one } : new object[0]);
            }
            catch { return new object[0]; }
        }

        static string Safe(string s) => new string((s ?? "").Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == '~' || c == '+').ToArray());

        // ------------------------------------------------------------------ apps
        public static object Apps()
        {
            if (Environment.OSVersion.Version.Major < 10) return new List<object>();
            var installed = Json(Ps("Get-AppxPackage" + (Program.IsAdmin ? " -AllUsers" : "") + " | Where-Object { -not $_.IsFramework -and $_.SignatureKind -eq 'Store' } | Select-Object -Unique Name, Version | ConvertTo-Json -Compress", 120000));
            var prov = Program.IsAdmin ? Json(Ps("Get-AppxProvisionedPackage -Online | Select-Object DisplayName | ConvertTo-Json -Compress", 120000)) : new object[0];
            var provNames = new HashSet<string>(prov.OfType<Dictionary<string, object>>().Select(d => Convert.ToString(d["DisplayName"])), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<Dictionary<string, object>>();
            foreach (var a in installed.OfType<Dictionary<string, object>>())
            {
                string name = Convert.ToString(a["Name"]); var info = Info(name);
                if (info == null || !seen.Add(name)) continue;
                list.Add(new Dictionary<string, object> { { "name", name }, { "title", info[0] }, { "risk", info[1] }, { "version", Convert.ToString(a["Version"]) }, { "installed", true }, { "provisioned", provNames.Contains(name) } });
            }
            foreach (var name in provNames)
            {
                var info = Info(name);
                if (info == null || !seen.Add(name)) continue; // só provisionado: volta em conta nova ou após grande atualização
                list.Add(new Dictionary<string, object> { { "name", name }, { "title", info[0] }, { "risk", info[1] }, { "version", "" }, { "installed", false }, { "provisioned", true } });
            }
            return list.OrderBy(d => (string)d["risk"] == "safe" ? 0 : 1).ThenBy(d => (string)d["title"]).Cast<object>().ToList();
        }

        public static object RemoveApps(IEnumerable<string> names, bool provisioned)
        {
            var done = new List<string>(); var failed = new List<string>();
            foreach (var raw in names)
            {
                if (Info(raw) == null) { failed.Add(raw); continue; }
                string n = Safe(raw);
                string all = Program.IsAdmin ? " -AllUsers" : "";
                string script = "$ErrorActionPreference='SilentlyContinue'; Get-AppxPackage" + all + " -Name '" + n + "' | Remove-AppxPackage" + all + ";";
                if (provisioned && Program.IsAdmin) script += " Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq '" + n + "' | Remove-AppxProvisionedPackage -Online -AllUsers | Out-Null;";
                script += " $left = @(Get-AppxPackage -Name '" + n + "').Count;" + (provisioned && Program.IsAdmin ? " $left += @(Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq '" + n + "').Count;" : "") + " 'LEFT=' + $left";
                var o = Ps(script);
                if (o.Contains("LEFT=0")) done.Add(raw); else failed.Add(raw);
            }
            return new Dictionary<string, object> { { "done", done }, { "failed", failed } };
        }

        // ------------------------------------------------------------------ Edge
        static string Pf86 => Environment.GetFolderPath(Environment.Is64BitOperatingSystem ? Environment.SpecialFolder.ProgramFilesX86 : Environment.SpecialFolder.ProgramFiles);
        static string EdgeExe => Path.Combine(Pf86, @"Microsoft\Edge\Application\msedge.exe");
        static string OneDriveExe()
        {
            foreach (var p in new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\OneDrive\OneDrive.exe"),
                Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft OneDrive\OneDrive.exe"),
                Path.Combine(Pf86, @"Microsoft OneDrive\OneDrive.exe") })
                if (File.Exists(p)) return p;
            return null;
        }

        public static object Status()
        {
            string ev = null;
            try { if (File.Exists(EdgeExe)) ev = FileVersionInfo.GetVersionInfo(EdgeExe).ProductVersion; } catch { }
            string od = OneDriveExe();
            return new Dictionary<string, object> {
                { "edge", ev != null }, { "edgeVersion", ev ?? "" },
                { "webview", Directory.Exists(Path.Combine(Pf86, @"Microsoft\EdgeWebView\Application")) },
                { "onedrive", od != null }, { "onedriveFolder", Environment.GetEnvironmentVariable("OneDrive") ?? "" },
                { "admin", Program.IsAdmin } };
        }

        static void Kill(params string[] names)
        {
            foreach (var n in names) foreach (var p in Process.GetProcessesByName(n)) using (p) try { p.Kill(); p.WaitForExit(3000); } catch { }
        }

        static RegistryKey Lm(bool wow = false) => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, wow || !Environment.Is64BitOperatingSystem ? RegistryView.Registry32 : RegistryView.Registry64);

        public static object RemoveEdge()
        {
            if (!Program.IsAdmin) throw new InvalidOperationException("Abra o GROBS como administrador para remover o Edge.");
            if (!File.Exists(EdgeExe)) return Status();
            Kill("msedge", "MicrosoftEdgeUpdate", "widgets");
            // o instalador do Edge só aceita desinstalar com esta liberação (fora do Espaço Econômico Europeu)
            using (var k = Lm(true).CreateSubKey(@"SOFTWARE\Microsoft\EdgeUpdateDev")) k.SetValue("AllowUninstall", 1, RegistryValueKind.DWord);
            var setups = Directory.GetFiles(Path.GetDirectoryName(EdgeExe), "setup.exe", SearchOption.AllDirectories);
            foreach (var s in setups)
                try
                {
                    using (var p = Process.Start(new ProcessStartInfo(s, "--uninstall --system-level --force-uninstall --msedge --channel=stable --verbose-logging") { UseShellExecute = false, CreateNoWindow = true }))
                        p?.WaitForExit(180000);
                }
                catch { }
            // impede a reinstalação automática pelo Windows Update/EdgeUpdate (WebView2 continua atualizando)
            using (var k = Lm().CreateSubKey(@"SOFTWARE\Policies\Microsoft\EdgeUpdate"))
            {
                k.SetValue("Install{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}", 0, RegistryValueKind.DWord);
                k.SetValue("DoNotUpdateToEdgeWithChromium", 1, RegistryValueKind.DWord);
            }
            using (var k = Lm().CreateSubKey(@"SOFTWARE\Microsoft\EdgeUpdate")) k.SetValue("DoNotUpdateToEdgeWithChromium", 1, RegistryValueKind.DWord);
            foreach (var lnk in new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Microsoft Edge.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Microsoft Edge.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Microsoft Edge.lnk") })
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
            if (File.Exists(EdgeExe)) throw new InvalidOperationException("O instalador do Edge recusou a remoção nesta versão do Windows. Nada mais foi alterado além de bloquear a reinstalação automática.");
            return Status();
        }

        public static object RestoreEdgePolicy()
        {
            using (var k = Lm().OpenSubKey(@"SOFTWARE\Policies\Microsoft\EdgeUpdate", true)) { k?.DeleteValue("Install{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}", false); k?.DeleteValue("DoNotUpdateToEdgeWithChromium", false); }
            using (var k = Lm().OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate", true)) k?.DeleteValue("DoNotUpdateToEdgeWithChromium", false);
            try { Process.Start(new ProcessStartInfo("https://www.microsoft.com/edge/download") { UseShellExecute = true }); } catch { }
            return Status();
        }

        // ------------------------------------------------------------------ OneDrive
        public static object RemoveOneDrive()
        {
            Kill("OneDrive", "OneDriveStandaloneUpdater", "FileCoAuth");
            var setups = new List<string>();
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var s in new[] { Path.Combine(sys, @"System32\OneDriveSetup.exe"), Path.Combine(sys, @"SysWOW64\OneDriveSetup.exe"), Path.Combine(sys, @"Sysnative\OneDriveSetup.exe") }) if (File.Exists(s)) setups.Add(s);
            string od = OneDriveExe();
            if (od != null) try { setups.AddRange(Directory.GetFiles(Path.GetDirectoryName(od), "OneDriveSetup.exe", SearchOption.AllDirectories)); } catch { }
            bool machine = od != null && !od.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase);
            foreach (var s in setups.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (OneDriveExe() == null) break;
                try { using (var p = Process.Start(new ProcessStartInfo(s, "/uninstall" + (machine ? " /allusers" : "")) { UseShellExecute = false, CreateNoWindow = true })) p?.WaitForExit(180000); } catch { }
            }
            using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) run?.DeleteValue("OneDrive", false);
            // some do painel do Explorador
            foreach (var clsid in new[] { @"Software\Classes\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}", @"Software\Classes\WOW6432Node\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}" })
                using (var k = Registry.CurrentUser.CreateSubKey(clsid)) k.SetValue("System.IsPinnedToNameSpaceTree", 0, RegistryValueKind.DWord);
            if (Program.IsAdmin)
            {
                using (var k = Lm().CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\OneDrive")) k.SetValue("DisableFileSyncNGSC", 1, RegistryValueKind.DWord);
                // contas novas não recebem o instalador do OneDrive
                string def = Path.Combine(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), @"Default\NTUSER.DAT");
                if (File.Exists(def))
                {
                    var reg = Power.Sys("reg.exe");
                    Power.Run(reg, "load HKU\\GrobsDefault \"" + def + "\"", 20000);
                    Power.Run(reg, "delete \"HKU\\GrobsDefault\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v OneDriveSetup /f", 20000);
                    Power.Run(reg, "unload HKU\\GrobsDefault", 20000);
                }
            }
            Tools.RestartExplorer();
            if (OneDriveExe() != null) throw new InvalidOperationException("O desinstalador do OneDrive não terminou a remoção. Tente de novo depois de reiniciar o PC.");
            return Status();
        }

        // ------------------------------------------------------------------ recursos opcionais
        // id (prefixo de capability "cap:" ou nome de feature "feat:") -> título, descrição, risco
        static readonly List<string[]> Optional = new List<string[]>
        {
            new[] { "cap:Browser.InternetExplorer", "Internet Explorer 11", "Navegador antigo, sem suporte desde 2022.", "safe" },
            new[] { "feat:Internet-Explorer-Optional-amd64", "Internet Explorer 11 (recurso)", "Navegador antigo, sem suporte desde 2022.", "safe" },
            new[] { "cap:Microsoft.Windows.WordPad", "WordPad", "Editor de texto descontinuado pela Microsoft.", "safe" },
            new[] { "cap:MathRecognizer", "Painel de Entrada de Matemática", "Reconhece fórmulas escritas à mão com caneta.", "safe" },
            new[] { "cap:Media.WindowsMediaPlayer", "Windows Media Player antigo", "O player clássico (o Media Player novo é outro app).", "review" },
            new[] { "feat:WindowsMediaPlayer", "Windows Media Player antigo (recurso)", "O player clássico (o Media Player novo é outro app).", "review" },
            new[] { "cap:Microsoft.Windows.PowerShell.ISE", "PowerShell ISE", "Editor de scripts antigo (o PowerShell continua).", "safe" },
            new[] { "cap:App.StepsRecorder", "Gravador de Passos", "Ferramenta antiga para registrar cliques em problemas.", "safe" },
            new[] { "cap:Print.Fax.Scan", "Fax e Scanner do Windows", "Envio de fax pelo modem. Scanners continuam funcionando por outros apps.", "review" },
            new[] { "feat:FaxServicesClientPackage", "Serviços de Fax", "Envio de fax pelo modem.", "safe" },
            new[] { "cap:XPS.Viewer", "Visualizador de XPS", "Formato de documento da Microsoft praticamente sem uso.", "safe" },
            new[] { "feat:Printing-XPSServices-Features", "Impressora XPS", "Impressora virtual \"Microsoft XPS Document Writer\".", "safe" },
            new[] { "feat:WorkFolders-Client", "Pastas de Trabalho", "Sincronização com servidor de empresa.", "safe" },
            new[] { "feat:MicrosoftWindowsPowerShellV2Root", "PowerShell 2.0", "Versão de 2009, usada para burlar proteções. Remover aumenta a segurança.", "safe" },
            new[] { "feat:SMB1Protocol", "SMB 1.0 (compartilhamento antigo)", "Protocolo explorado pelo WannaCry. Só NAS/impressoras muito antigas precisam dele.", "safe" },
            new[] { "feat:Recall", "Recall", "Capturas contínuas da tela para a IA do Windows.", "safe" },
            new[] { "cap:App.Support.QuickAssist", "Assistência Rápida (antiga)", "Suporte remoto da Microsoft.", "review" },
            new[] { "cap:Hello.Face", "Windows Hello: reconhecimento facial", "Só remova se não usa o rosto para entrar no Windows.", "review" },
            new[] { "cap:Microsoft.Windows.MSPaint", "Paint clássico", "Paint do Windows 10 (remove o Paint).", "review" },
            new[] { "cap:Language.Speech", "Reconhecimento de fala", "Ditado e comandos de voz.", "review" },
            new[] { "cap:Language.TextToSpeech", "Leitura em voz alta", "Vozes usadas pelo Narrador e por leitores de tela.", "review" },
            new[] { "cap:Language.Handwriting", "Reconhecimento de escrita à mão", "Só usado com caneta.", "review" },
        };

        public static object Features()
        {
            if (!Program.IsAdmin) throw new InvalidOperationException("Abra o GROBS como administrador para ver os recursos opcionais.");
            var caps = Json(Ps("Get-WindowsCapability -Online | Where-Object State -eq 'Installed' | Select-Object Name | ConvertTo-Json -Compress", 300000))
                .OfType<Dictionary<string, object>>().Select(d => Convert.ToString(d["Name"])).ToList();
            var feats = new HashSet<string>(Json(Ps("Get-WindowsOptionalFeature -Online | Where-Object State -eq 'Enabled' | Select-Object FeatureName | ConvertTo-Json -Compress", 300000))
                .OfType<Dictionary<string, object>>().Select(d => Convert.ToString(d["FeatureName"])), StringComparer.OrdinalIgnoreCase);
            var list = new List<object>();
            foreach (var o in Optional)
            {
                if (o[0].StartsWith("cap:"))
                {
                    string pre = o[0].Substring(4);
                    foreach (var c in caps.Where(c => c.StartsWith(pre + "~", StringComparison.OrdinalIgnoreCase)))
                        list.Add(new Dictionary<string, object> { { "id", "cap:" + c }, { "title", o[1] + (pre.StartsWith("Language.") ? " (" + c.Split('~').Skip(3).FirstOrDefault() + ")" : "") }, { "desc", o[2] }, { "risk", o[3] } });
                }
                else if (feats.Contains(o[0].Substring(5)))
                    list.Add(new Dictionary<string, object> { { "id", o[0] }, { "title", o[1] }, { "desc", o[2] }, { "risk", o[3] } });
            }
            // removidos por aqui antes: aparecem para poder reinstalar
            var removed = Removed();
            foreach (var r in removed.Keys.ToList())
                if (list.OfType<Dictionary<string, object>>().Any(d => ((string)d["id"]).Equals(r, StringComparison.OrdinalIgnoreCase))) removed.Remove(r);
            foreach (var r in removed) list.Add(new Dictionary<string, object> { { "id", r.Key }, { "title", r.Value }, { "desc", "" }, { "risk", "safe" }, { "removed", true } });
            return list;
        }

        static Dictionary<string, string> Removed()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Settings.Get<object>("removedFeatures") is Dictionary<string, object> m) foreach (var kv in m) d[kv.Key] = Convert.ToString(kv.Value);
            return d;
        }
        static void SaveRemoved(Dictionary<string, string> d) => Settings.Set("removedFeatures", d.ToDictionary(kv => kv.Key, kv => (object)kv.Value));

        static bool Known(string id) =>
            id.StartsWith("feat:") ? Optional.Any(o => o[0].Equals(id, StringComparison.OrdinalIgnoreCase))
            : id.StartsWith("cap:") && Optional.Any(o => o[0].StartsWith("cap:") && id.StartsWith(o[0] + "~", StringComparison.OrdinalIgnoreCase));

        public static object RemoveFeature(string id, string title)
        {
            if (!Program.IsAdmin) throw new InvalidOperationException("Abra o GROBS como administrador.");
            if (!Known(id)) throw new InvalidOperationException("Recurso fora da lista permitida.");
            string n = Safe(id.Substring(id.IndexOf(':') + 1));
            string o = id.StartsWith("cap:")
                ? Ps("$r = Remove-WindowsCapability -Online -Name '" + n + "' -ErrorAction Stop; 'OK RESTART=' + $r.RestartNeeded")
                : Ps("$r = Disable-WindowsOptionalFeature -Online -FeatureName '" + n + "' -NoRestart -ErrorAction Stop" + (n == "MicrosoftWindowsPowerShellV2Root" ? "; Disable-WindowsOptionalFeature -Online -FeatureName 'MicrosoftWindowsPowerShellV2' -NoRestart -ErrorAction SilentlyContinue | Out-Null" : "") + "; 'OK RESTART=' + $r.RestartNeeded");
            if (!o.Contains("OK")) throw new InvalidOperationException("O Windows não removeu este recurso.");
            var rm = Removed(); rm[id] = string.IsNullOrEmpty(title) ? n : title; SaveRemoved(rm);
            return new Dictionary<string, object> { { "restart", o.Contains("RESTART=True") } };
        }

        public static object RestoreFeature(string id)
        {
            if (!Program.IsAdmin) throw new InvalidOperationException("Abra o GROBS como administrador.");
            if (!Known(id)) throw new InvalidOperationException("Recurso fora da lista permitida.");
            string n = Safe(id.Substring(id.IndexOf(':') + 1));
            string o = id.StartsWith("cap:")
                ? Ps("$r = Add-WindowsCapability -Online -Name '" + n + "' -ErrorAction Stop; 'OK RESTART=' + $r.RestartNeeded")
                : Ps("$r = Enable-WindowsOptionalFeature -Online -FeatureName '" + n + "' -All -NoRestart -ErrorAction Stop; 'OK RESTART=' + $r.RestartNeeded");
            if (!o.Contains("OK")) throw new InvalidOperationException("O Windows não reinstalou este recurso (precisa de internet e do Windows Update funcionando).");
            var rm = Removed(); rm.Remove(id); SaveRemoved(rm);
            return new Dictionary<string, object> { { "restart", o.Contains("RESTART=True") } };
        }
    }
}
