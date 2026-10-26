using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Grobs
{
    /// <summary>
    /// Configurações do driver NVIDIA por jogo (as mesmas do Painel de Controle NVIDIA / NVIDIA Profile Inspector),
    /// gravadas pela API oficial de perfis (DRS). IDs e valores conferidos em NvApiDriverSettings.h.
    /// </summary>
    internal static class NvProfiles
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FOutHandle(out IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FHandle(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FFindApp(IntPtr s, IntPtr name, out IntPtr profile, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FFindProfile(IntPtr s, IntPtr name, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FCreateProfile(IntPtr s, IntPtr info, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FCreateApp(IntPtr s, IntPtr profile, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FSetSetting(IntPtr s, IntPtr profile, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FGetSetting(IntPtr s, IntPtr profile, uint id, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FRestoreSetting(IntPtr s, IntPtr profile, uint id);

        const uint CreateSession = 0x0694D52E, DestroySession = 0xDAD9CFF8, LoadSettings = 0x375DBD6B, SaveSettings = 0xFCBC7E14,
            FindApplicationByName = 0xEEE566B2, FindProfileByName = 0x7E4A9A0B, CreateProfile = 0xCC176068, CreateApplication = 0x4347A9DE,
            SetSetting = 0x577DD202, GetSetting = 0x73BF8338, RestoreProfileDefaultSetting = 0x53F0381E;

        // tamanhos das estruturas (NvAPI_UnicodeString = 2048 wchar)
        const int STR = 4096, SETTING_V1 = 12328, PROFILE_V1 = 4116, APP_V3 = 16396, APP_V4 = 20492;
        static uint Ver(int size, int v) => (uint)size | ((uint)v << 16);

        public sealed class Opt { public string Key, Title, Desc; public uint Id; public Dictionary<string, uint> Values; }

        /// <summary>Opções oferecidas por jogo. "default" = deixar o driver decidir (remove o ajuste).</summary>
        public static readonly List<Opt> Options = new List<Opt>
        {
            new Opt { Key = "power", Id = 0x1057EB71, Title = "Gerenciamento de energia", Desc = "“Desempenho máximo” evita quedas de clock no meio do jogo.",
                Values = new Dictionary<string, uint> { { "max", 1 }, { "optimal", 5 }, { "adaptive", 0 } } },
            new Opt { Key = "latency", Id = 0x007BA09E, Title = "Modo de baixa latência", Desc = "Menos quadros na fila = resposta mais rápida do mouse (jogos competitivos).",
                Values = new Dictionary<string, uint> { { "on", 1 }, { "off", 0 } } },
            new Opt { Key = "texture", Id = 0x00CE2691, Title = "Qualidade da filtragem de textura", Desc = "“Alto desempenho” ganha alguns FPS com perda visual mínima.",
                Values = new Dictionary<string, uint> { { "highperf", 0x14 }, { "perf", 0x0A }, { "quality", 0 }, { "highquality", 0xFFFFFFF6 } } },
            new Opt { Key = "vsync", Id = 0x00A879CF, Title = "Sincronização vertical (V-Sync)", Desc = "Desligar reduz input lag; ligar elimina tearing.",
                Values = new Dictionary<string, uint> { { "off", 0x08416747 }, { "on", 0x47814940 }, { "app", 0x60925292 } } },
            new Opt { Key = "fpslimit", Id = 0x10835002, Title = "Limite de FPS (driver)", Desc = "Limitar um pouco abaixo da taxa do monitor deixa o frametime estável.",
                Values = new Dictionary<string, uint> { { "off", 0 }, { "60", 60 }, { "75", 75 }, { "117", 117 }, { "141", 141 }, { "160", 160 }, { "237", 237 } } },
            new Opt { Key = "oglthreads", Id = 0x20C1221E, Title = "Otimização de threads (OpenGL)", Desc = "Usa mais núcleos da CPU em jogos OpenGL (ex.: Minecraft Java).",
                Values = new Dictionary<string, uint> { { "on", 1 }, { "off", 2 } } },
        };

        public static bool Available => GpuOc.Init();

        static IntPtr Alloc(int size) { var p = Marshal.AllocHGlobal(size); for (int i = 0; i < size; i += 4) Marshal.WriteInt32(p, i, 0); return p; }

        static IntPtr Str(string s)
        {
            var p = Alloc(STR);
            var bytes = Encoding.Unicode.GetBytes(s.Length > 2047 ? s.Substring(0, 2047) : s);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            return p;
        }

        static void Check(int rc, string what) { if (rc != 0) throw new InvalidOperationException(what + " (NVAPI " + rc + ")"); }

        static T F<T>(uint id) where T : class => GpuOc.Fn<T>(id);

        /// <summary>Abre uma sessão de perfis, executa e (opcionalmente) salva.</summary>
        static TResult Session<TResult>(Func<IntPtr, TResult> body, bool save)
        {
            if (!Available) throw new NotSupportedException("GPU NVIDIA não encontrada");
            IntPtr s;
            Check(F<FOutHandle>(CreateSession)(out s), "Falha ao abrir os perfis do driver");
            try
            {
                Check(F<FHandle>(LoadSettings)(s), "Falha ao carregar os perfis");
                var r = body(s);
                if (save) Check(F<FHandle>(SaveSettings)(s), "Falha ao salvar os perfis");
                return r;
            }
            finally { F<FHandle>(DestroySession)(s); }
        }

        /// <summary>Perfil do jogo: o perfil oficial que a NVIDIA já tem para o exe ou um perfil "GROBS - jogo.exe" criado na hora.</summary>
        static IntPtr ProfileFor(IntPtr s, string exe, bool create)
        {
            string name = Path.GetFileName(exe).ToLowerInvariant();
            IntPtr nm = Str(name);
            try
            {
                foreach (var v in new[] { 4, 3 })
                {
                    int size = v == 4 ? APP_V4 : APP_V3;
                    IntPtr app = Alloc(size);
                    try
                    {
                        Marshal.WriteInt32(app, 0, (int)Ver(size, v));
                        IntPtr prof;
                        int rc = F<FFindApp>(FindApplicationByName)(s, nm, out prof, app);
                        if (rc == 0) return prof;
                        if (rc == -9) continue; // versão de estrutura incompatível: tenta a anterior
                        break; // não encontrado
                    }
                    finally { Marshal.FreeHGlobal(app); }
                }
                if (!create) return IntPtr.Zero;

                string pname = "GROBS - " + name;
                IntPtr pn = Str(pname);
                try
                {
                    IntPtr prof;
                    if (F<FFindProfile>(FindProfileByName)(s, pn, out prof) != 0)
                    {
                        IntPtr info = Alloc(PROFILE_V1);
                        try
                        {
                            Marshal.WriteInt32(info, 0, (int)Ver(PROFILE_V1, 1));
                            var b = Encoding.Unicode.GetBytes(pname); Marshal.Copy(b, 0, info + 4, b.Length);
                            Check(F<FCreateProfile>(CreateProfile)(s, info, out prof), "Falha ao criar o perfil");
                        }
                        finally { Marshal.FreeHGlobal(info); }
                    }
                    foreach (var v in new[] { 4, 3 })
                    {
                        int size = v == 4 ? APP_V4 : APP_V3;
                        IntPtr app = Alloc(size);
                        try
                        {
                            Marshal.WriteInt32(app, 0, (int)Ver(size, v));
                            var b = Encoding.Unicode.GetBytes(name); Marshal.Copy(b, 0, app + 8, b.Length);           // appName
                            var f = Encoding.Unicode.GetBytes(Path.GetFileNameWithoutExtension(exe)); Marshal.Copy(f, 0, app + 8 + STR, f.Length); // nome amigável
                            int rc = F<FCreateApp>(CreateApplication)(s, prof, app);
                            if (rc == 0) return prof;
                            if (rc != -9) Check(rc, "Falha ao associar o jogo ao perfil");
                        }
                        finally { Marshal.FreeHGlobal(app); }
                    }
                    throw new InvalidOperationException("Versão do driver incompatível");
                }
                finally { Marshal.FreeHGlobal(pn); }
            }
            finally { Marshal.FreeHGlobal(nm); }
        }

        /// <summary>Valores atuais (chave da opção -> nome do valor, ou "default").</summary>
        public static Dictionary<string, object> Read(string exe)
        {
            return Session(s =>
            {
                var r = new Dictionary<string, object>();
                IntPtr prof = ProfileFor(s, exe, false);
                foreach (var o in Options)
                {
                    string cur = "default";
                    if (prof != IntPtr.Zero)
                    {
                        IntPtr st = Alloc(SETTING_V1);
                        try
                        {
                            Marshal.WriteInt32(st, 0, (int)Ver(SETTING_V1, 1));
                            // settingLocation (offset 4108): 0 = definido no próprio perfil do jogo
                            if (F<FGetSetting>(GetSetting)(s, prof, o.Id, st) == 0 && Marshal.ReadInt32(st, 4108) == 0)
                            {
                                uint val = (uint)Marshal.ReadInt32(st, 8224);
                                cur = o.Values.FirstOrDefault(kv => kv.Value == val).Key ?? ("0x" + val.ToString("X"));
                            }
                        }
                        finally { Marshal.FreeHGlobal(st); }
                    }
                    r[o.Key] = cur;
                }
                return r;
            }, false);
        }

        /// <summary>Aplica as escolhas; "default" remove o ajuste e volta ao padrão do driver.</summary>
        public static Dictionary<string, object> Apply(string exe, Dictionary<string, object> choices)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            Session(s =>
            {
                IntPtr prof = ProfileFor(s, exe, true);
                foreach (var o in Options)
                {
                    object v; if (!choices.TryGetValue(o.Key, out v)) continue;
                    string key = Convert.ToString(v);
                    if (key == "default") { F<FRestoreSetting>(RestoreProfileDefaultSetting)(s, prof, o.Id); continue; }
                    uint val; if (!o.Values.TryGetValue(key, out val)) continue;
                    IntPtr st = Alloc(SETTING_V1);
                    try
                    {
                        Marshal.WriteInt32(st, 0, (int)Ver(SETTING_V1, 1));
                        Marshal.WriteInt32(st, 4100, (int)o.Id);   // settingId
                        Marshal.WriteInt32(st, 4104, 0);           // NVDRS_DWORD_TYPE
                        Marshal.WriteInt32(st, 8224, (int)val);    // u32CurrentValue
                        Check(F<FSetSetting>(SetSetting)(s, prof, st), "O driver recusou “" + o.Title + "”");
                    }
                    finally { Marshal.FreeHGlobal(st); }
                }
                return true;
            }, true);
            return Read(exe);
        }
    }
}
