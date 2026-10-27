using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Grobs
{
    internal sealed class MainForm : Form
    {
        const string Host = "https://app.grobs/";
        const int Border = 5;

        readonly WebView2 _web = new WebView2();
        readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        readonly string _devUrl;
        readonly bool _startInTray;
        NotifyIcon _tray;
        Monitor _monitor;
        System.Threading.Timer _timer;
        bool _ready, _visibleUi = true, _exiting;
        CancellationTokenSource _cleanCts;
        Dictionary<string, object> _sysInfoCache;
        readonly Dictionary<string, Func<Dictionary<string, object>, object>> _rpc;

        public MainForm(string[] args)
        {
            int i = Array.IndexOf(args, "--dev");
            if (i >= 0) _devUrl = i + 1 < args.Length ? args[i + 1] : "http://localhost:5173";
            _startInTray = Array.IndexOf(args, "--tray") >= 0;

            Text = "GROBS Optimizer";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(10, 11, 16);
            StartPosition = FormStartPosition.CenterScreen;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(1240, wa.Width - 40), Math.Min(800, wa.Height - 40));
            MinimumSize = new Size(1000, 640);
            Padding = new Padding(Border);
            DoubleBuffered = true;

            _web.Dock = DockStyle.Fill;
            _web.DefaultBackgroundColor = BackColor;
            Controls.Add(_web);

            _rpc = BuildRpc();
            if (_startInTray) { WindowState = FormWindowState.Minimized; ShowInTaskbar = false; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000 | 0x00010000 | 0x00080000; // WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU (minimizar pela barra de tarefas)
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int round = 2; Native.DwmSetWindowAttribute(Handle, 33, ref round, 4); // cantos arredondados no Windows 11
            int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            if (_startInTray) { CreateTray(); Hide(); }
            ApplyHotkey();
            try { await InitWebView(); }
            catch (Exception ex)
            {
                Program.Log(ex);
                MessageBox.Show("Não foi possível iniciar o WebView2.\n\nInstale o \"Microsoft Edge WebView2 Runtime\" (gratuito, da Microsoft) e abra o GROBS novamente.\n\n" + ex.Message,
                    "GROBS Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { _monitor = new Monitor(); _monitor.Sample(); } catch (Exception ex) { Program.Log(ex); }
                if (Program.IsAdmin && Settings.Get<bool>("sensorsAuto")) Sensors.Start(); // temperatura real da CPU no painel
                Games.Watcher.Refresh(); // Turbo automático, Modo Jogo e gravação de FPS
                MemoryCleaner.StartAuto();
                TimerRes.Restore();
                Smart.StartAlerts();
                Updater.StartAuto();
                RestartTimer();
                if (Convert.ToBoolean(Overlay.Config()["enabled"])) Overlay.Apply();
            });
        }

        async Task InitWebView()
        {
            string args = "--disable-background-networking --disable-component-update --disable-sync --no-first-run --disable-extensions " +
                "--disable-features=msSmartScreenProtection,msWebOOUI,msPdfOOUI,Translate,AutofillServerCommunication,msEdgeShopping";
            string debugPort = Environment.GetEnvironmentVariable("GROBS_DEBUG_PORT");
            int dp = Array.IndexOf(Environment.GetCommandLineArgs(), "--debug-port");
            if (dp >= 0 && dp + 1 < Environment.GetCommandLineArgs().Length) debugPort = Environment.GetCommandLineArgs()[dp + 1];
            if (!string.IsNullOrEmpty(debugPort)) args += " --remote-debugging-port=" + int.Parse(debugPort);
            var loader = Program.LoaderDir();
            if (loader != null) CoreWebView2Environment.SetLoaderDllFolderPath(loader);
            var opts = new CoreWebView2EnvironmentOptions(args);
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.DataDir, "WebView2"), opts);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            var s = core.Settings;
            bool dev = _devUrl != null;
            s.AreDevToolsEnabled = dev;
            s.AreDefaultContextMenusEnabled = dev;
            s.AreBrowserAcceleratorKeysEnabled = dev;
            s.IsZoomControlEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsBuiltInErrorPageEnabled = false;
            // Interfaces mais novas: o runtime 109 (último do Windows 7) pode não ter todas.
            Try(() => s.IsPinchZoomEnabled = false);
            Try(() => s.IsSwipeNavigationEnabled = false);
            Try(() => s.IsPasswordAutosaveEnabled = false);
            Try(() => s.IsGeneralAutofillEnabled = false);

            core.WebMessageReceived += OnMessage;
            core.NewWindowRequested += (o, a) => { a.Handled = true; OpenUrl(a.Uri); };
            core.NavigationStarting += (o, a) =>
            {
                if (!a.Uri.StartsWith(Host) && !(dev && a.Uri.StartsWith(_devUrl))) { a.Cancel = true; OpenUrl(a.Uri); }
            };
            if (dev) core.Navigate(_devUrl);
            else
            {
                core.AddWebResourceRequestedFilter(Host + "*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += ServeResource;
                core.Navigate(Host + "index.html");
            }
        }

        static void Try(Action a) { try { a(); } catch { } }

        /// <summary>Atualização encontrada pela checagem automática: avisa a interface e, se o app estiver na bandeja, mostra um balão.</summary>
        public void UpdateAvailable(Dictionary<string, object> info)
        {
            Notify("update", info);
            if (!_visibleUi) Balloon("GROBS Optimizer " + info["version"] + " disponível", "Abra o GROBS para atualizar com um clique.", false);
        }

        /// <summary>Encerra de verdade (ignora "fechar para a bandeja") — usado pela atualização.</summary>
        public void ExitNow() { _exiting = true; Close(); }

        /// <summary>Notificação do Windows (balão da bandeja). Usa o ícone da bandeja se existir; senão cria um temporário.</summary>
        public void Balloon(string title, string text, bool warn)
        {
            if (IsDisposed) return;
            BeginInvoke((Action)(() =>
            {
                var icon = warn ? ToolTipIcon.Warning : ToolTipIcon.Info;
                if (_tray != null) { _tray.ShowBalloonTip(6000, title, text, icon); return; }
                var tmp = new NotifyIcon { Icon = Icon, Text = "GROBS Optimizer", Visible = true };
                tmp.BalloonTipClosed += (o, e) => tmp.Dispose();
                tmp.BalloonTipClicked += (o, e) => { ShowFromTray(); tmp.Dispose(); };
                tmp.ShowBalloonTip(6000, title, text, icon);
                var t = new System.Windows.Forms.Timer { Interval = 9000 };
                t.Tick += (o, e) => { t.Dispose(); tmp.Visible = false; tmp.Dispose(); };
                t.Start();
            }));
        }

        /// <summary>Usado pelo overlay: coleta mesmo com a janela principal oculta.</summary>
        public Dictionary<string, object> LatestStats() => _monitor?.Latest(900);

        static void OpenUrl(string url)
        {
            if (url.StartsWith("https://") || url.StartsWith("http://"))
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        static readonly Dictionary<string, string> Mime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" }, { ".js", "text/javascript; charset=utf-8" }, { ".css", "text/css; charset=utf-8" },
            { ".svg", "image/svg+xml" }, { ".png", "image/png" }, { ".woff2", "font/woff2" }, { ".json", "application/json" }, { ".ico", "image/x-icon" },
        };

        void ServeResource(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            string path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
            if (path.Length == 0) path = "index.html";
            var asm = Assembly.GetExecutingAssembly();
            var stream = asm.GetManifestResourceStream("ui/" + path) ?? asm.GetManifestResourceStream("ui/" + path.Replace('/', '\\'));
            if (stream == null) { e.Response = _web.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", ""); return; }
            string mime; Mime.TryGetValue(Path.GetExtension(path), out mime);
            e.Response = _web.CoreWebView2.Environment.CreateWebResourceResponse(stream, 200, "OK",
                "Content-Type: " + (mime ?? "application/octet-stream") + "\r\nCache-Control: no-cache");
        }

        // ------------------------------------------------------------------ RPC

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> msg;
            try { msg = _json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); } catch { return; }
            object idObj; msg.TryGetValue("id", out idObj);
            string method = Convert.ToString(msg.ContainsKey("m") ? msg["m"] : "");
            var p = msg.ContainsKey("p") ? msg["p"] as Dictionary<string, object> ?? new Dictionary<string, object>() : new Dictionary<string, object>();

            if (method.StartsWith("win."))
            {
                // comandos de janela precisam rodar já, na thread da interface
                object res = null; string err = null;
                try { res = WindowCommand(method, p); } catch (Exception ex) { err = ex.Message; }
                if (idObj != null) Reply(idObj, res, err);
                return;
            }
            Func<Dictionary<string, object>, object> fn;
            if (!_rpc.TryGetValue(method, out fn)) { Reply(idObj, null, "Método desconhecido: " + method); return; }
            Task.Run(() =>
            {
                object res = null; string err = null;
                try { res = fn(p); }
                catch (Exception ex) { err = (ex.InnerException ?? ex).Message; if (!(ex is UnauthorizedAccessException)) Program.Log(ex); }
                Reply(idObj, res, err);
            });
        }

        void Reply(object id, object result, string error)
        {
            Post(new Dictionary<string, object> { { "id", id }, { "ok", error == null }, { "r", result }, { "e", error } });
        }

        public void Notify(string ev, object data) => Post(new Dictionary<string, object> { { "ev", ev }, { "d", data } });

        void Post(object payload)
        {
            string json;
            try { json = _json.Serialize(payload); } catch (Exception ex) { Program.Log(ex); return; }
            if (IsDisposed) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    try { if (_web.CoreWebView2 != null) _web.CoreWebView2.PostWebMessageAsJson(json); } catch { }
                }));
            }
            catch { }
        }

        static string Str(Dictionary<string, object> p, string k) { object v; return p.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }
        static bool Bool(Dictionary<string, object> p, string k) { object v; return p.TryGetValue(k, out v) && v != null && Convert.ToBoolean(v); }
        static int Int(Dictionary<string, object> p, string k) { object v; return p.TryGetValue(k, out v) && v != null ? Convert.ToInt32(v) : 0; }
        static string[] Arr(Dictionary<string, object> p, string k)
        {
            object v; if (!p.TryGetValue(k, out v) || v == null) return new string[0];
            return ((IEnumerable)v).Cast<object>().Select(x => Convert.ToString(x)).ToArray();
        }

        Dictionary<string, Func<Dictionary<string, object>, object>> BuildRpc()
        {
            return new Dictionary<string, Func<Dictionary<string, object>, object>>
            {
                { "app.info", p => new Dictionary<string, object> {
                    { "version", Program.Version }, { "admin", Program.IsAdmin }, { "proc64", Environment.Is64BitProcess },
                    { "os64", Environment.Is64BitOperatingSystem }, { "settings", Settings.All() }, { "dev", _devUrl != null } } },
                { "app.elevate", p => { bool ok = Program.Elevate(); if (ok) BeginInvoke((Action)(() => { _exiting = true; Application.Exit(); })); return ok; } },
                { "app.setting", p => { Settings.Set(Str(p, "key"), p.ContainsKey("value") ? p["value"] : null); if (Str(p, "key") == "interval") RestartTimer(); if (Str(p, "key") == "killHotkey") BeginInvoke((Action)ApplyHotkey); return Settings.All(); } },
                { "app.openLog", p => { var f = Path.Combine(Program.DataDir, "erros.log"); if (File.Exists(f)) System.Diagnostics.Process.Start(f); return true; } },

                { "sys.info", p => _sysInfoCache ?? (_sysInfoCache = SysInfo.Get()) },
                { "sys.disks", p => SysInfo.Disks() },
                { "sys.battery", p => SysInfo.Battery() },
                { "sys.sample", p => _monitor?.Sample() },

                { "proc.list", p => Processes.List() },
                { "proc.kill", p => Processes.ForceKill(Int(p, "pid"), Bool(p, "tree")) },
                { "proc.hung", p => Processes.Hung() },
                { "proc.priority", p => Processes.SetPriority(Int(p, "pid"), Str(p, "level")) },

                { "clean.scan", p => { _cleanCts?.Cancel(); _cleanCts = new CancellationTokenSource(); return Cleaner.Scan(_cleanCts.Token); } },
                { "clean.run", p => { _cleanCts = new CancellationTokenSource(); return Cleaner.Clean(Arr(p, "ids"), _cleanCts.Token); } },
                { "clean.cancel", p => { _cleanCts?.Cancel(); return true; } },

                { "startup.list", p => Startup.List() },
                { "startup.set", p => Startup.SetEnabled(Str(p, "id"), Bool(p, "enabled")) },

                { "tweaks.list", p => Tweaks.List(Arr(p, "groups")) },
                { "appx.list", p => Appx.List() },
                { "appx.remove", p => Appx.Remove(Str(p, "name")) },
                { "accent.set", p => Accent.Set(Str(p, "hex")) },
                { "drv.status", p => Drivers.Status() },
                { "drv.search", p => Drivers.Search() },
                { "drv.install", p => Drivers.Install(Arr(p, "ids")) },
                { "drv.installed", p => Drivers.Installed() },
                { "drv.missing", p => Drivers.Missing() },
                { "games.list", p => Games.List() },
                { "games.scan", p => Games.DeepScan() },
                { "games.set", p => Games.Set(Str(p, "exe"), Str(p, "what"), Bool(p, "on")) },
                { "games.optimize", p => Games.OptimizeAll(Str(p, "exe")) },
                { "games.add", p => Games.AddCustom(Str(p, "exe")) },
                { "games.remove", p => Games.RemoveCustom(Str(p, "exe")) },
                { "games.nvRead", p => NvProfiles.Read(Str(p, "exe")) },
                { "games.nvApply", p => NvProfiles.Apply(Str(p, "exe"), p["choices"] as Dictionary<string, object> ?? new Dictionary<string, object>()) },
                { "net.info", p => NetBoost.Info() },
                { "net.ping", p => NetBoost.PingTest(Str(p, "host"), Int(p, "count")) },
                { "net.dnsBench", p => NetBoost.DnsBenchmark() },
                { "net.setDns", p => NetBoost.SetDns(Str(p, "primary"), Str(p, "secondary")) },
                { "net.nagle", p => NetBoost.SetNagle(Bool(p, "off")) },
                { "shell.pickExe", p => (string)Invoke((Func<string>)(() => { using (var d = new OpenFileDialog { Filter = "Jogos (*.exe)|*.exe", Title = "Escolha o executável do jogo" }) return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null; })) },
                { "gpu.check", p => GpuDrivers.Check() },
                { "sensors.tree", p => Sensors.Tree() },
                { "oc.status", p => GpuOc.Status() },
                { "oc.clocks", p => GpuOc.SetClocks(Int(p, "core"), Int(p, "mem")) },
                { "oc.power", p => GpuOc.SetPower(Int(p, "percent")) },
                { "oc.fan", p => GpuOc.SetFan(Int(p, "level")) },
                { "oc.reset", p => GpuOc.Reset() },
                { "sensors.pawnio", p => Sensors.InstallPawnIO() },
                { "sensors.pawnStatus", p => Sensors.PawnStatus() },
                { "gpu.download", p => GpuDrivers.DownloadAndRun(Str(p, "url")) },
                { "gpu.downloadStatus", p => GpuDrivers.DownloadStatus() },
                { "tweaks.set", p => Tweaks.Set(Str(p, "id"), Bool(p, "on")) },

                { "power.list", p => Power.List() },
                { "power.set", p => Power.SetActive(Str(p, "id")) },
                { "power.unlockUltimate", p => Power.UnlockUltimate() },
                { "power.hibernation", p => Power.HibernationOn() },
                { "power.setHibernation", p => Power.SetHibernation(Bool(p, "on")) },

                { "boost.status", p => Boost.Status() },
                { "boost.start", p => Boost.Start(Arr(p, "apps")) },
                { "boost.stop", p => Boost.Stop() },

                { "sec.status", p => Security.Status() },
                { "sec.scan", p => Security.StartScan(Str(p, "type"), Str(p, "path")) },
                { "sec.scanStatus", p => Security.ScanStatus() },
                { "sec.cancel", p => Security.CancelScan() },
                { "sec.update", p => Security.UpdateSignatures() },
                { "sec.threats", p => Security.Threats() },
                { "sec.audit", p => Security.Audit() },

                { "apps.list", p => Apps.List() },
                { "apps.icons", p => Apps.Icons() },
                { "apps.uninstall", p => Apps.Uninstall(Str(p, "id")) },
                { "apps.leftovers", p => Apps.Leftovers(Str(p, "name"), Str(p, "location")) },
                { "apps.deleteLeftover", p => Apps.DeleteLeftover(Str(p, "path")) },

                { "disk.start", p => DiskAnalyzer.Start(Str(p, "path")) },
                { "disk.status", p => DiskAnalyzer.Status() },
                { "disk.cancel", p => DiskAnalyzer.Cancel() },
                { "disk.children", p => DiskAnalyzer.Children(Str(p, "sub")) },

                { "tools.restorePoint", p => Tools.CreateRestorePoint("GROBS Optimizer") },
                { "tools.flushDns", p => Tools.FlushDns() },
                { "tools.restartExplorer", p => Tools.RestartExplorer() },
                { "tools.repair", p => Tools.RepairSystem(Str(p, "which")) },
                { "tools.ping", p => Tools.Ping(Str(p, "host")) },

                { "svc.list", p => Services.List() },
                { "svc.set", p => Services.Set(Str(p, "name"), Bool(p, "disable")) },

                { "vfx.list", p => VisualFx.List() },
                { "vfx.set", p => VisualFx.SetOne(Str(p, "id"), Bool(p, "on")) },
                { "vfx.preset", p => VisualFx.Preset(Str(p, "preset")) },

                { "wu.status", p => WinUpdate.Status() },
                { "wu.pause", p => WinUpdate.Pause(Int(p, "days")) },
                { "wu.defer", p => WinUpdate.Defer(Int(p, "feature"), Int(p, "quality")) },
                { "wu.disable", p => WinUpdate.Disable() },
                { "wu.enable", p => WinUpdate.Enable() },
                { "wu.clean", p => WinUpdate.CleanCache() },
                { "wu.schedule", p => WinUpdate.Schedule(Str(p, "freq")) },

                { "diag.scan", p => Diagnostics.Scan() },
                { "diag.fix", p => Diagnostics.Fix(Str(p, "fix")) },

                { "overlay.status", p => Overlay.Status() },
                { "overlay.config", p => { Settings.Set("overlay", p["config"]); Overlay.Apply(); return Overlay.Config(); } },

                { "shell.reveal", p => { var f = Str(p, "path"); if (File.Exists(f) || Directory.Exists(f)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + f + "\""); return true; } },
                { "shell.pickFolder", p => (string)Invoke((Func<string>)(() => { using (var d = new FolderBrowserDialog { Description = "Escolha uma pasta" }) return d.ShowDialog(this) == DialogResult.OK ? d.SelectedPath : null; })) },

                { "update.check", p => Updater.Check() },
                { "update.status", p => Updater.Status() },
                { "update.install", p => Updater.Install() },

                { "ram.status", p => MemoryCleaner.Status() },
                { "ram.clean", p => MemoryCleaner.Clean(Bool(p, "workingSets")) },
                { "ram.config", p => { Settings.Set("ramAuto", p["config"]); MemoryCleaner.StartAuto(); return MemoryCleaner.Status(); } },
                { "timer.status", p => TimerRes.Status() },
                { "timer.set", p => TimerRes.Set(Bool(p, "on")) },
                { "msi.list", p => Latency.MsiList() },
                { "msi.set", p => { RestorePoints.Ensure(); return Latency.MsiSet(Str(p, "id"), Bool(p, "on")); } },
                { "parking.status", p => Latency.ParkingStatus() },
                { "parking.set", p => { if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador"); RestorePoints.Ensure(); return Latency.ParkingSet(Bool(p, "off")); } },
                { "pagefile.status", p => Pagefile.Status() },
                { "pagefile.set", p => { RestorePoints.Ensure(); return Pagefile.Set(Bool(p, "auto"), Str(p, "drive"), Int(p, "initial"), Int(p, "max")); } },
                { "pagefile.compression", p => Pagefile.SetCompression(Bool(p, "on")) },
                { "nettune.status", p => NetTune.Status() },
                { "nettune.set", p => NetTune.Set(Str(p, "what"), Bool(p, "on")) },
                { "nettune.flush", p => NetTune.FlushAll() },

                { "smart.read", p => Smart.Read() },
                { "dbench.start", p => DiskBench.Start(Str(p, "drive"), Int(p, "size"), Int(p, "passes"), Bool(p, "quick")) },
                { "dbench.status", p => DiskBench.Status() },
                { "dbench.cancel", p => DiskBench.Cancel() },
                { "drives.status", p => DriveOptimizer.Status() },
                { "drives.optimize", p => DriveOptimizer.Optimize(Str(p, "drive")) },
                { "drives.trim", p => DriveOptimizer.SetTrim(Bool(p, "on")) },
                { "drives.indexing", p => DriveOptimizer.SetIndexing(Bool(p, "on")) },

                { "bench.start", p => Bench.Start(Bool(p, "cpu"), Bool(p, "ram"), Bool(p, "disk")) },
                { "bench.status", p => Bench.Status() },

                { "gamemode.status", p => GameMode.Status() },
                { "gamemode.config", p => GameMode.SetConfig(p["config"] as Dictionary<string, object> ?? new Dictionary<string, object>()) },
                { "fpslog.status", p => FpsLog.Status() },
                { "fpslog.enable", p => FpsLog.SetEnabled(Bool(p, "on")) },
                { "fpslog.analyze", p => FpsLog.Analyze(Int(p, "seconds")) },
                { "fpslog.stop", p => FpsLog.Stop() },
                { "fpslog.list", p => FpsLog.List() },
                { "fpslog.get", p => FpsLog.Get(Str(p, "id")) },
                { "fpslog.delete", p => FpsLog.Delete(Str(p, "id")) },

                { "prio.list", p => AppPriority.List() },
                { "prio.set", p => AppPriority.Set(Str(p, "exe"), Str(p, "level")) },
                { "startup.delays", p => StartupDelay.List() },
                { "startup.delay", p => StartupDelay.Set(Str(p, "id"), Int(p, "seconds")) },
                { "rp.status", p => RestorePoints.Status() },
                { "rp.create", p => RestorePoints.Create(Str(p, "desc")) },
                { "rp.restore", p => RestorePoints.Restore(Convert.ToInt64(p["seq"])) },
                { "rp.enable", p => RestorePoints.EnableProtection() },
                { "profiles.status", p => Profiles.Status() },
                { "profiles.apply", p => Profiles.Apply(Str(p, "id")) },
                { "profiles.undo", p => Profiles.Undo() },
                { "cleansched.status", p => CleanSchedule.Status() },
                { "cleansched.set", p => CleanSchedule.Set(Str(p, "freq"), Str(p, "time"), Arr(p, "ids")) },
                { "battery.report", p => BatteryReport.Open() },
                { "backup.export", p => (object)Invoke((Func<object>)(() => {
                    using (var d = new SaveFileDialog { Filter = "Backup do GROBS (*.grobs.json)|*.grobs.json", FileName = "GROBS-" + Environment.MachineName + "-" + DateTime.Now.ToString("yyyy-MM-dd") + ".grobs.json" })
                    { if (d.ShowDialog(this) != DialogResult.OK) return null; File.WriteAllText(d.FileName, Backup.Build()); return d.FileName; } })) },
                { "backup.import", p => {
                    string f = (string)Invoke((Func<string>)(() => { using (var d = new OpenFileDialog { Filter = "Backup do GROBS (*.grobs.json;*.json)|*.grobs.json;*.json" }) return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null; }));
                    return f == null ? null : Backup.Import(f); } },

                { "dup.folders", p => Duplicates.DefaultFolders() },
                { "dup.start", p => Duplicates.Start(Arr(p, "folders"), Int(p, "minKb")) },
                { "dup.status", p => Duplicates.Status() },
                { "dup.cancel", p => Duplicates.Cancel() },
                { "dup.delete", p => Duplicates.Delete(Arr(p, "paths")) },
                { "repair.start", p => Repair.Start(Str(p, "id")) },
                { "repair.status", p => Repair.Status() },
                { "winget.list", p => Winget.List() },
                { "winget.upgrade", p => Winget.Upgrade(Arr(p, "ids")) },
                { "winget.status", p => Winget.Status() },
            };
        }

        object WindowCommand(string m, Dictionary<string, object> p)
        {
            switch (m)
            {
                case "win.drag":
                    if (WindowState == FormWindowState.Maximized) return false;
                    Native.ReleaseCapture();
                    Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
                    return true;
                case "win.minimize": WindowState = FormWindowState.Minimized; return true;
                case "win.maximize": ToggleMaximize(); return WindowState == FormWindowState.Maximized;
                case "win.close": CloseOrTray(); return true;
                case "win.state": return WindowState == FormWindowState.Maximized;
            }
            return null;
        }

        void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized) { WindowState = FormWindowState.Normal; Padding = new Padding(Border); }
            else
            {
                MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
                Padding = new Padding(0);
                WindowState = FormWindowState.Maximized;
            }
            Notify("maximized", WindowState == FormWindowState.Maximized);
        }

        // ------------------------------------------------------------------ amostragem

        void RestartTimer()
        {
            int ms = Math.Max(500, Settings.Get<int>("interval"));
            if (_timer == null) _timer = new System.Threading.Timer(_ => Tick(), null, ms, ms);
            else _timer.Change(_visibleUi ? ms : Timeout.Infinite, ms);
        }

        int _busy;
        void Tick()
        {
            if (!_ready || !_visibleUi || _monitor == null) return;
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try { Notify("stats", _monitor.Latest(Math.Max(400, Settings.Get<int>("interval") - 150))); }
            catch (Exception ex) { Program.Log(ex); }
            finally { _busy = 0; }
        }

        /// <summary>Janela oculta/minimizada: para a amostragem e suspende o WebView2 (≈0% de CPU).</summary>
        async void SetUiVisible(bool visible)
        {
            if (_visibleUi == visible || _web.CoreWebView2 == null) return;
            _visibleUi = visible;
            try
            {
                if (!visible)
                {
                    _timer?.Change(Timeout.Infinite, Timeout.Infinite);
                    _web.Visible = false;
                    Try(() => _web.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low);
                    await _web.CoreWebView2.TrySuspendAsync();
                }
                else
                {
                    _web.Visible = true;
                    Try(() => _web.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal);
                    RestartTimer();
                    Notify("resume", null);
                }
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) SetUiVisible(false);
            else if (Visible) SetUiVisible(true);
            if (WindowState != FormWindowState.Maximized && Padding.All == 0) Padding = new Padding(Border);
        }

        // ------------------------------------------------------------------ bandeja

        void CreateTray()
        {
            if (_tray != null) return;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir GROBS", null, (s, e) => ShowFromTray());
            menu.Items.Add("Modo Turbo (liga/desliga)", null, (s, e) => Task.Run(() =>
            {
                var r = Boost.Active ? Boost.Stop() : Boost.Start(Settings.Get<object>("boostApps") is IEnumerable a ? a.Cast<object>().Select(x => x.ToString()).ToArray() : null);
                Notify("boost", r);
                BeginInvoke((Action)(() => _tray.ShowBalloonTip(2000, "GROBS", Boost.Active ? "Modo Turbo ativado" : "Modo Turbo desativado", ToolTipIcon.Info)));
            }));
            menu.Items.Add("Liberar memória RAM", null, (s, e) => Task.Run(() =>
            {
                string msg;
                try { var r = (Dictionary<string, object>)MemoryCleaner.Clean(false); msg = Math.Round(Convert.ToDouble(r["freed"]) / 1048576) + " MB liberados"; }
                catch (Exception ex) { msg = ex.Message; }
                Balloon("GROBS", msg, false);
            }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair", null, (s, e) => { _exiting = true; Close(); });
            _tray = new NotifyIcon { Icon = Icon, Text = "GROBS Optimizer", Visible = true, ContextMenuStrip = menu };
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
        }

        void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            SetUiVisible(true);
        }

        void CloseOrTray()
        {
            if (Settings.Get<bool>("trayOnClose")) { CreateTray(); Hide(); SetUiVisible(false); }
            else { _exiting = true; Close(); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing && Settings.Get<bool>("trayOnClose"))
            {
                e.Cancel = true; CreateTray(); Hide(); SetUiVisible(false); return;
            }
            if (Boost.Active) Boost.Stop();
            try { FpsLog.End(); GameMode.Exit(); } catch { }
            _timer?.Dispose();
            _monitor?.Dispose();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------ mensagens da janela

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

        /// <summary>Ctrl+Alt+F4 = finalizar à força o app em primeiro plano (opcional, em Configurações).</summary>
        void ApplyHotkey()
        {
            UnregisterHotKey(Handle, 7);
            if (Settings.Get<bool>("killHotkey")) RegisterHotKey(Handle, 7, 0x1 | 0x2 | 0x4000, 0x73);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 7) { System.Threading.Tasks.Task.Run(() => Processes.KillForeground()); return; }
            if (m.Msg == Program.ShowMessage && Program.ShowMessage != 0) { ShowFromTray(); return; }
            if (m.Msg == Native.WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                var pt = PointToClient(new Point((short)(m.LParam.ToInt32() & 0xFFFF), (short)((m.LParam.ToInt32() >> 16) & 0xFFFF)));
                int w = ClientSize.Width, h = ClientSize.Height, b = Border + 2;
                bool l = pt.X < b, r = pt.X >= w - b, t = pt.Y < b, bo = pt.Y >= h - b;
                int hit = t && l ? Native.HTTOPLEFT : t && r ? Native.HTTOPRIGHT : bo && l ? Native.HTBOTTOMLEFT : bo && r ? Native.HTBOTTOMRIGHT
                    : l ? Native.HTLEFT : r ? Native.HTRIGHT : t ? Native.HTTOP : bo ? Native.HTBOTTOM : 0;
                if (hit != 0) m.Result = (IntPtr)hit;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _ready = true;
        }
    }
}
