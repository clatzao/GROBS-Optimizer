using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>Programas de inicialização. Ativa/desativa como o Gerenciador de Tarefas (chaves StartupApproved).</summary>
    internal static class Startup
    {
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        sealed class Source
        {
            public string Id, Label, Approved; public RegistryKey Hive; public string RunKey, Folder;
        }

        static IEnumerable<Source> Sources()
        {
            yield return new Source { Id = "hkcu", Label = "Registro (usuário)", Hive = Registry.CurrentUser, RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Approved = "Run" };
            yield return new Source { Id = "hklm", Label = "Registro (todos os usuários)", Hive = Registry.LocalMachine, RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Approved = "Run" };
            if (Environment.Is64BitOperatingSystem)
                yield return new Source { Id = "hklm32", Label = "Registro 32 bits", Hive = Registry.LocalMachine, RunKey = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", Approved = "Run32" };
            yield return new Source { Id = "fcu", Label = "Pasta Inicializar (usuário)", Hive = Registry.CurrentUser, Folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup), Approved = "StartupFolder" };
            yield return new Source { Id = "flm", Label = "Pasta Inicializar (todos)", Hive = Registry.LocalMachine, Folder = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Approved = "StartupFolder" };
        }

        static RegistryKey Open(RegistryKey hive, string path, bool write)
        {
            // Sempre a visão de 64 bits do registro, mesmo no executável x86.
            var root = RegistryKey.OpenBaseKey(hive == Registry.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
                Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
            return write ? root.CreateSubKey(path) : root.OpenSubKey(path);
        }

        public static string ExePath(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return "";
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith("\"")) { int e = cmd.IndexOf('"', 1); return e > 0 ? cmd.Substring(1, e - 1) : cmd.Trim('"'); }
            int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return i > 0 ? cmd.Substring(0, i + 4) : cmd.Split(' ')[0];
        }

        /// <summary>Destino de um atalho .lnk da pasta Inicializar (ou o próprio arquivo).</summary>
        static string ShortcutTarget(string file)
        {
            if (!file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return file;
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                string target = shell.CreateShortcut(file).TargetPath;
                return string.IsNullOrEmpty(target) ? file : target;
            }
            catch { return file; }
        }

        public static string Signer(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var cert = X509Certificate.CreateFromSignedFile(path);
                var subject = cert.Subject;
                var cn = subject.Split(',').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("CN="));
                return cn != null ? cn.Substring(3).Trim('"') : subject;
            }
            catch { return null; }
        }

        static bool IsEnabled(Source s, string name)
        {
            using (var k = Open(s.Hive, Approved + s.Approved, false))
            {
                var v = k?.GetValue(name) as byte[];
                return v == null || v.Length == 0 || (v[0] & 1) == 0;
            }
        }

        public static object List()
        {
            var items = new List<object>();
            foreach (var s in Sources())
            {
                var entries = new List<KeyValuePair<string, string>>();
                if (s.RunKey != null)
                {
                    using (var k = Open(s.Hive, s.RunKey, false))
                        if (k != null)
                            foreach (var n in k.GetValueNames())
                                if (!string.IsNullOrEmpty(n)) entries.Add(new KeyValuePair<string, string>(n, Convert.ToString(k.GetValue(n))));
                }
                else if (Directory.Exists(s.Folder))
                {
                    foreach (var f in Directory.GetFiles(s.Folder))
                        if (!Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                            entries.Add(new KeyValuePair<string, string>(Path.GetFileName(f), f));
                }
                foreach (var e in entries)
                {
                    string exe = s.Folder != null ? ShortcutTarget(e.Value) : ExePath(e.Value);
                    string company = "", desc = "";
                    try
                    {
                        if (File.Exists(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            var vi = FileVersionInfo.GetVersionInfo(exe);
                            company = vi.CompanyName ?? ""; desc = vi.FileDescription ?? "";
                        }
                    }
                    catch { }
                    items.Add(new Dictionary<string, object> {
                        { "id", s.Id + "|" + e.Key }, { "name", s.Folder != null ? Path.GetFileNameWithoutExtension(e.Key) : e.Key },
                        { "desc", desc }, { "command", e.Value }, { "path", exe }, { "company", company },
                        { "signer", Signer(exe) }, { "exists", File.Exists(exe) }, { "enabled", IsEnabled(s, e.Key) },
                        { "location", s.Label }, { "admin", s.Hive == Registry.LocalMachine },
                        { "suspicious", exe.IndexOf(@"\Temp\", StringComparison.OrdinalIgnoreCase) >= 0 } });
                }
            }
            return items;
        }

        public static object SetEnabled(string id, bool enabled)
        {
            var parts = id.Split(new[] { '|' }, 2);
            var s = Sources().First(x => x.Id == parts[0]);
            if (s.Hive == Registry.LocalMachine && !Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            var data = new byte[12];
            data[0] = enabled ? (byte)2 : (byte)3;
            if (!enabled) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(data, 4);
            using (var k = Open(s.Hive, Approved + s.Approved, true)) k.SetValue(parts[1], data, RegistryValueKind.Binary);
            return true;
        }
    }
}
