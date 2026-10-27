using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>
    /// Atualização pelo GitHub Releases: checa ao abrir e a cada 24 h; com um clique baixa o instalador
    /// (ou o exe portátil) e reinstala na mesma pasta, com os mesmos módulos.
    /// </summary>
    internal static class Updater
    {
        const string Api = "https://api.github.com/repos/clatzao/GROBS-Optimizer/releases/latest";
        static System.Threading.Timer _timer;
        static Dictionary<string, object> _latest;
        static volatile string _state = "idle"; // idle | checking | downloading | installing | error
        static volatile int _progress;
        static string _error;

        public static void StartAuto()
        {
            if (_timer != null) return;
            _timer = new System.Threading.Timer(_ =>
            {
                if (!Settings.Get<bool>("autoUpdate")) return;
                try
                {
                    var r = Check();
                    if (Convert.ToBoolean(r["available"])) Program.Window?.UpdateAvailable(r);
                }
                catch { /* sem internet: tenta de novo no próximo ciclo */ }
            }, null, 15000, 24 * 3600 * 1000);
        }

        static WebClient Client()
        {
            var w = new WebClient();
            w.Headers[HttpRequestHeader.UserAgent] = "GROBS-Optimizer/" + Program.Version;
            w.Encoding = System.Text.Encoding.UTF8;
            return w;
        }

        public static int Compare(string a, string b)
        {
            Func<string, int[]> parse = s => (s ?? "").TrimStart('v', 'V').Split('.', '-').Take(3).Select(x => { int n; return int.TryParse(x, out n) ? n : 0; }).Concat(new[] { 0, 0, 0 }).Take(3).ToArray();
            var x1 = parse(a); var y1 = parse(b);
            for (int i = 0; i < 3; i++) if (x1[i] != y1[i]) return x1[i].CompareTo(y1[i]);
            return 0;
        }

        /// <summary>Instalado pelo instalador (pasta com modules.json registrada no Painel de Controle) ou portátil.</summary>
        static bool Installed()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\GROBSOptimizer"))
            {
                string loc = Convert.ToString(k?.GetValue("InstallLocation") ?? "").TrimEnd('\\');
                return loc.Length > 0 && loc.Equals(dir, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(dir, "modules.json"));
            }
        }

        public static Dictionary<string, object> Check()
        {
            _state = "checking";
            try
            {
                string json;
                using (var w = Client()) json = w.DownloadString(Api);
                var j = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                var assets = ((j["assets"] as IEnumerable) ?? new object[0]).Cast<Dictionary<string, object>>()
                    .ToDictionary(a => Convert.ToString(a["name"]), a => a, StringComparer.OrdinalIgnoreCase);
                string tag = Convert.ToString(j["tag_name"]);
                _latest = new Dictionary<string, object> {
                    { "current", Program.Version }, { "version", tag.TrimStart('v', 'V') }, { "notes", Convert.ToString(j.ContainsKey("body") ? j["body"] : "") },
                    { "date", Convert.ToString(j.ContainsKey("published_at") ? j["published_at"] : "") }, { "url", Convert.ToString(j["html_url"]) },
                    { "available", Compare(tag, Program.Version) > 0 }, { "installed", Installed() } };
                string want = Installed() ? "GROBS-Optimizer-Setup.exe" : Environment.Is64BitProcess ? "GROBS-Optimizer-x64.exe" : "GROBS-Optimizer-x86.exe";
                Dictionary<string, object> asset;
                if (assets.TryGetValue(want, out asset)) { _latest["asset"] = asset["browser_download_url"]; _latest["size"] = Convert.ToDouble(asset["size"]); }
                else _latest["available"] = false;
                Settings.Set("updateChecked", DateTime.Now.ToString("s"));
                _state = "idle";
                return _latest;
            }
            catch (Exception ex) { _state = "idle"; throw new InvalidOperationException("Não foi possível verificar atualizações: " + ex.Message); }
        }

        public static object Status() => new Dictionary<string, object> { { "state", _state }, { "progress", _progress }, { "error", _error }, { "latest", _latest }, { "checked", Settings.Get<string>("updateChecked") } };

        /// <summary>Baixa e aplica em segundo plano. O app fecha sozinho e reabre na versão nova.</summary>
        public static object Install()
        {
            if (_state == "downloading" || _state == "installing") return Status();
            var info = _latest ?? Check();
            if (!Convert.ToBoolean(info["available"])) throw new InvalidOperationException("Você já está na versão mais recente.");
            _state = "downloading"; _progress = 0; _error = null;
            new Thread(() =>
            {
                try
                {
                    string url = Convert.ToString(info["asset"]);
                    string file = Path.Combine(Path.GetTempPath(), "GROBS-update-" + info["version"] + Path.GetExtension(url));
                    using (var w = Client())
                    {
                        w.DownloadProgressChanged += (s, e) => _progress = e.ProgressPercentage;
                        var done = new ManualResetEvent(false); Exception err = null;
                        w.DownloadFileCompleted += (s, e) => { err = e.Error; done.Set(); };
                        w.DownloadFileAsync(new Uri(url), file);
                        done.WaitOne();
                        if (err != null) throw err;
                    }
                    if (new FileInfo(file).Length != Convert.ToInt64(info["size"])) throw new InvalidOperationException("Download incompleto.");
                    _state = "installing";
                    Apply(file, Convert.ToBoolean(info["installed"]));
                }
                catch (Exception ex) { _error = ex.Message; _state = "error"; Program.Log(ex); }
            }) { IsBackground = true }.Start();
            return Status();
        }

        static void Apply(string file, bool installed)
        {
            string exe = Application.ExecutablePath, dir = Path.GetDirectoryName(exe);
            if (installed)
            {
                // mesmas escolhas da instalação atual: pasta, módulos, arquitetura, atalho e inicialização
                string mods = string.Join(",", new JavaScriptSerializer().Deserialize<string[]>(File.ReadAllText(Path.Combine(dir, "modules.json"))));
                string desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "GROBS Optimizer.lnk");
                bool startup;
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) startup = k?.GetValue("GROBS Optimizer") != null;
                string args = "--silent --launch --dir \"" + dir + "\" --modules " + mods + (Environment.Is64BitProcess ? "" : " --x86") + (File.Exists(desk) ? "" : " --no-desktop") + (startup ? " --startup" : "");
                Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true, Verb = "runas" }); // o instalador fecha este processo
            }
            else
            {
                // portátil: troca o exe depois que este processo sair
                string bat = Path.Combine(Path.GetTempPath(), "grobs-update.cmd");
                File.WriteAllText(bat, "@echo off\r\nfor /l %%i in (1,1,30) do (\r\n  move /y \"" + file + "\" \"" + exe + "\" >nul 2>&1 && goto ok\r\n  ping -n 2 127.0.0.1 >nul\r\n)\r\nexit /b 1\r\n:ok\r\nstart \"\" \"" + exe + "\"\r\ndel \"%~f0\"\r\n");
                Process.Start(new ProcessStartInfo(Power.Sys("cmd.exe"), "/c \"" + bat + "\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                Program.Window?.BeginInvoke((Action)(() => Program.Window.ExitNow()));
            }
        }
    }
}
