using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>
    /// Permissão "de fato" para itens que o Windows protege mesmo de administradores:
    /// 1) assume a posse de uma chave do registro (dono = Administradores, controle total) e tenta de novo;
    /// 2) em último caso gera um .bat com os comandos e o executa como SISTEMA por uma tarefa agendada temporária.
    /// Tudo fica registrado em %LocalAppData%\GROBS\run\ (o .bat e a saída), para conferir o que foi feito.
    /// </summary>
    internal static class PrivRun
    {
        static string Dir => Path.Combine(Program.DataDir, "run");

        // ------------------------------------------------------------------ posse de chaves do registro
        [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] struct TOKEN_PRIVILEGES { public int Count; public LUID Luid; public int Attr; }
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr p, int access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string sys, string name, out LUID luid);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES p, int len, IntPtr prev, IntPtr ret);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        static void Enable(string privilege)
        {
            IntPtr tok;
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x28, out tok)) return; // ADJUST_PRIVILEGES | QUERY
            try
            {
                LUID l;
                if (!LookupPrivilegeValue(null, privilege, out l)) return;
                var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = l, Attr = 2 };
                AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally { CloseHandle(tok); }
        }

        /// <summary>Torna os Administradores donos da chave (HKLM) com controle total. Devolve true se conseguiu.</summary>
        public static bool GrantKey(RegistryHive hive, string path)
        {
            if (!Program.IsAdmin) return false;
            try
            {
                Enable("SeTakeOwnershipPrivilege"); Enable("SeRestorePrivilege"); Enable("SeBackupPrivilege");
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                using (var root = RegistryKey.OpenBaseKey(hive, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default))
                {
                    using (var k = root.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.TakeOwnership))
                    {
                        if (k == null) return false;
                        var sec = k.GetAccessControl(AccessControlSections.None);
                        sec.SetOwner(admins);
                        k.SetAccessControl(sec);
                    }
                    using (var k = root.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadKey))
                    {
                        var sec = k.GetAccessControl();
                        sec.AddAccessRule(new RegistryAccessRule(admins, RegistryRights.FullControl, InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
                        k.SetAccessControl(sec);
                    }
                }
                Log("posse da chave " + hive + "\\" + path);
                return true;
            }
            catch (Exception ex) { Log("falhou posse de " + path + ": " + ex.Message); return false; }
        }

        /// <summary>Executa a ação; se o Windows negar acesso, assume a posse da chave e tenta de novo.</summary>
        public static void WithKey(RegistryHive hive, string path, Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                if (!GrantKey(hive, path)) throw;
                action();
            }
        }

        // ------------------------------------------------------------------ .bat como SISTEMA
        /// <summary>
        /// Roda os comandos num .bat como SISTEMA (tarefa agendada temporária) e devolve a saída.
        /// Serve para o que nem o administrador consegue: tarefas e serviços protegidos do Windows Update, por exemplo.
        /// </summary>
        public static string AsSystem(IEnumerable<string> commands, int timeoutMs = 120000)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Abra o GROBS como administrador.");
            Directory.CreateDirectory(Dir);
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string bat = Path.Combine(Dir, "grobs-" + id + ".bat"), log = Path.Combine(Dir, "grobs-" + id + ".log"), done = log + ".fim";
            var sb = new StringBuilder("@echo off\r\nchcp 65001 >nul\r\n");
            foreach (var c in commands) sb.Append("echo ^> ").Append(c.Replace("%", "%%").Replace("&", "^&").Replace("<", "^<").Replace(">", "^>").Replace("|", "^|")).Append("\r\n").Append(c).Append("\r\n");
            sb.Append("echo fim > \"").Append(done).Append("\"\r\n");
            File.WriteAllText(bat, sb.ToString(), new UTF8Encoding(false));
            string task = "GROBS\\Executor " + id;
            string sch = Power.Sys("schtasks.exe");
            Power.Run(sch, "/Create /F /RU SYSTEM /RL HIGHEST /SC ONCE /ST 00:00 /SD 01/01/2000 /TN \"" + task + "\" /TR \"cmd.exe /c \\\"\\\"" + bat + "\\\" > \\\"" + log + "\\\" 2>&1\\\"\"", 20000);
            Power.Run(sch, "/Run /TN \"" + task + "\"", 20000);
            var sw = Stopwatch.StartNew();
            while (!File.Exists(done) && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(300);
            Power.Run(sch, "/Delete /F /TN \"" + task + "\"", 20000);
            string output = "";
            try { output = File.Exists(log) ? File.ReadAllText(log) : ""; } catch { }
            try { File.Delete(done); } catch { }
            Log(".bat como SISTEMA: " + bat + (File.Exists(done) || output.Length > 0 ? "" : " (sem resposta)"));
            if (sw.ElapsedMilliseconds >= timeoutMs) throw new TimeoutException("Os comandos como SISTEMA não terminaram a tempo (veja " + log + ").");
            return output;
        }

        static void Log(string s) { try { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, "registro.txt"), DateTime.Now.ToString("s") + " " + s + Environment.NewLine); } catch { } }
    }
}
