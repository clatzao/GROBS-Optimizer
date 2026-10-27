using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Grobs
{
    /// <summary>
    /// Overlay de FPS/métricas sobre jogos e programas: janela "layered" translúcida, sempre no topo,
    /// que não recebe cliques. Funciona em janela e tela cheia sem borda; tela cheia exclusiva antiga pode cobrir.
    /// Atalho para mostrar/ocultar: Ctrl+Shift+F10.
    /// </summary>
    internal sealed class Overlay : Form
    {
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize, IntPtr hdcSrc, ref Point pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        static Overlay _instance;
        readonly Timer _timer = new Timer { Interval = 500 };
        bool _hidden;
        int _lastPid;

        public static readonly string[] AllMetrics = { "fps", "low1", "frametime", "cpu", "cpuTemp", "gpu", "gpuTemp", "vram", "ram", "clock" };

        public static bool IsOn => _instance != null && !_instance.IsDisposed;

        public static void Apply()
        {
            var cfg = Config();
            bool want = Convert.ToBoolean(cfg["enabled"]);
            Program.Window?.BeginInvoke((Action)(() =>
            {
                if (want && !IsOn) { _instance = new Overlay(); _instance.Show(); }
                else if (!want && IsOn) { _instance.Close(); _instance = null; }
                else if (IsOn) _instance.Render();
            }));
        }

        public static Dictionary<string, object> Config()
        {
            var d = Settings.Get<object>("overlay") as Dictionary<string, object> ?? new Dictionary<string, object>();
            if (!d.ContainsKey("enabled")) d["enabled"] = false;
            if (!d.ContainsKey("position")) d["position"] = "tl";
            if (!d.ContainsKey("scale")) d["scale"] = 1.0;
            if (!d.ContainsKey("opacity")) d["opacity"] = 0.78;
            if (!d.ContainsKey("metrics")) d["metrics"] = new object[] { "fps", "low1", "frametime", "cpu", "gpu", "cpuTemp", "gpuTemp", "ram" };
            return d;
        }

        Overlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            _timer.Tick += (s, e) => Render();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // LAYERED | TRANSPARENT (clique atravessa) | TOOLWINDOW (fora do Alt+Tab) | NOACTIVATE | TOPMOST
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000 | 0x8;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RegisterHotKey(Handle, 1, 0x2 | 0x4 | 0x4000, 0x79); // Ctrl+Shift+F10 (MOD_NOREPEAT)
            if (Program.IsAdmin) FpsCounter.Start();
            _timer.Start();
            Render();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) { _hidden = !_hidden; Render(); return; } // WM_HOTKEY
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            UnregisterHotKey(Handle, 1);
            if (!FpsLog.Recording) FpsCounter.Stop();
            base.OnFormClosed(e);
        }

        static string Num(object v, string fmt = "0") => v == null ? "—" : Convert.ToDouble(v) < 0 ? "—" : Convert.ToDouble(v).ToString(fmt);

        void Render()
        {
            var cfg = Config();
            var metrics = ((IEnumerable)cfg["metrics"]).Cast<object>().Select(x => x.ToString()).ToList();
            float scale = (float)Convert.ToDouble(cfg["scale"]);
            int alpha = (int)(255 * Math.Max(0.2, Math.Min(1, Convert.ToDouble(cfg["opacity"]))));
            var stats = Program.Window?.LatestStats() ?? new Dictionary<string, object>();

            int pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            var fps = FpsCounter.Get(pid);
            if (fps == null && FpsCounter.Running)
            {
                // navegadores/Electron/launchers desenham num subprocesso de GPU: procura entre os filhos da janela em foco
                var parents = Processes.RawParents();
                foreach (var ap in FpsCounter.ActivePids())
                {
                    int cur = ap;
                    for (int depth = 0; depth < 3 && cur > 4; depth++)
                    {
                        int parent; if (!parents.TryGetValue(cur, out parent)) break;
                        if (parent == pid) { fps = FpsCounter.Get(ap); break; }
                        cur = parent;
                    }
                    if (fps != null) break;
                }
            }
            if (fps == null && _lastPid != 0) fps = FpsCounter.Get(_lastPid); // mantém o último jogo ao alternar rapidamente
            else if (fps != null) _lastPid = pid;

            Func<string, object> s = k => stats.ContainsKey(k) ? stats[k] : null;
            bool fahr = Settings.Get<string>("tempUnit") == "F";
            Func<object, string> tmp = v => v == null || Convert.ToDouble(v) < 0 ? "—" : (fahr ? Convert.ToDouble(v) * 9 / 5 + 32 : Convert.ToDouble(v)).ToString("0") + (fahr ? "°F" : "°C");
            var rows = new List<Tuple<string, string, Color>>();
            Color fpsColor = Color.FromArgb(52, 211, 153);
            foreach (var m in metrics)
            {
                switch (m)
                {
                    case "fps":
                        string f = fps != null ? fps["fps"].ToString() : (FpsCounter.Running ? "—" : (Program.IsAdmin ? "…" : "admin"));
                        if (fps != null) { int v = Convert.ToInt32(fps["fps"]); fpsColor = v >= 60 ? Color.FromArgb(52, 211, 153) : v >= 30 ? Color.FromArgb(251, 191, 36) : Color.FromArgb(248, 113, 113); }
                        rows.Add(Tuple.Create("FPS", f, fpsColor)); break;
                    case "low1": rows.Add(Tuple.Create("1% LOW", fps != null ? fps["low1"].ToString() : "—", Color.White)); break;
                    case "frametime": rows.Add(Tuple.Create("FRAME", fps != null ? Convert.ToDouble(fps["frametime"]).ToString("0.0") + " ms" : "—", Color.White)); break;
                    case "cpu": rows.Add(Tuple.Create("CPU", Num(s("cpu")) + "%", Color.FromArgb(196, 181, 253))); break;
                    case "cpuTemp": rows.Add(Tuple.Create("CPU °", tmp(s("cpuTemp")), Color.FromArgb(253, 164, 175))); break;
                    case "gpu": rows.Add(Tuple.Create("GPU", Num(s("gpu")) + "%", Color.FromArgb(103, 232, 249))); break;
                    case "gpuTemp": rows.Add(Tuple.Create("GPU °", tmp(s("gpuTemp")), Color.FromArgb(253, 164, 175))); break;
                    case "vram": rows.Add(Tuple.Create("VRAM", s("gpuMem") != null ? (Convert.ToDouble(s("gpuMem")) / 1073741824).ToString("0.0") + " GB" : "—", Color.White)); break;
                    case "ram": rows.Add(Tuple.Create("RAM", s("memUsed") != null ? (Convert.ToDouble(s("memUsed")) / 1073741824).ToString("0.0") + " GB" : "—", Color.White)); break;
                    case "clock": rows.Add(Tuple.Create("HORA", DateTime.Now.ToString("HH:mm"), Color.White)); break;
                }
            }
            Draw(rows, scale, alpha, Convert.ToString(cfg["position"]));
        }

        void Draw(List<Tuple<string, string, Color>> rows, float scale, int alpha, string pos)
        {
            int pad = (int)(8 * scale), lineH = (int)(18 * scale), labelW = (int)(54 * scale), valueW = (int)(70 * scale);
            int w = pad * 2 + labelW + valueW, h = _hidden || rows.Count == 0 ? 1 : pad * 2 + lineH * rows.Count;
            if (_hidden) w = 1;
            var area = Screen.FromPoint(Cursor.Position).Bounds;
            int margin = (int)(14 * scale);
            int x = pos.EndsWith("r") ? area.Right - w - margin : area.Left + margin;
            int y = pos.StartsWith("b") ? area.Bottom - h - margin - 40 : area.Top + margin;

            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                if (!_hidden)
                    using (var g = Graphics.FromImage(bmp))
                    using (var lf = new Font("Segoe UI", 8.2f * scale, FontStyle.Bold, GraphicsUnit.Point))
                    using (var vf = new Font("Consolas", 10.5f * scale, FontStyle.Bold, GraphicsUnit.Point))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        using (var path = Rounded(new RectangleF(0, 0, w - 1, h - 1), 8 * scale))
                        using (var bg = new SolidBrush(Color.FromArgb(alpha, 10, 11, 16)))
                        using (var border = new Pen(Color.FromArgb(Math.Min(255, alpha), 139, 92, 246), 1))
                        { g.FillPath(bg, path); g.DrawPath(border, path); }
                        for (int i = 0; i < rows.Count; i++)
                        {
                            float ty = pad + i * lineH;
                            using (var lb = new SolidBrush(Color.FromArgb(220, 163, 169, 187))) g.DrawString(rows[i].Item1, lf, lb, pad, ty + 2 * scale);
                            using (var vb = new SolidBrush(rows[i].Item3)) g.DrawString(rows[i].Item2, vf, vb, pad + labelW, ty);
                        }
                    }
                Push(bmp, x, y);
            }
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // reafirma TOPMOST sem mover
        }

        static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
            return p;
        }

        void Push(Bitmap bmp, int x, int y)
        {
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mem, hbmp);
            try
            {
                var size = new Size(bmp.Width, bmp.Height); var src = new Point(0, 0); var dst = new Point(x, y);
                var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
            }
            finally { SelectObject(mem, old); DeleteObject(hbmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }
        }

        /// <summary>Estado para a interface: FPS do processo em primeiro plano e jogos detectados.</summary>
        public static object Status()
        {
            var list = new List<object>();
            foreach (var pid in FpsCounter.ActivePids())
            {
                string name = "";
                try { using (var p = System.Diagnostics.Process.GetProcessById(pid)) name = p.ProcessName; } catch { continue; }
                if (name.Equals("dwm", StringComparison.OrdinalIgnoreCase) || name.StartsWith("msedgewebview", StringComparison.OrdinalIgnoreCase) || name.StartsWith("GROBS", StringComparison.OrdinalIgnoreCase)) continue;
                var f = FpsCounter.Get(pid);
                if (f != null) { f["pid"] = pid; f["name"] = name; list.Add(f); }
            }
            return new Dictionary<string, object> { { "on", IsOn }, { "running", FpsCounter.Running }, { "error", FpsCounter.LastError }, { "apps", list }, { "config", Config() }, { "admin", Program.IsAdmin } };
        }
    }
}
