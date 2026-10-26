using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Grobs
{
    /// <summary>Lista de processos com uma única chamada NtQuerySystemInformation (como o Process Hacker).</summary>
    internal static class Processes
    {
        static readonly Dictionary<long, long> _lastTimes = new Dictionary<long, long>();
        static long _lastStamp;
        static readonly Dictionary<int, string> _paths = new Dictionary<int, string>();
        static readonly object _lock = new object();
        static readonly int Cpus = Environment.ProcessorCount;

        struct Raw { public int Pid, Threads, Priority; public string Name; public long Time, Created; public double WorkingSet, Private; }

        static List<Raw> Query()
        {
            var list = new List<Raw>();
            int len = 512 * 1024;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                IntPtr buf = Marshal.AllocHGlobal(len);
                try
                {
                    int ret;
                    int st = Native.NtQuerySystemInformation(5, buf, len, out ret);
                    if (st == unchecked((int)0xC0000004)) { len = Math.Max(len * 2, ret + 65536); continue; }
                    if (st != 0) return list;
                    IntPtr p = buf;
                    var t = typeof(Native.SYSTEM_PROCESS_INFORMATION);
                    while (true)
                    {
                        var spi = (Native.SYSTEM_PROCESS_INFORMATION)Marshal.PtrToStructure(p, t);
                        int pid = spi.UniqueProcessId.ToInt32();
                        string name = spi.ImageName.Buffer != IntPtr.Zero ? Marshal.PtrToStringUni(spi.ImageName.Buffer, spi.ImageName.Length / 2) : (pid == 0 ? "Ocioso do sistema" : "System");
                        list.Add(new Raw
                        {
                            Pid = pid, Name = name, Threads = (int)spi.NumberOfThreads, Priority = spi.BasePriority,
                            Time = spi.UserTime + spi.KernelTime, Created = spi.CreateTime,
                            WorkingSet = (double)(ulong)spi.WorkingSetSize.ToInt64(),
                            Private = spi.WorkingSetPrivateSize, // "Memória (conjunto de trabalho privado)" do Gerenciador de Tarefas
                        });
                        if (spi.NextEntryOffset == 0) break;
                        p += (int)spi.NextEntryOffset;
                    }
                    return list;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return list;
        }

        static string PathOf(int pid)
        {
            string p;
            if (_paths.TryGetValue(pid, out p)) return p;
            p = "";
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h != IntPtr.Zero)
            {
                var sb = new StringBuilder(1024); int size = sb.Capacity;
                if (Native.QueryFullProcessImageName(h, 0, sb, ref size)) p = sb.ToString();
                Native.CloseHandle(h);
            }
            _paths[pid] = p;
            return p;
        }

        public static object List()
        {
            lock (_lock)
            {
                var raw = Query();
                long now = DateTime.UtcNow.Ticks;
                double elapsed = _lastStamp == 0 ? 0 : (now - _lastStamp);
                var seen = new HashSet<long>();
                var outList = new List<object>(raw.Count);
                foreach (var r in raw)
                {
                    if (r.Pid == 0) continue;
                    long key = ((long)r.Pid << 20) ^ (r.Created & 0xFFFFF);
                    seen.Add(key);
                    long last; double cpu = 0;
                    if (elapsed > 0 && _lastTimes.TryGetValue(key, out last))
                        cpu = Math.Max(0, Math.Min(100, (r.Time - last) / elapsed / Cpus * 100));
                    _lastTimes[key] = r.Time;
                    outList.Add(new Dictionary<string, object> {
                        { "pid", r.Pid }, { "name", r.Name }, { "cpu", Math.Round(cpu, 1) }, { "mem", r.Private }, { "ws", r.WorkingSet },
                        { "threads", r.Threads }, { "prio", r.Priority }, { "path", PathOf(r.Pid) } });
                }
                foreach (var k in new List<long>(_lastTimes.Keys)) if (!seen.Contains(k)) _lastTimes.Remove(k);
                if (_paths.Count > 2000) _paths.Clear();
                _lastStamp = now;
                return outList;
            }
        }

        public static object Kill(int pid)
        {
            if (pid <= 4 || pid == Process.GetCurrentProcess().Id) throw new InvalidOperationException("Processo protegido");
            using (var p = Process.GetProcessById(pid)) p.Kill();
            return true;
        }

        public static object SetPriority(int pid, string level)
        {
            ProcessPriorityClass c;
            switch (level)
            {
                case "high": c = ProcessPriorityClass.High; break;
                case "above": c = ProcessPriorityClass.AboveNormal; break;
                case "below": c = ProcessPriorityClass.BelowNormal; break;
                case "idle": c = ProcessPriorityClass.Idle; break;
                default: c = ProcessPriorityClass.Normal; break;
            }
            using (var p = Process.GetProcessById(pid)) p.PriorityClass = c;
            return true;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr h, uint code);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        delegate bool EnumProc(IntPtr h, IntPtr l);

        static readonly HashSet<string> Critical = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "System", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe", "svchost.exe", "dwm.exe", "fontdrvhost.exe", "Registry", "Memory Compression", "MsMpEng.exe" };

        /// <summary>
        /// Finaliza à força (TerminateProcess) — como "taskkill /F /T". Com admin usa SeDebugPrivilege,
        /// o que fecha processos que o Gerenciador de Tarefas não consegue. Processos críticos do Windows são recusados.
        /// </summary>
        public static object ForceKill(int pid, bool tree)
        {
            if (pid <= 4 || pid == Process.GetCurrentProcess().Id) throw new InvalidOperationException("Processo protegido");
            if (Program.IsAdmin) Native.EnablePrivilege("SeDebugPrivilege");
            var raw = Query();
            var me = raw.FirstOrDefault(r => r.Pid == pid);
            if (me.Name != null && Critical.Contains(me.Name)) throw new InvalidOperationException(me.Name + " é essencial para o Windows e não pode ser finalizado");
            var targets = new List<int> { pid };
            if (tree)
            {
                // filhos, netos...: InheritedFromUniqueProcessId (vem junto na consulta)
                var parents = RawParents();
                for (int i = 0; i < targets.Count; i++)
                    foreach (var kv in parents) if (kv.Value == targets[i] && !targets.Contains(kv.Key)) targets.Add(kv.Key);
            }
            int killed = 0; string err = null;
            foreach (var t in targets.AsEnumerable().Reverse())
            {
                var r = raw.FirstOrDefault(x => x.Pid == t);
                if (r.Name != null && Critical.Contains(r.Name)) continue;
                IntPtr h = Native.OpenProcess(Native.PROCESS_TERMINATE, false, t);
                if (h == IntPtr.Zero) { err = "Acesso negado" + (Program.IsAdmin ? "" : " — abra o GROBS como administrador"); continue; }
                if (TerminateProcess(h, 1)) killed++; else err = "O Windows recusou finalizar (" + Marshal.GetLastWin32Error() + ")";
                Native.CloseHandle(h);
            }
            if (killed == 0) throw new InvalidOperationException(err ?? "Nada foi finalizado");
            return new Dictionary<string, object> { { "killed", killed } };
        }

        internal static Dictionary<int, int> RawParents()
        {
            var map = new Dictionary<int, int>();
            int len = 1024 * 1024; IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                int ret;
                if (Native.NtQuerySystemInformation(5, buf, len, out ret) != 0) return map;
                IntPtr p = buf; var t = typeof(Native.SYSTEM_PROCESS_INFORMATION);
                while (true)
                {
                    var spi = (Native.SYSTEM_PROCESS_INFORMATION)Marshal.PtrToStructure(p, t);
                    map[spi.UniqueProcessId.ToInt32()] = spi.InheritedFromUniqueProcessId.ToInt32();
                    if (spi.NextEntryOffset == 0) break;
                    p += (int)spi.NextEntryOffset;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return map;
        }

        /// <summary>Janelas "Não respondendo" (mesmo critério do Windows: IsHungAppWindow).</summary>
        public static object Hung()
        {
            var list = new List<object>(); var seen = new HashSet<int>();
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h) || !IsHungAppWindow(h)) return true;
                int pid; GetWindowThreadProcessId(h, out pid);
                if (!seen.Add(pid)) return true;
                var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
                string name = ""; try { using (var p = Process.GetProcessById(pid)) name = p.ProcessName; } catch { }
                list.Add(new Dictionary<string, object> { { "pid", pid }, { "title", sb.ToString() }, { "name", name } });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>Atalho Ctrl+Alt+F4: finaliza à força o app em primeiro plano.</summary>
        public static void KillForeground()
        {
            int pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            if (pid <= 4 || pid == Process.GetCurrentProcess().Id) return;
            try { using (var p = Process.GetProcessById(pid)) if (p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return; } catch { return; }
            try { ForceKill(pid, true); } catch { }
        }

        public static int KillByName(IEnumerable<string> names)
        {
            int n = 0;
            foreach (var name in names)
                foreach (var p in Process.GetProcessesByName(name))
                    using (p) { try { p.Kill(); n++; } catch { } }
            return n;
        }
    }
}
