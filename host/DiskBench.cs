using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Grobs
{
    /// <summary>
    /// Teste de velocidade de disco no padrão do CrystalDiskMark: SEQ1M Q8T1, SEQ1M Q1T1, RND4K Q32T1 e RND4K Q1T1,
    /// leitura e gravação, sem cache do Windows (FILE_FLAG_NO_BUFFERING) e com fila real de requisições (E/S sobreposta).
    /// </summary>
    internal static class DiskBench
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(SafeFileHandle h, IntPtr buf, int n, IntPtr read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle h, IntPtr buf, int n, IntPtr written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr ov, out int bytes, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateEvent(IntPtr sec, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern uint WaitForMultipleObjects(int n, IntPtr[] h, bool all, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr VirtualAlloc(IntPtr addr, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll")] static extern bool VirtualFree(IntPtr addr, UIntPtr size, uint type);

        sealed class Spec { public string Id; public bool Seq; public int Block, Queue; }

        static readonly Spec[] Full = {
            new Spec { Id = "SEQ1M Q8T1", Seq = true, Block = 1 << 20, Queue = 8 }, new Spec { Id = "SEQ1M Q1T1", Seq = true, Block = 1 << 20, Queue = 1 },
            new Spec { Id = "RND4K Q32T1", Seq = false, Block = 4096, Queue = 32 }, new Spec { Id = "RND4K Q1T1", Seq = false, Block = 4096, Queue = 1 } };
        static readonly Spec[] Quick = { Full[0], Full[3] };

        static volatile bool _cancel;
        static volatile string _phase = "";
        static Thread _thread;
        static volatile bool _busy; // falso já na notificação final (a thread ainda está viva nesse momento)
        static Dictionary<string, object> _result;
        static string _error;

        public static object Status() => new Dictionary<string, object> { { "running", _busy }, { "phase", _phase }, { "result", _result }, { "error", _error } };
        public static object Cancel() { _cancel = true; return true; }

        public static object Start(string drive, int sizeMiB, int passes, bool quick)
        {
            if (_busy) return Status();
            _cancel = false; _error = null; _result = null;
            _thread = new Thread(() =>
            {
                try { _result = Run(drive, sizeMiB, passes, quick, r => { _result = r; Program.Window?.Notify("diskBench", Status()); }); }
                catch (Exception ex) { _error = ex.Message; }
                _busy = false; _phase = "";
                Program.Window?.Notify("diskBench", Status());
            }) { IsBackground = true, Name = "DiskBench" };
            _busy = true;
            _thread.Start();
            return Status();
        }

        /// <summary>Também usado pelo Benchmark rápido (modo quick, síncrono).</summary>
        public static Dictionary<string, object> Run(string drive, int sizeMiB, int passes, bool quick, Action<Dictionary<string, object>> progress = null)
        {
            drive = (drive ?? "C:").TrimEnd('\\');
            if (drive.Length < 2) drive += ":";
            var di = new DriveInfo(drive);
            if (!di.IsReady) throw new InvalidOperationException("Unidade não disponível.");
            long size = (long)Math.Max(64, sizeMiB) << 20;
            if (di.AvailableFreeSpace < size + (256L << 20)) throw new InvalidOperationException("Espaço livre insuficiente em " + drive + " para um arquivo de teste de " + (size >> 20) + " MiB.");
            string sys = Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\');
            string dir = drive.Equals(sys, StringComparison.OrdinalIgnoreCase) ? Path.GetTempPath() : Path.Combine(drive + "\\", "GROBSBench");
            bool made = !Directory.Exists(dir);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "grobs-bench-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            var specs = quick ? Quick : Full;
            var tests = specs.Select(s => new Dictionary<string, object> { { "id", s.Id }, { "read", -1.0 }, { "write", -1.0 } }).ToList();
            var res = new Dictionary<string, object> { { "drive", drive }, { "size", sizeMiB }, { "passes", passes }, { "quick", quick }, { "tests", tests }, { "date", DateTime.Now.ToString("dd/MM/yyyy HH:mm") } };
            int seconds = quick ? 3 : 5;
            IntPtr mem = VirtualAlloc(IntPtr.Zero, (UIntPtr)(32u << 20), 0x3000, 0x04); // 32 MiB alinhados: 32 × 1 MiB ou 32 × 4 KiB
            try
            {
                var rnd = new Random();
                var chunk = new byte[1 << 20];
                rnd.NextBytes(chunk);
                for (int i = 0; i < 32; i++) Marshal.Copy(chunk, 0, mem + (i << 20), chunk.Length);
                _phase = "Preparando arquivo de teste";
                progress?.Invoke(res);
                using (var fs = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.WriteThrough))
                {
                    for (long w = 0; w < size && !_cancel; w += chunk.Length) { rnd.NextBytes(chunk); fs.Write(chunk, 0, chunk.Length); }
                    fs.Flush(true);
                }
                foreach (bool write in new[] { false, true })
                    for (int s = 0; s < specs.Length && !_cancel; s++)
                    {
                        double best = 0, bestIops = 0, bestLat = 0;
                        for (int p = 0; p < Math.Max(1, passes) && !_cancel; p++)
                        {
                            _phase = (write ? "Gravação " : "Leitura ") + specs[s].Id + " — passada " + (p + 1) + "/" + Math.Max(1, passes);
                            progress?.Invoke(res);
                            double mbps, iops, lat;
                            Measure(file, size, specs[s], write, seconds, mem, out mbps, out iops, out lat);
                            if (mbps > best) { best = mbps; bestIops = iops; bestLat = lat; }
                        }
                        tests[s][write ? "write" : "read"] = Math.Round(best, 2);
                        tests[s][write ? "writeIops" : "readIops"] = Math.Round(bestIops);
                        tests[s][write ? "writeLat" : "readLat"] = Math.Round(bestLat, 1);
                        progress?.Invoke(res);
                    }
                if (_cancel) res["cancelled"] = true;
                return res;
            }
            finally
            {
                VirtualFree(mem, UIntPtr.Zero, 0x8000);
                try { File.Delete(file); } catch { }
                if (made) try { Directory.Delete(dir); } catch { }
            }
        }

        static void Measure(string file, long size, Spec s, bool write, int seconds, IntPtr mem, out double mbps, out double iops, out double latUs)
        {
            const uint NO_BUFFERING = 0x20000000, OVERLAPPED = 0x40000000; // como o CrystalDiskMark: sem cache, sem write-through
            using (var h = CreateFile(file, write ? 0xC0000000 : 0x80000000, 0, IntPtr.Zero, 3, NO_BUFFERING | OVERLAPPED, IntPtr.Zero))
            {
                if (h.IsInvalid) throw new IOException("Não foi possível abrir o arquivo de teste (" + Marshal.GetLastWin32Error() + ").");
                int q = s.Queue, p = IntPtr.Size;
                var ovs = new IntPtr[q]; var events = new IntPtr[q]; var started = new long[q];
                var rnd = new Random();
                long blocks = size / s.Block, next = 0;
                long done = 0, ops = 0; double latSum = 0;
                var sw = Stopwatch.StartNew();
                long limit = Stopwatch.Frequency * seconds;
                try
                {
                    for (int i = 0; i < q; i++)
                    {
                        ovs[i] = Marshal.AllocHGlobal(p * 2 + 8 + p);
                        events[i] = CreateEvent(IntPtr.Zero, true, false, null);
                    }
                    Func<int, bool> issue = i =>
                    {
                        long off = (s.Seq ? (next++ % blocks) : (long)(rnd.NextDouble() * blocks)) * s.Block;
                        for (int b = 0; b < p * 2; b++) Marshal.WriteByte(ovs[i], b, 0);
                        Marshal.WriteInt32(ovs[i], p * 2, (int)(off & 0xFFFFFFFF));
                        Marshal.WriteInt32(ovs[i], p * 2 + 4, (int)(off >> 32));
                        Marshal.WriteIntPtr(ovs[i], p * 2 + 8, events[i]);
                        IntPtr buf = mem + (s.Block >= (1 << 20) ? i << 20 : i * 4096);
                        started[i] = sw.ElapsedTicks;
                        bool ok = write ? WriteFile(h, buf, s.Block, IntPtr.Zero, ovs[i]) : ReadFile(h, buf, s.Block, IntPtr.Zero, ovs[i]);
                        if (!ok && Marshal.GetLastWin32Error() != 997) throw new IOException("Erro de E/S no teste (" + Marshal.GetLastWin32Error() + ").");
                        return true;
                    };
                    int pending = 0;
                    for (int i = 0; i < q; i++) { issue(i); pending++; }
                    var active = Enumerable.Range(0, q).ToList();
                    while (pending > 0)
                    {
                        var hs = active.Select(i => events[i]).ToArray();
                        uint w = WaitForMultipleObjects(hs.Length, hs, false, 10000);
                        if (w >= hs.Length) throw new IOException("O disco parou de responder durante o teste.");
                        int slot = active[(int)w];
                        int bytes;
                        if (!GetOverlappedResult(h, ovs[slot], out bytes, true)) throw new IOException("Erro de E/S no teste (" + Marshal.GetLastWin32Error() + ").");
                        done += bytes; ops++;
                        latSum += (sw.ElapsedTicks - started[slot]) * 1e6 / Stopwatch.Frequency;
                        if (sw.ElapsedTicks < limit && !_cancel) issue(slot);
                        else { active.Remove(slot); pending--; }
                    }
                }
                finally
                {
                    foreach (var e in events) if (e != IntPtr.Zero) CloseHandle(e);
                    foreach (var o in ovs) if (o != IntPtr.Zero) Marshal.FreeHGlobal(o);
                }
                double secs = sw.Elapsed.TotalSeconds;
                mbps = done / 1e6 / secs; iops = ops / secs; latUs = ops > 0 ? latSum / ops : 0;
            }
        }
    }
}
