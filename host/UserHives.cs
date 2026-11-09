using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>
    /// Registro das outras contas do PC e do perfil padrão (que vira a base das contas novas). Contas sem sessão aberta
    /// têm o NTUSER.DAT carregado temporariamente (reg load) e descarregado logo depois.
    /// </summary>
    internal static class UserHives
    {
        public static void Each(Action<RegistryKey> apply)
        {
            if (!Program.IsAdmin) return;
            string me = WindowsIdentity.GetCurrent().User?.Value;
            var targets = new List<KeyValuePair<string, string>>(); // sid (ou "Default"), caminho do NTUSER.DAT
            using (var pl = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
                foreach (var sid in pl?.GetSubKeyNames() ?? new string[0])
                {
                    if (!sid.StartsWith("S-1-5-21-") || sid == me) continue;
                    using (var k = pl.OpenSubKey(sid))
                    {
                        string dir = Environment.ExpandEnvironmentVariables(Convert.ToString(k?.GetValue("ProfileImagePath") ?? ""));
                        if (dir != "") targets.Add(new KeyValuePair<string, string>(sid, Path.Combine(dir, "NTUSER.DAT")));
                    }
                }
            string def = Path.Combine(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), @"Default\NTUSER.DAT");
            targets.Add(new KeyValuePair<string, string>("Default", def));

            string reg = Power.Sys("reg.exe");
            int n = 0;
            foreach (var t in targets)
            {
                try
                {
                    using (var loaded = t.Key == "Default" ? null : Registry.Users.OpenSubKey(t.Key, true))
                        if (loaded != null) { apply(loaded); continue; } // conta com sessão aberta: já está carregada
                    if (!File.Exists(t.Value)) continue;
                    string mount = "GROBS_Hive" + (n++);
                    Power.Run(reg, "load HKU\\" + mount + " \"" + t.Value + "\"", 20000);
                    try { using (var k = Registry.Users.OpenSubKey(mount, true)) if (k != null) apply(k); }
                    finally { GC.Collect(); GC.WaitForPendingFinalizers(); Power.Run(reg, "unload HKU\\" + mount, 20000); }
                }
                catch (Exception ex) { Program.Log(ex); }
            }
        }
    }
}
