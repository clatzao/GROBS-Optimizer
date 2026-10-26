using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>Apps pré-instalados (Store/UWP) que podem ser removidos e reinstalados depois pela Microsoft Store.</summary>
    internal static class Appx
    {
        static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.BingNews", "Notícias" }, { "Microsoft.BingWeather", "Clima" }, { "Microsoft.BingSearch", "Pesquisa Bing" },
            { "Microsoft.GetHelp", "Obter Ajuda" }, { "Microsoft.Getstarted", "Dicas" }, { "Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection" },
            { "Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office Hub)" }, { "Microsoft.People", "Pessoas" }, { "Microsoft.WindowsFeedbackHub", "Hub de Comentários" },
            { "Microsoft.ZuneVideo", "Filmes e TV" }, { "Microsoft.YourPhone", "Vincular ao Celular" }, { "Microsoft.549981C3F5F10", "Cortana (app)" },
            { "Clipchamp.Clipchamp", "Clipchamp" }, { "MicrosoftTeams", "Teams (pessoal)" }, { "MSTeams", "Teams" }, { "Microsoft.Todos", "Microsoft To Do" },
            { "Microsoft.PowerAutomateDesktop", "Power Automate" }, { "Microsoft.WindowsMaps", "Mapas" }, { "Microsoft.MixedReality.Portal", "Portal de Realidade Misturada" },
            { "Microsoft.SkypeApp", "Skype" }, { "Microsoft.Copilot", "Copilot (app)" }, { "Microsoft.OutlookForWindows", "Novo Outlook" },
            { "Microsoft.MicrosoftJournal", "Journal" }, { "Microsoft.Microsoft3DViewer", "Visualizador 3D" }, { "Microsoft.Print3D", "Print 3D" },
            { "Microsoft.MSPaint", "Paint 3D" }, { "Microsoft.Wallet", "Carteira" }, { "Microsoft.Messaging", "Mensagens" }, { "Microsoft.OneConnect", "Planos Móveis" },
            { "Microsoft.WindowsCommunicationsApps", "Email e Calendário" }, { "Microsoft.XboxApp", "Xbox Console Companion" },
            { "MicrosoftCorporationII.QuickAssist", "Assistência Rápida" }, { "Microsoft.Windows.DevHome", "Dev Home" },
            { "king.com.CandyCrushSaga", "Candy Crush Saga" }, { "king.com.CandyCrushSodaSaga", "Candy Crush Soda" }, { "SpotifyAB.SpotifyMusic", "Spotify (pré-instalado)" },
            { "Disney.37853FC22B2CE", "Disney+" }, { "Facebook.Facebook", "Facebook" }, { "BytedancePte.Ltd.TikTok", "TikTok" }, { "AmazonVideo.PrimeVideo", "Prime Video" },
        };

        static string Ps(string script, int timeout = 120000) =>
            Power.Run(Power.Sys(@"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -Command \"" + script.Replace("\"", "\\\"") + "\"", timeout);

        public static object List()
        {
            if (Environment.OSVersion.Version.Major < 10) return new List<object>();
            string json = Ps("[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-AppxPackage | Where-Object { -not $_.IsFramework -and $_.SignatureKind -eq 'Store' } | Select-Object Name, PackageFullName, Version | ConvertTo-Json -Compress");
            var list = new List<object>();
            try
            {
                var parsed = new JavaScriptSerializer().DeserializeObject(json.Trim());
                var arr = parsed as object[] ?? (parsed is Dictionary<string, object> one ? new object[] { one } : new object[0]);
                foreach (Dictionary<string, object> a in arr)
                {
                    string name = Convert.ToString(a["Name"]);
                    string nice; bool known = Known.TryGetValue(name, out nice);
                    if (!known && !name.StartsWith("king.com", StringComparison.OrdinalIgnoreCase)) continue; // só o que é reconhecidamente dispensável
                    list.Add(new Dictionary<string, object> { { "name", name }, { "title", nice ?? name }, { "full", a["PackageFullName"] }, { "version", Convert.ToString(a["Version"]) } });
                }
            }
            catch { }
            return list.Cast<Dictionary<string, object>>().OrderBy(d => (string)d["title"]).Cast<object>().ToList();
        }

        public static object Remove(string name)
        {
            if (!Known.ContainsKey(name) && !name.StartsWith("king.com", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("App fora da lista permitida");
            string safe = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-').ToArray());
            string o = Ps("Get-AppxPackage -Name '" + safe + "' | Remove-AppxPackage -ErrorAction Stop; 'ok'");
            if (!o.Contains("ok")) throw new InvalidOperationException("O Windows recusou remover este app.");
            return true;
        }
    }

    /// <summary>Cor de destaque do Windows (a mesma de Configurações > Personalização > Cores).</summary>
    internal static class Accent
    {
        public static object Set(string hex)
        {
            hex = hex.TrimStart('#');
            int r = Convert.ToInt32(hex.Substring(0, 2), 16), g = Convert.ToInt32(hex.Substring(2, 2), 16), b = Convert.ToInt32(hex.Substring(4, 2), 16);
            int abgr = unchecked((int)(0xFF000000u | (uint)(b << 16) | (uint)(g << 8) | (uint)r));
            int argb = unchecked((int)(0xC4000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b));
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
            {
                k.SetValue("AccentColorMenu", abgr, RegistryValueKind.DWord);
                k.SetValue("StartColorMenu", abgr, RegistryValueKind.DWord);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\DWM"))
            {
                k.SetValue("AccentColor", abgr, RegistryValueKind.DWord);
                k.SetValue("ColorizationColor", argb, RegistryValueKind.DWord);
                k.SetValue("ColorizationAfterglow", argb, RegistryValueKind.DWord);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop")) k.SetValue("AutoColorization", "0");
            Tools.RestartExplorer();
            return true;
        }
    }
}
