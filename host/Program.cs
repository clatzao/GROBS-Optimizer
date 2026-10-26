using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Grobs
{
    internal static class Program
    {
        public const string Version = "1.0.0";
        public static readonly bool IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GROBS");
        public static MainForm Window;
        public static int ShowMessage;
        static Mutex _mutex;

        [STAThread]
        static void Main(string[] args)
        {
            System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072; // TLS 1.2 (Windows 7)
            // Modo tarefa agendada: executa e sai, sem janela.
            int ti = Array.IndexOf(args, "--task");
            if (ti >= 0 && ti + 1 < args.Length)
            {
                Directory.CreateDirectory(DataDir);
                try { if (args[ti + 1] == "clean-wu") WinUpdate.CleanCache(); } catch (Exception ex) { Log(ex); }
                return;
            }
            ShowMessage = Native.RegisterWindowMessage("GROBS_OPTIMIZER_SHOW");
            bool created;
            _mutex = new Mutex(true, "GROBS_Optimizer_SingleInstance", out created);
            if (!created && Array.IndexOf(args, "--elevated") < 0)
            {
                Native.PostMessage(Native.HWND_BROADCAST, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                return;
            }
            Directory.CreateDirectory(DataDir);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;
            Application.ThreadException += (s, e) => Log(e.Exception);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Run(args);
            GC.KeepAlive(_mutex);
        }

        // Em método separado: os tipos do WebView2 só são carregados depois do AssemblyResolve estar registrado.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Run(string[] args)
        {
            Window = new MainForm(args);
            Application.Run(Window);
        }

        /// <summary>As DLLs gerenciadas do WebView2 ficam embutidas no exe (distribuição em arquivo único).</summary>
        static System.Reflection.Assembly ResolveEmbedded(object sender, ResolveEventArgs e)
        {
            string name = new System.Reflection.AssemblyName(e.Name).Name + ".dll";
            using (var s = typeof(Program).Assembly.GetManifestResourceStream("lib/" + name))
            {
                if (s == null) return null;
                var buf = new byte[s.Length];
                s.Read(buf, 0, buf.Length);
                return System.Reflection.Assembly.Load(buf);
            }
        }

        /// <summary>Extrai o WebView2Loader.dll nativo (por arquitetura e versão) para a pasta de dados.</summary>
        public static string LoaderDir()
        {
            using (var s = typeof(Program).Assembly.GetManifestResourceStream("lib/WebView2Loader.dll"))
            {
                if (s == null) return null; // build em pasta: a DLL já está ao lado do exe
                string dir = Path.Combine(DataDir, "bin", (Environment.Is64BitProcess ? "x64-" : "x86-") + Version + "-" + s.Length);
                string file = Path.Combine(dir, "WebView2Loader.dll");
                if (!File.Exists(file) || new FileInfo(file).Length != s.Length)
                {
                    Directory.CreateDirectory(dir);
                    using (var f = File.Create(file)) s.CopyTo(f);
                }
                return dir;
            }
        }

        public static void Log(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(DataDir, "erros.log"), DateTime.Now.ToString("s") + " " + ex + Environment.NewLine); } catch { }
        }

        /// <summary>Reabre o app como administrador (UAC). Retorna false se o usuário cancelar.</summary>
        public static bool Elevate()
        {
            try
            {
                _mutex.ReleaseMutex(); _mutex.Dispose();
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--elevated") { UseShellExecute = true, Verb = "runas" });
                return true;
            }
            catch
            {
                bool c; _mutex = new Mutex(true, "GROBS_Optimizer_SingleInstance", out c);
                return false;
            }
        }
    }

    /// <summary>Preferências em %LocalAppData%\GROBS\settings.json.</summary>
    internal static class Settings
    {
        static readonly string FilePath = Path.Combine(Program.DataDir, "settings.json");
        static Dictionary<string, object> _values;
        static readonly object _lock = new object();

        static readonly Dictionary<string, object> Defaults = new Dictionary<string, object>
        {
            { "animations", "full" },   // full | reduced | off
            { "interval", 1000 },
            { "trayOnClose", false },
            { "startWithWindows", false },
            { "tempUnit", "C" },
            { "boostApps", new object[0] },
            { "accent", "violet" },
            { "sensorsAuto", true },
            { "killHotkey", false },
        };

        public static Dictionary<string, object> All()
        {
            lock (_lock)
            {
                if (_values == null)
                {
                    _values = new Dictionary<string, object>(Defaults);
                    try
                    {
                        if (File.Exists(FilePath))
                            foreach (var kv in new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(FilePath)))
                                _values[kv.Key] = kv.Value;
                    }
                    catch { }
                    _values["startWithWindows"] = StartWithWindows;
                    // módulos escolhidos no instalador (sem o arquivo = versão portátil completa)
                    try
                    {
                        var mf = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modules.json");
                        if (File.Exists(mf)) _values["modules"] = new JavaScriptSerializer().Deserialize<object[]>(File.ReadAllText(mf));
                        else _values.Remove("modules");
                        // escolha feita depois em Configurações > Módulos tem prioridade sobre a do instalador
                        object user;
                        if (_values.TryGetValue("modulesUser", out user) && user != null) _values["modules"] = user;
                    }
                    catch { }
                }
                return new Dictionary<string, object>(_values);
            }
        }

        public static T Get<T>(string key)
        {
            var all = All();
            object v;
            if (!all.TryGetValue(key, out v) || v == null) return default(T);
            if (v is T) return (T)v; // objetos (ex.: configuração do overlay) não são IConvertible
            try { return (T)Convert.ChangeType(v, typeof(T)); } catch { return default(T); }
        }

        public static void Set(string key, object value)
        {
            All();
            lock (_lock)
            {
                if (key == "startWithWindows") StartWithWindows = Convert.ToBoolean(value);
                _values[key] = value;
                if (key == "modulesUser") _values["modules"] = value;
                try { File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(_values)); } catch { }
            }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static bool StartWithWindows
        {
            get { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue("GROBS Optimizer") != null; }
            set
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("GROBS Optimizer", "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue("GROBS Optimizer", false);
                }
            }
        }
    }
}
