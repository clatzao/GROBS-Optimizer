using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Grobs
{
    internal static class Power
    {
        public static readonly Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static readonly Guid High = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static readonly Guid Ultimate = new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61");

        public static Guid Active()
        {
            IntPtr p;
            if (Native.PowerGetActiveScheme(IntPtr.Zero, out p) != 0) return Guid.Empty;
            var g = (Guid)Marshal.PtrToStructure(p, typeof(Guid));
            Native.LocalFree(p);
            return g;
        }

        static string Name(Guid g)
        {
            uint size = 0;
            Native.PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
            if (size == 0) return g.ToString();
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try { return Native.PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, buf, ref size) == 0 ? Marshal.PtrToStringUni(buf) : g.ToString(); }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public static object List()
        {
            var active = Active();
            var plans = new List<object>();
            IntPtr buf = Marshal.AllocHGlobal(16);
            try
            {
                for (uint i = 0; ; i++)
                {
                    uint size = 16;
                    if (Native.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16 /*ACCESS_SCHEME*/, i, buf, ref size) != 0) break;
                    var g = (Guid)Marshal.PtrToStructure(buf, typeof(Guid));
                    string kind = g == Balanced ? "balanced" : g == High ? "high" : g == Ultimate ? "ultimate" : "custom";
                    string n = Name(g);
                    if (kind == "custom" && n.IndexOf("ultim", StringComparison.OrdinalIgnoreCase) >= 0) kind = "ultimate";
                    if (kind == "custom" && n.IndexOf("econom", StringComparison.OrdinalIgnoreCase) >= 0) kind = "saver";
                    plans.Add(new Dictionary<string, object> { { "id", g.ToString() }, { "name", n }, { "kind", kind }, { "active", g == active } });
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return plans;
        }

        public static object SetActive(string id)
        {
            var g = new Guid(id);
            uint r = Native.PowerSetActiveScheme(IntPtr.Zero, ref g);
            if (r != 0) throw new InvalidOperationException("Não foi possível ativar o plano (" + r + ")");
            return true;
        }

        public static string Run(string exe, string args, int timeoutMs = 60000)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(timeoutMs);
                return o;
            }
        }

        public static string Sys(string exe)
        {
            // Em processo 32 bits, Sysnative evita o redirecionamento para SysWOW64.
            string dir = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative")
                : Environment.SystemDirectory;
            return Path.Combine(dir, exe);
        }

        /// <summary>Cria o plano "Desempenho Máximo" (oculto por padrão no Windows 10/11 Pro).</summary>
        public static object UnlockUltimate()
        {
            Run(Sys("powercfg.exe"), "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
            return List();
        }

        public static bool HibernationOn()
        {
            return File.Exists(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "hiberfil.sys"));
        }

        public static object SetHibernation(bool on)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            Run(Sys("powercfg.exe"), on ? "/hibernate on" : "/hibernate off");
            return HibernationOn();
        }
    }

    /// <summary>Modo Turbo: plano de alto desempenho + fecha apps escolhidos. Reverte ao desligar.</summary>
    internal static class Boost
    {
        static Guid _previous = Guid.Empty;
        public static bool Active { get; private set; }

        public static object Status() => new Dictionary<string, object> { { "active", Active } };

        public static object Start(string[] closeApps)
        {
            var before = Power.Active();
            var plans = (List<object>)Power.List();
            var ult = plans.Cast<Dictionary<string, object>>().FirstOrDefault(p => (string)p["kind"] == "ultimate");
            var high = plans.Cast<Dictionary<string, object>>().FirstOrDefault(p => (string)p["kind"] == "high");
            var pick = ult ?? high;
            if (!Active) _previous = before;
            if (pick != null) Power.SetActive((string)pick["id"]);
            int closed = closeApps != null ? Processes.KillByName(closeApps.Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 4) : n)) : 0;
            Active = true;
            return new Dictionary<string, object> { { "active", true }, { "plan", pick != null ? pick["name"] : "" }, { "closed", closed } };
        }

        public static object Stop()
        {
            if (Active && _previous != Guid.Empty) { try { Power.SetActive(_previous.ToString()); } catch { } }
            Active = false;
            return new Dictionary<string, object> { { "active", false } };
        }
    }
}
