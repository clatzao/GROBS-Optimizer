using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Grobs
{
    /// <summary>
    /// Overclock de GPU NVIDIA pela NVAPI (mesmas funções do MSI Afterburner/NvAPIWrapper):
    /// offset de clock do núcleo e da memória (P0), limite de energia e controle da ventoinha.
    /// Os limites vêm do próprio driver — o app nunca ultrapassa o que a placa informa como permitido.
    /// </summary>
    internal static class GpuOc
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string f);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr m, string n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr QI(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int F0();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FEnum([Out] IntPtr[] handles, out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FBuf(IntPtr gpu, IntPtr buffer);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FName(IntPtr gpu, IntPtr name);

        static QI _qi;
        static bool _init;
        static IntPtr _gpu;
        static string _name;

        const uint ID_Init = 0x0150E828, ID_Enum = 0xE5AC921F, ID_Name = 0xCEEE8E9F,
            ID_GetP20 = 0x6FF81213, ID_SetP20 = 0x0F4DAE6B,
            ID_PwrInfo = 0x34206D86, ID_PwrGet = 0x70916171, ID_PwrSet = 0xAD95F5ED,
            ID_FanGetCtl = 0x814B209F, ID_FanSetCtl = 0xA58971A5, ID_FanStatus = 0x35AED5E8;

        // tamanhos (NvAPIWrapper, Pack=8)
        const int P20_CLOCK = 44, P20_VOLT = 24, P20_STATE = 8 + 8 * P20_CLOCK + 4 * P20_VOLT, P20_V1 = 20 + 16 * P20_STATE, P20_V2 = P20_V1 + 4 + 4 * P20_VOLT;
        const int PWR_INFO = 8 + 4 * 44, PWR_STATUS = 8 + 4 * 16, FAN_CTL = 44 + 32 * 44, FAN_STATUS = 40 + 32 * 52;

        internal static T Fn<T>(uint id) where T : class
        {
            IntPtr p = _qi(id);
            if (p == IntPtr.Zero) throw new NotSupportedException("Função NVAPI indisponível neste driver");
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        static uint Ver(int size, int v) => (uint)size | ((uint)v << 16);

        internal static bool Init()
        {
            if (_init) return _gpu != IntPtr.Zero;
            _init = true;
            try
            {
                IntPtr lib = LoadLibrary(Environment.Is64BitProcess ? "nvapi64.dll" : "nvapi.dll");
                if (lib == IntPtr.Zero) return false;
                IntPtr q = GetProcAddress(lib, "nvapi_QueryInterface");
                if (q == IntPtr.Zero) return false;
                _qi = (QI)Marshal.GetDelegateForFunctionPointer(q, typeof(QI));
                if (Fn<F0>(ID_Init)() != 0) return false;
                var handles = new IntPtr[64]; int count;
                if (Fn<FEnum>(ID_Enum)(handles, out count) != 0 || count == 0) return false;
                _gpu = handles[0];
                IntPtr nb = Marshal.AllocHGlobal(64);
                try { if (Fn<FName>(ID_Name)(_gpu, nb) == 0) _name = Marshal.PtrToStringAnsi(nb); } finally { Marshal.FreeHGlobal(nb); }
                return true;
            }
            catch { _gpu = IntPtr.Zero; return false; }
        }

        static IntPtr Buf(int size, uint version)
        {
            IntPtr b = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(b, i, 0);
            Marshal.WriteInt32(b, 0, (int)version);
            return b;
        }

        static int I(IntPtr b, int off) => Marshal.ReadInt32(b, off);

        /// <summary>Lê offsets atuais/limites (P0), energia e ventoinha.</summary>
        public static object Status()
        {
            var r = new Dictionary<string, object> { { "supported", false }, { "admin", Program.IsAdmin } };
            if (!Init()) { r["reason"] = "Nenhuma GPU NVIDIA com driver compatível foi encontrada."; return r; }
            r["supported"] = true; r["name"] = _name;

            // P-states 2.0: tenta V2 e depois V1
            foreach (var v in new[] { 2, 1 })
            {
                int size = v == 2 ? P20_V2 : P20_V1;
                IntPtr b = Buf(size, Ver(size, v));
                try
                {
                    if (Fn<FBuf>(ID_GetP20)(_gpu, b) != 0) continue;
                    r["p20ver"] = v;
                    r["editable"] = (I(b, 4) & 1) == 1;
                    int numClocks = I(b, 12);
                    int ps0 = 20; // primeiro P-state (P0)
                    for (int c = 0; c < Math.Min(8, numClocks); c++)
                    {
                        int e = ps0 + 8 + c * P20_CLOCK;
                        int domain = I(b, e);
                        string key = domain == 0 ? "core" : domain == 4 ? "mem" : null;
                        if (key == null) continue;
                        r[key] = new Dictionary<string, object> {
                            { "offset", I(b, e + 12) / 1000 }, { "min", I(b, e + 16) / 1000 }, { "max", I(b, e + 20) / 1000 },
                            { "base", I(b, e + 24) / 1000 }, { "type", I(b, e + 4) } };
                    }
                    break;
                }
                catch { }
                finally { Marshal.FreeHGlobal(b); }
            }

            // limite de energia (PCM: 100000 = 100%)
            IntPtr info = Buf(PWR_INFO, Ver(PWR_INFO, 1)), st = Buf(PWR_STATUS, Ver(PWR_STATUS, 1));
            try
            {
                if (Fn<FBuf>(ID_PwrInfo)(_gpu, info) == 0 && Fn<FBuf>(ID_PwrGet)(_gpu, st) == 0 && Marshal.ReadByte(info, 5) > 0)
                    r["power"] = new Dictionary<string, object> {
                        { "min", I(info, 8 + 12) / 1000 }, { "def", I(info, 8 + 24) / 1000 }, { "max", I(info, 8 + 36) / 1000 }, { "cur", I(st, 8 + 8) / 1000 } };
            }
            catch { }
            finally { Marshal.FreeHGlobal(info); Marshal.FreeHGlobal(st); }

            // ventoinhas (RTX 20 em diante)
            IntPtr fc = Buf(FAN_CTL, Ver(FAN_CTL, 1)), fs = Buf(FAN_STATUS, Ver(FAN_STATUS, 1));
            try
            {
                if (Fn<FBuf>(ID_FanGetCtl)(_gpu, fc) == 0)
                {
                    int n = I(fc, 8);
                    bool manual = n > 0 && I(fc, 44 + 8) == 1;
                    int level = n > 0 ? I(fc, 44 + 4) : 0, rpm = -1;
                    if (Fn<FBuf>(ID_FanStatus)(_gpu, fs) == 0 && I(fs, 4) > 0) { rpm = I(fs, 40 + 4); if (!manual) level = I(fs, 40 + 16); }
                    r["fan"] = new Dictionary<string, object> { { "count", n }, { "manual", manual }, { "level", level }, { "rpm", rpm } };
                }
            }
            catch { }
            finally { Marshal.FreeHGlobal(fc); Marshal.FreeHGlobal(fs); }
            return r;
        }

        static void Need()
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            if (!Init()) throw new NotSupportedException("GPU NVIDIA não encontrada");
        }

        /// <summary>Aplica offsets de clock (MHz) dentro dos limites que o driver informa.</summary>
        public static object SetClocks(int coreMhz, int memMhz)
        {
            Need();
            var st = (Dictionary<string, object>)Status();
            if (!st.ContainsKey("p20ver")) throw new NotSupportedException("Esta GPU não aceita ajuste de clock pela NVAPI");
            if (!(bool)st["editable"]) throw new NotSupportedException("O driver bloqueia ajuste de clock nesta GPU (comum em notebooks)");
            int v = (int)st["p20ver"], size = v == 2 ? P20_V2 : P20_V1;
            var entries = new List<int[]>(); // domínio, tipo, offset kHz
            foreach (var k in new[] { "core", "mem" })
            {
                if (!st.ContainsKey(k)) continue;
                var c = (Dictionary<string, object>)st[k];
                int want = k == "core" ? coreMhz : memMhz;
                want = Math.Max((int)c["min"], Math.Min((int)c["max"], want)); // nunca além do permitido
                entries.Add(new[] { k == "core" ? 0 : 4, (int)c["type"], want * 1000 });
            }
            IntPtr b = Buf(size, Ver(size, v));
            try
            {
                Marshal.WriteInt32(b, 8, 1);               // numPstates
                Marshal.WriteInt32(b, 12, entries.Count);  // numClocks
                Marshal.WriteInt32(b, 16, 0);              // numBaseVoltages
                Marshal.WriteInt32(b, 20, 0);              // P0
                for (int i = 0; i < entries.Count; i++)
                {
                    int e = 20 + 8 + i * P20_CLOCK;
                    Marshal.WriteInt32(b, e, entries[i][0]); Marshal.WriteInt32(b, e + 4, entries[i][1]); Marshal.WriteInt32(b, e + 12, entries[i][2]);
                }
                int rc = Fn<FBuf>(ID_SetP20)(_gpu, b);
                if (rc != 0) throw new InvalidOperationException("O driver recusou o ajuste (código " + rc + ")");
            }
            finally { Marshal.FreeHGlobal(b); }
            return Status();
        }

        public static object SetPower(int percent)
        {
            Need();
            var st = (Dictionary<string, object>)Status();
            if (!st.ContainsKey("power")) throw new NotSupportedException("Limite de energia indisponível nesta GPU");
            var p = (Dictionary<string, object>)st["power"];
            percent = Math.Max((int)p["min"], Math.Min((int)p["max"], percent));
            IntPtr b = Buf(PWR_STATUS, Ver(PWR_STATUS, 1));
            try
            {
                if (Fn<FBuf>(ID_PwrGet)(_gpu, b) != 0) throw new InvalidOperationException("Falha ao ler o limite atual");
                Marshal.WriteInt32(b, 8 + 8, percent * 1000);
                int rc = Fn<FBuf>(ID_PwrSet)(_gpu, b);
                if (rc != 0) throw new InvalidOperationException("O driver recusou o limite (código " + rc + ")");
            }
            finally { Marshal.FreeHGlobal(b); }
            return Status();
        }

        /// <summary>level &lt; 0 = automático (controle do driver).</summary>
        public static object SetFan(int level)
        {
            Need();
            IntPtr b = Buf(FAN_CTL, Ver(FAN_CTL, 1));
            try
            {
                if (Fn<FBuf>(ID_FanGetCtl)(_gpu, b) != 0) throw new NotSupportedException("Controle de ventoinha indisponível (GPUs RTX 20 ou mais novas)");
                int n = I(b, 8);
                for (int i = 0; i < n && i < 32; i++)
                {
                    int e = 44 + i * 44;
                    Marshal.WriteInt32(b, e + 4, level < 0 ? 0 : Math.Max(30, Math.Min(100, level))); // nunca abaixo de 30% no manual
                    Marshal.WriteInt32(b, e + 8, level < 0 ? 0 : 1);
                }
                int rc = Fn<FBuf>(ID_FanSetCtl)(_gpu, b);
                if (rc != 0) throw new InvalidOperationException("O driver recusou o controle da ventoinha (código " + rc + ")");
            }
            finally { Marshal.FreeHGlobal(b); }
            return Status();
        }

        public static object Reset()
        {
            Need();
            try { SetClocks(0, 0); } catch { }
            try
            {
                var st = (Dictionary<string, object>)Status();
                if (st.ContainsKey("power")) SetPower((int)((Dictionary<string, object>)st["power"])["def"]);
            }
            catch { }
            try { SetFan(-1); } catch { }
            return Status();
        }
    }
}
