using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Grobs
{
    /// <summary>Benchmark rápido: CPU (1 núcleo e todos), RAM (cópia e latência) e disco (modo rápido do DiskBench). A GPU é testada na interface (WebGL).</summary>
    internal static class Bench
    {
        static volatile string _phase = "";
        static Thread _thread;
        static volatile bool _busy; // falso já na notificação final (a thread ainda está viva nesse momento)
        static Dictionary<string, object> _result;
        static string _error;

        public static object Status() => new Dictionary<string, object> { { "running", _busy }, { "phase", _phase }, { "result", _result }, { "error", _error } };

        public static object Start(bool cpu, bool ram, bool disk)
        {
            if (_busy) return Status();
            _error = null; _result = new Dictionary<string, object>();
            _thread = new Thread(() =>
            {
                var proc = Process.GetCurrentProcess();
                var prio = proc.PriorityClass;
                try
                {
                    proc.PriorityClass = ProcessPriorityClass.AboveNormal;
                    if (cpu)
                    {
                        _phase = "CPU — 1 núcleo"; Push();
                        _result["cpuSingle"] = Math.Round(CpuScore(1, 4));
                        _phase = "CPU — todos os núcleos"; Push();
                        _result["cpuMulti"] = Math.Round(CpuScore(Environment.ProcessorCount, 4));
                        _result["threads"] = Environment.ProcessorCount;
                    }
                    if (ram)
                    {
                        _phase = "RAM — velocidade"; Push();
                        _result["ramCopy"] = Math.Round(RamCopy(), 2);
                        _phase = "RAM — latência"; Push();
                        _result["ramLatency"] = Math.Round(RamLatency(), 1);
                        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); // devolve os ~600 MB usados no teste
                    }
                    if (disk)
                    {
                        _phase = "Disco do sistema"; Push();
                        var d = DiskBench.Run(System.IO.Path.GetPathRoot(Environment.SystemDirectory), 256, 1, true);
                        var tests = (List<Dictionary<string, object>>)d["tests"];
                        _result["diskSeqRead"] = tests[0]["read"]; _result["diskSeqWrite"] = tests[0]["write"];
                        _result["disk4kRead"] = tests[1]["read"]; _result["disk4kWrite"] = tests[1]["write"];
                    }
                }
                catch (Exception ex) { _error = ex.Message; Program.Log(ex); }
                finally { try { proc.PriorityClass = prio; } catch { } proc.Dispose(); }
                _busy = false; _phase = "";
                Push();
            }) { IsBackground = true, Name = "Bench" };
            _busy = true;
            _thread.Start();
            return Status();
        }

        static void Push() => Program.Window?.Notify("bench", Status());

        // Mistura de inteiros (xorshift) e ponto flutuante (sqrt): parecido com o que jogos e compressão fazem.
        static double Kernel(ulong seed)
        {
            ulong h = seed | 1; double x = 0;
            for (int i = 0; i < 4096; i++)
            {
                h ^= h << 13; h ^= h >> 7; h ^= h << 17;
                double c = (h & 0xFFFF) * (1.0 / 65536);
                x = x * 0.5 + Math.Sqrt(c * c + x + 1.0);
            }
            return x;
        }

        /// <summary>Pontos = blocos por segundo / 100 (um i5-3470 dá ~1000 pontos por núcleo — veja CpuScale).</summary>
        static double CpuScore(int threads, int seconds)
        {
            long total = 0; double sink = 0;
            var ts = new Thread[threads];
            var stop = Stopwatch.StartNew();
            long limit = Stopwatch.Frequency * seconds;
            for (int t = 0; t < threads; t++)
            {
                int id = t;
                ts[t] = new Thread(() =>
                {
                    long n = 0; double s = 0;
                    while (stop.ElapsedTicks < limit) { for (int k = 0; k < 16; k++) s += Kernel((ulong)(n * 16 + k + id * 7919)); n += 16; }
                    Interlocked.Add(ref total, n);
                    lock (ts) sink += s;
                }) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
                ts[t].Start();
            }
            foreach (var t in ts) t.Join();
            GC.KeepAlive(sink);
            return total / stop.Elapsed.TotalSeconds / CpuScale;
        }

        internal static double CpuScale = 28; // calibrado: i5-3470 ≈ 1000 pontos por núcleo

        /// <summary>Cópia de memória em GB/s (todas as threads).</summary>
        static double RamCopy()
        {
            int mb = Environment.Is64BitProcess ? 256 : 64;
            var a = new byte[mb << 20]; var b = new byte[mb << 20];
            for (int i = 0; i < a.Length; i += 4096) a[i] = (byte)i;
            int threads = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));
            int part = a.Length / threads;
            Buffer.BlockCopy(a, 0, b, 0, a.Length); // aquece (páginas na memória)
            double best = 0;
            for (int rep = 0; rep < 5; rep++)
            {
                var sw = Stopwatch.StartNew();
                var ts = new Thread[threads];
                for (int t = 0; t < threads; t++)
                {
                    int off = t * part;
                    ts[t] = new Thread(() => { for (int k = 0; k < 4; k++) Buffer.BlockCopy(a, off, b, off, part); }) { IsBackground = true };
                    ts[t].Start();
                }
                foreach (var t in ts) t.Join();
                best = Math.Max(best, (double)part * threads * 4 / sw.Elapsed.TotalSeconds / 1e9);
            }
            return best;
        }

        /// <summary>Latência média de acesso aleatório (ns) — perseguição de ponteiros em 64 MB, fora do cache da CPU.</summary>
        static double RamLatency()
        {
            int n = (64 << 20) / 4;
            var next = new int[n];
            var r = new Random(7);
            // passos de 64 bytes (uma linha de cache) em ordem aleatória
            int lines = n / 16;
            var perm = new int[lines];
            for (int i = 0; i < lines; i++) perm[i] = i;
            for (int i = lines - 1; i > 0; i--) { int j = r.Next(i + 1); int t = perm[i]; perm[i] = perm[j]; perm[j] = t; }
            for (int i = 0; i < lines; i++) next[perm[i] * 16] = perm[(i + 1) % lines] * 16;
            int p = 0; const int steps = 4000000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < steps; i++) p = next[p];
            double ns = sw.Elapsed.TotalMilliseconds * 1e6 / steps;
            GC.KeepAlive(p);
            return ns;
        }
    }
}
