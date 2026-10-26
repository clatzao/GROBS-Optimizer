using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Grobs
{
    internal static class Native
    {
        // ---------- janela ----------
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int WM_NCHITTEST = 0x84;
        public const int WM_SYSCOMMAND = 0x0112;
        public const int HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
            HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] public static extern int RegisterWindowMessage(string msg);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr pvParam, uint winIni);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint uiParam, int[] pvParam, uint winIni);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint uiParam, ref ANIMATIONINFO pvParam, uint winIni);

        [StructLayout(LayoutKind.Sequential)]
        public struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }

        // ---------- memória ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        [StructLayout(LayoutKind.Sequential)]
        public struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache,
                KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint HandleCount, ProcessCount, ThreadCount;
        }
        [DllImport("psapi.dll")] public static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pi, uint cb);
        [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();

        // ---------- processos ----------
        public const uint PROCESS_TERMINATE = 0x0001, PROCESS_SET_QUOTA = 0x0100, PROCESS_SET_INFORMATION = 0x0200,
            PROCESS_QUERY_INFORMATION = 0x0400, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder name, ref int size);

        [DllImport("ntdll.dll")] public static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int retLen);
        [DllImport("ntdll.dll")] public static extern int NtSetSystemInformation(int cls, ref int info, int len);

        [StructLayout(LayoutKind.Sequential)]
        public struct UNICODE_STRING { public ushort Length, MaximumLength; public IntPtr Buffer; }

        // Layout sequencial: o marshaller calcula os offsets corretos em x86 e x64.
        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_PROCESS_INFORMATION
        {
            public uint NextEntryOffset, NumberOfThreads;
            public long WorkingSetPrivateSize;
            public uint HardFaultCount, NumberOfThreadsHighWatermark;
            public ulong CycleTime;
            public long CreateTime, UserTime, KernelTime;
            public UNICODE_STRING ImageName;
            public int BasePriority;
            public IntPtr UniqueProcessId, InheritedFromUniqueProcessId;
            public uint HandleCount, SessionId;
            public IntPtr UniqueProcessKey, PeakVirtualSize, VirtualSize;
            public uint PageFaultCount;
            public IntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivatePageCount;
        }

        // ---------- privilégios ----------
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct TOKEN_PRIVILEGES { public uint Count; public long Luid; public uint Attr; }
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool LookupPrivilegeValue(string sys, string name, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES n, int len, IntPtr prev, IntPtr retLen);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();

        public static bool EnablePrivilege(string name)
        {
            IntPtr tok;
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out tok)) return false;
            try
            {
                long luid;
                if (!LookupPrivilegeValue(null, name, out luid)) return false;
                var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attr = 2 };
                return AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
            }
            finally { CloseHandle(tok); }
        }

        // ---------- PDH ----------
        public const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;
        public const uint PDH_MORE_DATA = 0x800007D2;

        [StructLayout(LayoutKind.Explicit)]
        public struct PDH_FMT_COUNTERVALUE { [FieldOffset(0)] public uint CStatus; [FieldOffset(8)] public double doubleValue; }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhOpenQuery(string src, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] public static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] public static extern uint PdhCloseQuery(IntPtr query);
        [DllImport("pdh.dll")] public static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint fmt, out uint type, out PDH_FMT_COUNTERVALUE value);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint fmt, ref uint size, out uint count, IntPtr buffer);

        // ---------- GPU (D3DKMT, igual ao Gerenciador de Tarefas) ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct D3DKMT_ADAPTERINFO { public uint hAdapter; public uint LuidLow; public int LuidHigh; public uint NumOfSources; public int bPrecisePresentRegionsPreferred; }
        [StructLayout(LayoutKind.Sequential)]
        public struct D3DKMT_ENUMADAPTERS2 { public uint NumAdapters; public IntPtr pAdapters; }
        [StructLayout(LayoutKind.Sequential)]
        public struct D3DKMT_QUERYADAPTERINFO { public uint hAdapter; public int Type; public IntPtr pPrivateDriverData; public uint PrivateDriverDataSize; }
        [DllImport("gdi32.dll")] public static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 e);
        [DllImport("gdi32.dll")] public static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO q);

        // ---------- NVIDIA NVML (opcional) ----------
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] public static extern int nvmlInit();
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2")] public static extern int nvmlDeviceGetCount(out uint count);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] public static extern int nvmlDeviceGetHandleByIndex(uint i, out IntPtr dev);
        [DllImport("nvml.dll")] public static extern int nvmlDeviceGetTemperature(IntPtr dev, int sensor, out uint temp);
        [DllImport("nvml.dll")] public static extern int nvmlDeviceGetFanSpeed(IntPtr dev, out uint speed);
        [DllImport("nvml.dll")] public static extern int nvmlDeviceGetPowerUsage(IntPtr dev, out uint mw);

        // ---------- energia ----------
        [DllImport("powrprof.dll")] public static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll")] public static extern uint PowerSetActiveScheme(IntPtr root, ref Guid guid);
        [DllImport("powrprof.dll")] public static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr sub, uint access, uint index, IntPtr buffer, ref uint size);
        [DllImport("powrprof.dll")] public static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, IntPtr buffer, ref uint size);
        [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr p);

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

        // ---------- shell ----------
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")] public static extern uint DnsFlushResolverCache();

        // ---------- enumeração rápida de arquivos ----------
        public static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public uint ftCreationLow, ftCreationHigh, ftAccessLow, ftAccessHigh, ftWriteLow, ftWriteHigh; // FILETIME = 2 x DWORD (alinhamento 4)
            public uint nFileSizeHigh, nFileSizeLow, dwReserved0, dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstFileEx(string name, int infoLevel, out WIN32_FIND_DATA data, int searchOp, IntPtr filter, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool FindNextFile(IntPtr h, out WIN32_FIND_DATA data);
        [DllImport("kernel32.dll")] public static extern bool FindClose(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern uint GetCompressedFileSize(string name, out uint high);
    }
}
