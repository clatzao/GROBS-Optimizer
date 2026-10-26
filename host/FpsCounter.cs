using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Grobs
{
    /// <summary>
    /// FPS por processo via ETW em tempo real (mesma fonte do PresentMon): conta os eventos de Present
    /// do DXGI (DirectX 10/11/12), do D3D9 e do kernel gráfico (Vulkan/OpenGL, melhor esforço).
    /// Exige administrador (sessões ETW em tempo real).
    /// </summary>
    internal static class FpsCounter
    {
        static readonly Guid DXGI = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        static readonly Guid D3D9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        static readonly Guid DxgKrnl = new Guid("802EC45A-1E99-4B83-9920-87C98277BA9D");
        const string SessionName = "GROBS_FPS";

        // ---------- ETW ----------
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint StartTrace(out ulong handle, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint ControlTrace(ulong handle, string name, IntPtr props, uint code);
        [DllImport("advapi32.dll")] static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level, ulong any, ulong all, uint timeout, IntPtr param);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ulong OpenTrace(ref EVENT_TRACE_LOGFILE log);
        [DllImport("advapi32.dll")] static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")] static extern uint CloseTrace(ulong handle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void EventRecordCallback(IntPtr record);

        [StructLayout(LayoutKind.Sequential)]
        struct EVENT_TRACE_HEADER { public ushort Size, FieldTypeFlags; public uint Version, ThreadId, ProcessId; public long TimeStamp; public Guid Guid; public uint KernelTime, UserTime; }
        [StructLayout(LayoutKind.Sequential)]
        struct EVENT_TRACE { public EVENT_TRACE_HEADER Header; public uint InstanceId, ParentInstanceId; public Guid ParentGuid; public IntPtr MofData; public uint MofLength, ClientContext; }
        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEMTIME { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TIME_ZONE_INFORMATION
        {
            public int Bias; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string StandardName; public SYSTEMTIME StandardDate; public int StandardBias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DaylightName; public SYSTEMTIME DaylightDate; public int DaylightBias;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct TRACE_LOGFILE_HEADER
        {
            public uint BufferSize, Version, ProviderVersion, NumberOfProcessors; public long EndTime; public uint TimerResolution, MaximumFileSize, LogFileMode, BuffersWritten;
            public Guid LogInstanceGuid; public IntPtr LoggerName, LogFileName; public TIME_ZONE_INFORMATION TimeZone; public long BootTime, PerfFreq, StartTime; public uint ReservedFlags, BuffersLost;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct EVENT_TRACE_LOGFILE
        {
            public string LogFileName, LoggerName; public long CurrentTime; public uint BuffersRead, ProcessTraceMode;
            public EVENT_TRACE CurrentEvent; public TRACE_LOGFILE_HEADER LogfileHeader; public IntPtr BufferCallback;
            public uint BufferSize, Filled, EventsLost; public EventRecordCallback EventRecordCallback; public uint IsKernelTrace; public IntPtr Context;
        }

        // ---------- estado ----------
        sealed class Proc { public readonly Queue<long> Times = new Queue<long>(); public int Source; }
        static readonly Dictionary<int, Proc> _procs = new Dictionary<int, Proc>();
        static readonly object _lock = new object();
        static ulong _session, _trace;
        static Thread _thread;
        static EventRecordCallback _cb; // referência viva para o GC não coletar
        static long _qpcFreq;
        public static bool Running { get; private set; }
        public static string LastError { get; private set; }

        [DllImport("kernel32.dll")] static extern bool QueryPerformanceFrequency(out long f);

        static IntPtr Props()
        {
            int baseSize = IntPtr.Size == 8 ? 120 : 116; // sizeof(EVENT_TRACE_PROPERTIES)
            int total = baseSize + 1024;
            IntPtr p = Marshal.AllocHGlobal(total);
            for (int i = 0; i < total; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt32(p, 0, total);            // Wnode.BufferSize
            Marshal.WriteInt32(p, 40, 1);               // Wnode.ClientContext = QPC
            Marshal.WriteInt32(p, 44, 0x00020000);      // WNODE_FLAG_TRACED_GUID
            Marshal.WriteInt32(p, 48, 64);              // BufferSize (KB)
            Marshal.WriteInt32(p, 52, 4);               // MinimumBuffers
            Marshal.WriteInt32(p, 56, 16);              // MaximumBuffers
            Marshal.WriteInt32(p, 64, 0x00000100);      // EVENT_TRACE_REAL_TIME_MODE
            Marshal.WriteInt32(p, 68, 1);               // FlushTimer (s)
            Marshal.WriteInt32(p, baseSize - 4, baseSize); // LoggerNameOffset
            return p;
        }

        public static bool Start()
        {
            if (Running) return true;
            LastError = null;
            if (!Program.IsAdmin) { LastError = "O contador de FPS precisa do modo administrador."; return false; }
            QueryPerformanceFrequency(out _qpcFreq);
            IntPtr props = Props();
            try
            {
                uint r = StartTrace(out _session, SessionName, props);
                if (r == 183) // já existe (sessão órfã de uma execução anterior)
                {
                    ControlTrace(0, SessionName, props, 1);
                    Marshal.FreeHGlobal(props); props = Props();
                    r = StartTrace(out _session, SessionName, props);
                }
                if (r != 0) { LastError = "Falha ao iniciar a sessão ETW (" + r + ")."; return false; }
            }
            finally { Marshal.FreeHGlobal(props); }

            var g = DXGI; EnableTraceEx2(_session, ref g, 1, 4, ulong.MaxValue, 0, 0, IntPtr.Zero);
            g = D3D9; EnableTraceEx2(_session, ref g, 1, 4, ulong.MaxValue, 0, 0, IntPtr.Zero);
            g = DxgKrnl; EnableTraceEx2(_session, ref g, 1, 4, 0x1 | 0x8000000, 0, 0, IntPtr.Zero); // Base | Present

            _cb = OnEvent;
            var log = new EVENT_TRACE_LOGFILE { LoggerName = SessionName, ProcessTraceMode = 0x00000100 | 0x10000000, EventRecordCallback = _cb };
            _trace = OpenTrace(ref log);
            if (_trace == ulong.MaxValue || _trace == 0xFFFFFFFF) { LastError = "Falha ao abrir a sessão ETW."; Stop(); return false; }
            Running = true;
            _thread = new Thread(() => { try { ProcessTrace(new[] { _trace }, 1, IntPtr.Zero, IntPtr.Zero); } catch { } Running = false; }) { IsBackground = true, Name = "ETW FPS" };
            _thread.Start();
            return true;
        }

        public static void Stop()
        {
            if (_session != 0)
            {
                IntPtr props = Props();
                ControlTrace(_session, null, props, 1);
                Marshal.FreeHGlobal(props);
                _session = 0;
            }
            if (_trace != 0) { CloseTrace(_trace); _trace = 0; }
            Running = false;
            lock (_lock) _procs.Clear();
        }

        static void OnEvent(IntPtr rec)
        {
            // EVENT_HEADER: ProcessId @12, TimeStamp @16, ProviderId @24, EventDescriptor.Id @40 (igual em x86 e x64)
            ushort id = (ushort)Marshal.ReadInt16(rec, 40);
            var provider = ReadGuid(rec + 24);
            int src;
            if (provider == DXGI && (id == 42)) src = 1;
            else if (provider == D3D9 && id == 1) src = 1;
            else if (provider == DxgKrnl && id == 184) src = 2;
            else return;
            int pid = Marshal.ReadInt32(rec, 12);
            long ts = Marshal.ReadInt64(rec, 16);
            lock (_lock)
            {
                Proc p;
                if (!_procs.TryGetValue(pid, out p)) _procs[pid] = p = new Proc { Source = src };
                // processos com Present do DXGI/D3D9 ignoram o evento do kernel (seria contagem dupla)
                if (src == 1 && p.Source == 2) { p.Source = 1; p.Times.Clear(); }
                if (src != p.Source) return;
                p.Times.Enqueue(ts);
                while (p.Times.Count > 2000) p.Times.Dequeue();
            }
        }

        static Guid ReadGuid(IntPtr p)
        {
            var b = new byte[16];
            Marshal.Copy(p, b, 0, 16);
            return new Guid(b);
        }

        [DllImport("kernel32.dll")] static extern bool QueryPerformanceCounter(out long c);

        /// <summary>FPS médio no último segundo, frametime médio e 1% low dos últimos ~10 s para um processo.</summary>
        public static Dictionary<string, object> Get(int pid)
        {
            if (!Running || pid <= 0) return null;
            long now; QueryPerformanceCounter(out now);
            long[] t;
            lock (_lock)
            {
                Proc p;
                if (!_procs.TryGetValue(pid, out p) || p.Times.Count < 2) return null;
                long limit = now - _qpcFreq * 10;
                while (p.Times.Count > 0 && p.Times.Peek() < limit) p.Times.Dequeue();
                t = p.Times.ToArray();
            }
            if (t.Length < 2) return null;
            int lastSec = t.Count(x => x >= now - _qpcFreq);
            var deltas = new List<double>(t.Length);
            for (int i = 1; i < t.Length; i++) deltas.Add((t[i] - t[i - 1]) * 1000.0 / _qpcFreq);
            // frametime médio só do último segundo (pausas antigas não distorcem)
            var recent = new List<double>();
            for (int i = 1; i < t.Length; i++) if (t[i - 1] >= now - _qpcFreq) recent.Add((t[i] - t[i - 1]) * 1000.0 / _qpcFreq);
            double avgFt = recent.Count > 0 ? recent.Average() : (lastSec > 0 ? 1000.0 / lastSec : 0);
            var sorted = deltas.OrderByDescending(d => d).ToList();
            int n = Math.Max(1, sorted.Count / 100);
            double low1 = 1000.0 / sorted.Take(n).Average();
            return new Dictionary<string, object> { { "fps", lastSec }, { "frametime", Math.Round(avgFt, 1) }, { "low1", Math.Round(low1) } };
        }

        /// <summary>Processos que estão apresentando quadros agora (para a lista de jogos detectados).</summary>
        public static List<int> ActivePids()
        {
            long now; QueryPerformanceCounter(out now);
            lock (_lock) return _procs.Where(kv => kv.Value.Times.Count > 0 && kv.Value.Times.Last() > now - _qpcFreq * 2).Select(kv => kv.Key).ToList();
        }
    }
}
