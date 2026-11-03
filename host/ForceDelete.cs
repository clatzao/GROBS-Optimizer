using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Grobs
{
    /// <summary>
    /// Apagar à força (estilo Unlocker): assume a posse, dá permissão aos administradores, fecha quem está
    /// travando o arquivo e, em último caso, agenda a exclusão para a próxima reinicialização.
    /// Pastas essenciais do Windows são sempre recusadas.
    /// </summary>
    internal static class ForceDelete
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RM_UNIQUE_PROCESS { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
            public int ApplicationType; public uint AppStatus; public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmStartSession(out uint handle, int flags, string key);
        [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint handle);
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmRegisterResources(uint handle, uint nFiles, string[] files, uint nApps, IntPtr apps, uint nServices, string[] services);
        [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint handle, out uint needed, ref uint count, [In, Out] RM_PROCESS_INFO[] info, ref uint reasons);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool MoveFileEx(string from, string to, int flags);

        const int MOVEFILE_DELAY_UNTIL_REBOOT = 4;
        const int MaxListed = 5000; // arquivos consultados para achar quem trava (a pasta inteira é apagada mesmo assim)

        /// <summary>Motivo de recusa, ou null se o caminho pode ser apagado.</summary>
        static string Blocked(string full)
        {
            string root = Path.GetPathRoot(full);
            string trimmed = full.TrimEnd('\\');
            if (full.StartsWith(@"\\")) return "Caminhos de rede não são aceitos.";
            if (trimmed.Length <= 2 || string.Equals(trimmed + "\\", root, StringComparison.OrdinalIgnoreCase)) return "A raiz de um disco não pode ser apagada.";

            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if (trimmed.Equals(win, StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase))
                return "Arquivos da pasta do Windows são protegidos: apagar pode impedir o PC de ligar.";

            // pastas-base: dá para apagar o que está dentro, nunca elas mesmas (nem uma pasta que as contenha)
            var bases = new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(root, "Boot"), Path.Combine(root, "Recovery"), Path.Combine(root, "System Volume Information"), Path.Combine(root, "$Recycle.Bin") };
            foreach (var b in bases.Where(x => !string.IsNullOrEmpty(x)).Select(x => x.TrimEnd('\\')))
                if (trimmed.Equals(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(trimmed + "\\", StringComparison.OrdinalIgnoreCase))
                    return "Esta é uma pasta essencial do sistema: apague só o que está dentro dela.";

            // arquivos de boot e de sistema na raiz do disco
            string parent = Path.GetDirectoryName(trimmed);
            if (parent != null && parent.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(trimmed).ToLowerInvariant();
                if (new[] { "pagefile.sys", "hiberfil.sys", "swapfile.sys", "dumpstack.log.tmp", "bootmgr", "bootnxt", "bootsect.bak", "boot" }.Contains(name)) return "Arquivo de sistema necessário para o Windows ligar.";
                // qualquer outro item marcado como de sistema na raiz do disco também fica de fora
                try { if ((File.GetAttributes(trimmed) & FileAttributes.System) != 0) return "Item de sistema na raiz do disco."; } catch { }
            }
            string self = System.Windows.Forms.Application.ExecutablePath;
            if (self.Equals(trimmed, StringComparison.OrdinalIgnoreCase) || self.StartsWith(trimmed + "\\", StringComparison.OrdinalIgnoreCase)) return "O próprio GROBS está nesta pasta.";
            return null;
        }

        static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Escolha um arquivo ou pasta.");
            return Path.GetFullPath(path.Trim().Trim('"'));
        }

        static bool IsLink(FileSystemInfo i) => (i.Attributes & FileAttributes.ReparsePoint) != 0;

        /// <summary>Todos os arquivos abaixo da pasta, sem entrar em links (o destino de um link nunca é tocado).</summary>
        static void Walk(string dir, List<string> files, List<string> dirs)
        {
            FileSystemInfo[] items;
            try { items = new DirectoryInfo(dir).GetFileSystemInfos(); }
            catch (UnauthorizedAccessException) { Unlock(dir, true); try { items = new DirectoryInfo(dir).GetFileSystemInfos(); } catch { return; } }
            catch { return; }
            foreach (var i in items)
            {
                if (i is DirectoryInfo)
                {
                    if (!IsLink(i)) Walk(i.FullName, files, dirs);
                    dirs.Add(i.FullName);
                }
                else files.Add(i.FullName);
            }
        }

        /// <summary>Processos que estão com os arquivos abertos (Restart Manager, a mesma API do "arquivo em uso" do Windows).</summary>
        static List<RM_PROCESS_INFO> Lockers(IEnumerable<string> files)
        {
            var result = new List<RM_PROCESS_INFO>();
            uint session;
            if (RmStartSession(out session, 0, Guid.NewGuid().ToString("N")) != 0) return result;
            try
            {
                var arr = files.Take(MaxListed).ToArray();
                if (arr.Length == 0 || RmRegisterResources(session, (uint)arr.Length, arr, 0, IntPtr.Zero, 0, null) != 0) return result;
                uint needed, count = 0, reasons = 0;
                int r = RmGetList(session, out needed, ref count, null, ref reasons);
                if (r == 234 && needed > 0) // ERROR_MORE_DATA
                {
                    var info = new RM_PROCESS_INFO[needed];
                    count = needed;
                    if (RmGetList(session, out needed, ref count, info, ref reasons) == 0) result.AddRange(info.Take((int)count));
                }
            }
            finally { RmEndSession(session); }
            return result;
        }

        public static object Inspect(string path)
        {
            string full = Normalize(path);
            bool isDir = Directory.Exists(full), isFile = File.Exists(full);
            if (!isDir && !isFile) throw new FileNotFoundException("Arquivo ou pasta não encontrado.");
            string blocked = Blocked(full);
            var files = new List<string>(); var dirs = new List<string>();
            long size = 0;
            if (blocked == null && isDir && !IsLink(new DirectoryInfo(full))) Walk(full, files, dirs);
            else if (isFile) files.Add(full);
            foreach (var f in files) try { size += new FileInfo(f).Length; } catch { }
            string owner = "";
            try { owner = (isDir ? (FileSystemSecurity)Directory.GetAccessControl(full, AccessControlSections.Owner) : File.GetAccessControl(full, AccessControlSections.Owner)).GetOwner(typeof(NTAccount)).ToString(); } catch { }
            var locks = blocked == null ? Lockers(files) : new List<RM_PROCESS_INFO>();
            return new Dictionary<string, object> {
                { "path", full }, { "isDir", isDir }, { "files", files.Count }, { "dirs", dirs.Count }, { "size", (double)size }, { "owner", owner }, { "blocked", blocked }, { "admin", Program.IsAdmin },
                { "lockers", locks.GroupBy(l => l.Process.dwProcessId).Select(g => (object)new Dictionary<string, object> { { "pid", g.Key }, { "name", g.First().strAppName } }).ToList() } };
        }

        /// <summary>Administradores viram donos e ganham controle total. Atributos somente leitura/sistema/oculto são limpos.</summary>
        static void Unlock(string path, bool isDir)
        {
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            try
            {
                // em link simbólico/junção a permissão cairia no destino, que não é o que está sendo apagado
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
                if (isDir)
                {
                    var own = new DirectorySecurity(); own.SetOwner(admins); Directory.SetAccessControl(path, own);
                    var acl = new DirectorySecurity(); acl.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    Directory.SetAccessControl(path, acl);
                }
                else
                {
                    var own = new FileSecurity(); own.SetOwner(admins); File.SetAccessControl(path, own);
                    var acl = new FileSecurity(); acl.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));
                    File.SetAccessControl(path, acl);
                }
            }
            catch (Exception ex) { Program.Log(ex); }
            try { File.SetAttributes(path, isDir ? FileAttributes.Directory : FileAttributes.Normal); } catch { }
        }

        static bool TryDelete(string path, bool isDir)
        {
            try
            {
                if (isDir) { var d = new DirectoryInfo(path); if (d.Exists) { if ((d.Attributes & FileAttributes.ReadOnly) != 0) d.Attributes = FileAttributes.Directory; d.Delete(false); } }
                else if (File.Exists(path)) { if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
                return true;
            }
            catch { return false; }
        }

        /// <summary>closeApps: fecha os programas que travam os arquivos. O que continuar preso é apagado ao reiniciar.</summary>
        public static object Run(string path, bool closeApps)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            string full = Normalize(path);
            string blocked = Blocked(full);
            if (blocked != null) throw new InvalidOperationException(blocked);
            bool isDir = Directory.Exists(full);
            if (!isDir && !File.Exists(full)) throw new FileNotFoundException("Arquivo ou pasta não encontrado.");
            foreach (var p in new[] { "SeTakeOwnershipPrivilege", "SeRestorePrivilege", "SeBackupPrivilege", "SeDebugPrivilege" }) Native.EnablePrivilege(p);

            var files = new List<string>(); var dirs = new List<string>();
            if (isDir)
            {
                if (!IsLink(new DirectoryInfo(full))) Walk(full, files, dirs);
                dirs.Add(full); // Walk já devolve as subpastas de dentro para fora; a raiz fica por último
            }
            else files.Add(full);

            int deleted = 0, pending = 0, failed = 0, closed = 0;
            var errors = new List<string>();
            var retry = new List<string>();
            foreach (var f in files)
            {
                if (TryDelete(f, false)) { deleted++; continue; }
                Unlock(f, false);
                if (TryDelete(f, false)) deleted++; else retry.Add(f);
            }
            if (retry.Count > 0 && closeApps)
            {
                foreach (var l in Lockers(retry))
                    try { Processes.ForceKill(l.Process.dwProcessId, false); closed++; }
                    catch (Exception ex) { errors.Add(l.strAppName + ": " + ex.Message); } // processos essenciais não são fechados
                if (closed > 0) System.Threading.Thread.Sleep(500);
            }
            foreach (var f in retry)
            {
                if (TryDelete(f, false)) deleted++;
                else if (MoveFileEx(f, null, MOVEFILE_DELAY_UNTIL_REBOOT)) pending++;
                else { failed++; if (errors.Count < 20) errors.Add(f + ": erro " + Marshal.GetLastWin32Error()); }
            }
            foreach (var d in dirs)
            {
                if (TryDelete(d, true)) continue;
                Unlock(d, true);
                if (TryDelete(d, true)) continue;
                // pasta com arquivo preso: sai junto na reinicialização (a ordem de dentro para fora é preservada)
                if (!MoveFileEx(d, null, MOVEFILE_DELAY_UNTIL_REBOOT)) { failed++; if (errors.Count < 20) errors.Add(d + ": erro " + Marshal.GetLastWin32Error()); }
                else if (pending == 0) pending++;
            }
            return new Dictionary<string, object> { { "deleted", deleted }, { "pending", pending }, { "failed", failed }, { "closed", closed }, { "errors", errors }, { "exists", isDir ? Directory.Exists(full) : File.Exists(full) } };
        }
    }
}
