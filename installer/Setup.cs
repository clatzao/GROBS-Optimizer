using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GrobsSetup
{
    internal static class Program
    {
        public const string AppName = "GROBS Optimizer", Version = "1.0.0", UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\GROBSOptimizer";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Contains("--uninstall")) { Uninstaller.Run(args.Contains("--silent")); return; }
            if (args.Contains("--silent"))
            {
                // Instalação sem janela (scripts/implantação):
                // --silent [--dir "C:\Pasta"] [--modules monitor,drivers] [--x86] [--no-desktop] [--startup]
                Func<string, string> arg = n => { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; };
                string dir = arg("--dir") ?? Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
                var mods = (arg("--modules") ?? "monitor,repair,clean,optimize,update,security,gamer,overlay,oc,drivers,beautify,tools").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                try
                {
                    Installer.Install(dir, Environment.Is64BitOperatingSystem && !args.Contains("--x86"), mods, !args.Contains("--no-desktop"), args.Contains("--startup"), (p, t) => { });
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "GROBS-Setup-erro.txt"), ex.ToString()); } catch { }
                    Environment.ExitCode = 1;
                }
                return;
            }
            Application.Run(new SetupForm());
        }
    }

    // ------------------------------------------------------------------ módulos
    internal sealed class Module
    {
        public string Id, Title, Desc; public bool On = true;
        public Module(string id, string title, string desc) { Id = id; Title = title; Desc = desc; }
    }

    // ------------------------------------------------------------------ controles com visual do app
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(10, 11, 16), Surface = Color.FromArgb(19, 21, 30), Surface2 = Color.FromArgb(27, 30, 43),
            Line = Color.FromArgb(40, 44, 60), Text = Color.FromArgb(238, 240, 246), Text2 = Color.FromArgb(163, 169, 187), Text3 = Color.FromArgb(107, 114, 135),
            Violet = Color.FromArgb(139, 92, 246), Cyan = Color.FromArgb(34, 211, 238);
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
    }

    internal class GradButton : Control
    {
        public bool Primary = true; bool _hover, _down;
        public GradButton() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); Cursor = Cursors.Hand; Font = new Font("Segoe UI Semibold", 10f); BackColor = Theme.Bg; }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (_down) r.Inflate(-1, -1);
            using (var path = Theme.Round(r, 9))
            {
                if (Primary)
                    using (var b = new LinearGradientBrush(new PointF(0, 0), new PointF(Width, Height), Theme.Violet, Theme.Cyan))
                    { g.FillPath(b, path); if (_hover) using (var w = new SolidBrush(Color.FromArgb(28, 255, 255, 255))) g.FillPath(w, path); }
                else
                    using (var b = new SolidBrush(_hover ? Theme.Surface2 : Theme.Surface)) using (var p = new Pen(Theme.Line)) { g.FillPath(b, path); g.DrawPath(p, path); }
                if (!Enabled) using (var dim = new SolidBrush(Color.FromArgb(150, Theme.Bg))) g.FillPath(dim, path);
            }
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), Enabled ? Color.White : Theme.Text3, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Cartão marcável de módulo (o mesmo estilo dos cartões do app).</summary>
    internal sealed class ModuleCard : Control
    {
        readonly Module _m; bool _hover;
        public event EventHandler Changed;
        public ModuleCard(Module m) { _m = m; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); Size = new Size(316, 62); Cursor = Cursors.Hand; Margin = new Padding(0, 0, 10, 10); BackColor = Theme.Bg; }
        public void Refresh2() => Invalidate();
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
        protected override void OnClick(EventArgs e) { _m.On = !_m.On; Invalidate(); Changed?.Invoke(this, EventArgs.Empty); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 10))
            using (var b = new SolidBrush(_m.On ? Color.FromArgb(32, 139, 92, 246) : (_hover ? Theme.Surface2 : Theme.Surface)))
            using (var p = new Pen(_m.On ? Color.FromArgb(150, 139, 92, 246) : Theme.Line)) { g.FillPath(b, path); g.DrawPath(p, path); }
            var box = new RectangleF(14, Height / 2f - 10, 20, 20);
            using (var bp = Theme.Round(box, 5))
            {
                if (_m.On) using (var bb = new SolidBrush(Theme.Violet)) g.FillPath(bb, bp);
                else using (var pp = new Pen(Color.FromArgb(70, 78, 100), 1.5f)) g.DrawPath(pp, bp);
            }
            if (_m.On) using (var pen = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(box.X + 5, box.Y + 10.5f), new PointF(box.X + 8.5f, box.Y + 14), new PointF(box.X + 15, box.Y + 6.5f) });
            TextRenderer.DrawText(g, _m.Title, new Font("Segoe UI Semibold", 10f), new Point(46, 11), Theme.Text);
            TextRenderer.DrawText(g, _m.Desc, new Font("Segoe UI", 8.5f), new Rectangle(46, 32, Width - 54, 22), Theme.Text3, TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class DarkCheck : CheckBox
    {
        public DarkCheck(string text, bool on) { Text = text; Checked = on; ForeColor = Theme.Text2; AutoSize = true; Font = new Font("Segoe UI", 9.5f); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderColor = Theme.Line; FlatAppearance.CheckedBackColor = Theme.Violet; Cursor = Cursors.Hand; }
    }

    // ------------------------------------------------------------------ janela
    internal sealed class SetupForm : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);

        readonly List<Module> _mods = new List<Module>
        {
            new Module("monitor", "Monitor do sistema", "CPU, RAM, GPU, discos, sensores e processos"),
            new Module("repair", "Diagnóstico", "Acha erros (telas azuis, disco, drivers) e corrige"),
            new Module("clean", "Limpeza", "Temporários, caches e restos de atualizações"),
            new Module("optimize", "Otimização", "Inicialização, serviços, recursos e efeitos visuais"),
            new Module("update", "Windows Update", "Pausar, adiar, desativar e limpar cache"),
            new Module("security", "Segurança", "Central com o motor do Microsoft Defender"),
            new Module("gamer", "Gamer", "Otimização por jogo, perfis NVIDIA, ping e DNS"),
            new Module("overlay", "Overlay de FPS", "FPS e sensores por cima dos jogos"),
            new Module("oc", "Overclock da GPU", "Clock, energia e ventoinha (NVIDIA)"),
            new Module("drivers", "Atualizador de drivers", "Windows Update + NVIDIA, Intel e AMD"),
            new Module("beautify", "Embelezar o Windows", "Tema, cores, barra de tarefas, Explorer"),
            new Module("tools", "Ferramentas", "Desinstalador, analisador de disco, utilitários"),
        };
        readonly List<ModuleCard> _cards = new List<ModuleCard>();
        readonly Panel _page1 = new Panel(), _page2 = new Panel();
        readonly TextBox _path = new TextBox();
        readonly DarkCheck _desktop = new DarkCheck("Atalho na área de trabalho", true), _startup = new DarkCheck("Iniciar com o Windows (na bandeja)", false), _launch = new DarkCheck("Abrir o GROBS ao concluir", true);
        readonly ComboBox _arch = new ComboBox();
        readonly GradButton _install = new GradButton { Text = "Instalar" }, _cancel = new GradButton { Text = "Cancelar", Primary = false };
        readonly Label _status = new Label(), _count = new Label();
        float _progress;

        public SetupForm()
        {
            Text = "Instalar " + Program.AppName;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 590); DoubleBuffered = true;

            // página 1: módulos e opções
            _page1.SetBounds(0, 110, 720, 420);
            var lbl = new Label { Text = "Escolha o que instalar", ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 11f), AutoSize = true, Location = new Point(28, 4) };
            _count.SetBounds(220, 8, 200, 20); _count.ForeColor = Theme.Text3;
            var presets = new FlowLayoutPanel { Location = new Point(392, 0), Size = new Size(310, 30), FlowDirection = FlowDirection.LeftToRight, BackColor = Theme.Bg };
            foreach (var p in new[] { Tuple.Create("Tudo", "*"), Tuple.Create("Gamer", "monitor,repair,optimize,gamer,overlay,oc,drivers,clean"), Tuple.Create("Só monitor", "monitor,repair"), Tuple.Create("Só drivers", "drivers") })
            {
                var b = new GradButton { Text = p.Item1, Primary = false, Size = new Size(72, 26), Margin = new Padding(0, 0, 4, 0), Font = new Font("Segoe UI", 8.5f) };
                var ids = p.Item2;
                b.Click += (s, e) => { foreach (var m in _mods) m.On = ids == "*" || ids.Split(',').Contains(m.Id); foreach (var c in _cards) c.Refresh2(); UpdateCount(); };
                presets.Controls.Add(b);
            }
            var grid = new FlowLayoutPanel { Location = new Point(28, 38), Size = new Size(670, 290), BackColor = Theme.Bg, AutoScroll = true };
            foreach (var m in _mods) { var c = new ModuleCard(m); c.Changed += (s, e) => UpdateCount(); _cards.Add(c); grid.Controls.Add(c); }

            var pathLbl = new Label { Text = "Pasta:", ForeColor = Theme.Text2, AutoSize = true, Location = new Point(28, 344) };
            _path.SetBounds(76, 340, 430, 26); _path.BackColor = Theme.Surface; _path.ForeColor = Theme.Text; _path.BorderStyle = BorderStyle.FixedSingle;
            _path.Text = Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Program.AppName);
            var browse = new GradButton { Text = "Procurar…", Primary = false, Bounds = new Rectangle(512, 339, 90, 27), Font = new Font("Segoe UI", 9f) };
            browse.Click += (s, e) => { using (var d = new FolderBrowserDialog { SelectedPath = _path.Text }) if (d.ShowDialog(this) == DialogResult.OK) _path.Text = Path.Combine(d.SelectedPath, Program.AppName); };
            _arch.SetBounds(608, 340, 90, 26); _arch.DropDownStyle = ComboBoxStyle.DropDownList; _arch.FlatStyle = FlatStyle.Flat; _arch.BackColor = Theme.Surface; _arch.ForeColor = Theme.Text;
            _arch.Items.AddRange(new object[] { "64 bits", "32 bits" }); _arch.SelectedIndex = Environment.Is64BitOperatingSystem ? 0 : 1; _arch.Enabled = Environment.Is64BitOperatingSystem;
            _desktop.Location = new Point(28, 382); _startup.Location = new Point(250, 382); _launch.Location = new Point(510, 382);
            _page1.Controls.AddRange(new Control[] { lbl, _count, presets, grid, pathLbl, _path, browse, _arch, _desktop, _startup, _launch });

            // página 2: progresso
            _page2.SetBounds(0, 110, 720, 420); _page2.Visible = false;
            _status.SetBounds(28, 120, 664, 30); _status.ForeColor = Theme.Text2; _status.Font = new Font("Segoe UI", 10.5f); _status.TextAlign = ContentAlignment.MiddleCenter;
            _page2.Controls.Add(_status);
            _page2.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                var track = new RectangleF(60, 170, 600, 10);
                using (var p = Theme.Round(track, 5)) using (var b = new SolidBrush(Theme.Surface2)) g.FillPath(b, p);
                if (_progress > 0.01f)
                    using (var p = Theme.Round(new RectangleF(track.X, track.Y, Math.Max(10, track.Width * _progress), track.Height), 5))
                    using (var b = new LinearGradientBrush(track, Theme.Violet, Theme.Cyan, 0f)) g.FillPath(b, p);
            };

            _install.SetBounds(560, 540, 138, 36); _cancel.SetBounds(440, 540, 110, 36);
            _install.Click += async (s, e) => await DoInstall();
            _cancel.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { _page1, _page2, _install, _cancel });
            UpdateCount();
            if (Registry.LocalMachine.OpenSubKey(Program.UninstallKey) != null) _install.Text = "Atualizar";
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        }

        void UpdateCount()
        {
            int n = _mods.Count(m => m.On);
            _count.Text = n + " de " + _mods.Count + " módulos";
            _install.Enabled = n > 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, Width, 100), Color.FromArgb(46, 139, 92, 246), Color.FromArgb(10, 34, 211, 238), 0f)) g.FillRectangle(b, 0, 0, Width, 100);
            using (var ico = new Icon(Icon, 48, 48)) g.DrawIcon(ico, 28, 26);
            TextRenderer.DrawText(g, Program.AppName, new Font("Segoe UI Semibold", 18f), new Point(88, 22), Theme.Text);
            TextRenderer.DrawText(g, "Versão " + Program.Version + " · leve, sem anúncios e sem telemetria", new Font("Segoe UI", 9.5f), new Point(91, 58), Theme.Text2);
            using (var p = new Pen(Theme.Line)) { g.DrawLine(p, 0, 100, Width, 100); g.DrawLine(p, 0, 528, Width, 528); }
        }

        void SetProgress(float p, string text)
        {
            BeginInvoke((Action)(() => { _progress = p; _status.Text = text; _page2.Invalidate(); }));
        }

        async Task DoInstall()
        {
            string dir = _path.Text.Trim();
            bool x64 = _arch.SelectedIndex == 0 && Environment.Is64BitOperatingSystem;
            var modules = _mods.Where(m => m.On).Select(m => m.Id).ToArray();
            bool desktop = _desktop.Checked, startup = _startup.Checked, launch = _launch.Checked;
            _page1.Visible = false; _page2.Visible = true; _install.Enabled = false; _cancel.Enabled = false;
            try
            {
                await Task.Run(() => Installer.Install(dir, x64, modules, desktop, startup, SetProgress));
                SetProgress(1, "Pronto! O GROBS Optimizer foi instalado.");
                _install.Text = "Concluir"; _install.Enabled = true;
                _install.Click += (s, e) => { if (launch) try { Process.Start(new ProcessStartInfo(Path.Combine(dir, "GROBS.exe")) { UseShellExecute = true }); } catch { } Close(); };
            }
            catch (Exception ex)
            {
                SetProgress(0, "Falha: " + ex.Message);
                _cancel.Enabled = true; _cancel.Text = "Fechar";
            }
        }
    }

    // ------------------------------------------------------------------ instalação
    internal static class Installer
    {
        static RegistryKey Lm() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

        public static void Install(string dir, bool x64, string[] modules, bool desktop, bool startup, Action<float, string> progress)
        {
            progress(0.05f, "Fechando o GROBS, se estiver aberto…");
            foreach (var p in Process.GetProcessesByName("GROBS")) using (p) { try { p.CloseMainWindow(); if (!p.WaitForExit(3000)) p.Kill(); } catch { } }

            progress(0.15f, "Copiando arquivos…");
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, "GROBS.exe");
            using (var res = typeof(Installer).Assembly.GetManifestResourceStream(x64 ? "GROBS-x64.gz" : "GROBS-x86.gz"))
            using (var gz = new GZipStream(res, CompressionMode.Decompress))
            using (var f = File.Create(exe)) gz.CopyTo(f);

            progress(0.45f, "Salvando os módulos escolhidos…");
            File.WriteAllText(Path.Combine(dir, "modules.json"), "[" + string.Join(",", modules.Select(m => "\"" + m + "\"")) + "]");

            progress(0.55f, "Criando o desinstalador…");
            string uninst = Path.Combine(dir, "Desinstalar.exe");
            File.Copy(Application.ExecutablePath, uninst, true);

            progress(0.65f, "Criando atalhos…");
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "GROBS Optimizer.lnk");
            Shortcut(startMenu, exe, dir);
            string desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "GROBS Optimizer.lnk");
            if (desktop) Shortcut(desk, exe, dir); else if (File.Exists(desk)) File.Delete(desk);

            progress(0.8f, "Registrando no Windows…");
            using (var k = Lm().CreateSubKey(Program.UninstallKey))
            {
                k.SetValue("DisplayName", Program.AppName);
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("Publisher", "GROBS");
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + uninst + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninst + "\" --uninstall --silent");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            }
            // "Iniciar com o Windows" fica no HKCU do usuário que instalou (o app usa a mesma chave)
            using (var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (startup) run.SetValue("GROBS Optimizer", "\"" + exe + "\" --tray"); else run.DeleteValue("GROBS Optimizer", false);
            }
            progress(0.95f, "Finalizando…");
        }

        static void Shortcut(string lnk, string target, string workDir)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic s = shell.CreateShortcut(lnk);
            s.TargetPath = target; s.WorkingDirectory = workDir; s.IconLocation = target + ",0"; s.Description = "GROBS Optimizer";
            s.Save();
        }
    }

    internal static class Uninstaller
    {
        public static void Run(bool silent)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            if (!silent && MessageBox.Show("Remover o GROBS Optimizer deste computador?\n\nOs ajustes que você aplicou continuam como estão; reverta-os no app antes, se quiser.",
                "Desinstalar GROBS Optimizer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var p in Process.GetProcessesByName("GROBS")) using (p) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
            try { Process.Start(new ProcessStartInfo("schtasks.exe", "/Delete /TN \"GROBS\\Limpar cache do Windows Update\" /F") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(10000); } catch { }
            foreach (var lnk in new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "GROBS Optimizer.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "GROBS Optimizer.lnk") })
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
            using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) run?.DeleteValue("GROBS Optimizer", false);
            using (var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default))
                lm.DeleteSubKeyTree(Program.UninstallKey, false);
            try { File.Delete(Path.Combine(dir, "GROBS.exe")); File.Delete(Path.Combine(dir, "modules.json")); } catch { }
            // o próprio desinstalador e a pasta são apagados logo após este processo sair
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + Path.Combine(dir, "Desinstalar.exe") + "\" & rmdir \"" + dir + "\"")
                { CreateNoWindow = true, UseShellExecute = false });
            if (!silent) MessageBox.Show("GROBS Optimizer removido.", "Desinstalar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
