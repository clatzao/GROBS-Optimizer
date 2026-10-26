using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>Os mesmos itens de "Opções de desempenho > Efeitos visuais" do Windows, aplicados ao vivo.</summary>
    internal static class VisualFx
    {
        sealed class Fx
        {
            public string Id, Title, Desc; public int Weight; // peso: impacto em PCs fracos (1..3)
            public uint SpiGet, SpiSet; public bool ViaUiParam;
            public string RegPath, RegName; public object RegOn, RegOff; public RegistryValueKind Kind = RegistryValueKind.DWord;
            public bool KeepInPerformance; // o preset "desempenho" mantém ligado (ex.: suavização de fontes)
        }

        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint ui, ref int pv, uint ini);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint ui, IntPtr pv, uint ini);

        const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        static readonly List<Fx> All = new List<Fx>
        {
            new Fx { Id = "minanimate", Title = "Animar janelas ao minimizar e maximizar", Desc = "Efeito de “voar” para a barra de tarefas.", Weight = 2 },
            new Fx { Id = "taskbaranim", Title = "Animações na barra de tarefas", Desc = "Ícones deslizando e piscando.", Weight = 1, RegPath = Adv, RegName = "TaskbarAnimations", RegOn = 1, RegOff = 0 },
            new Fx { Id = "clientanim", Title = "Animar controles dentro das janelas", Desc = "Botões e barras de rolagem com transição.", Weight = 2, SpiGet = 0x1042, SpiSet = 0x1043 },
            new Fx { Id = "menuanim", Title = "Esmaecer ou deslizar menus", Desc = "Menus aparecem com animação.", Weight = 1, SpiGet = 0x1002, SpiSet = 0x1003 },
            new Fx { Id = "menufade", Title = "Esmaecer itens de menu após o clique", Desc = "", Weight = 1, SpiGet = 0x1014, SpiSet = 0x1015 },
            new Fx { Id = "tooltip", Title = "Esmaecer dicas de ferramenta", Desc = "Balões de ajuda com fade.", Weight = 1, SpiGet = 0x1016, SpiSet = 0x1017 },
            new Fx { Id = "combo", Title = "Deslizar caixas de combinação", Desc = "Listas suspensas que deslizam ao abrir.", Weight = 1, SpiGet = 0x1004, SpiSet = 0x1005 },
            new Fx { Id = "smoothscroll", Title = "Rolagem suave de listas", Desc = "", Weight = 1, SpiGet = 0x1006, SpiSet = 0x1007 },
            new Fx { Id = "dropshadow", Title = "Sombras sob as janelas", Desc = "Sombra projetada atrás de cada janela.", Weight = 2, SpiGet = 0x1024, SpiSet = 0x1025 },
            new Fx { Id = "cursorshadow", Title = "Sombra sob o ponteiro do mouse", Desc = "", Weight = 1, SpiGet = 0x101A, SpiSet = 0x101B },
            new Fx { Id = "dragfull", Title = "Mostrar conteúdo da janela ao arrastar", Desc = "Desligado: arrasta só o contorno (bem mais leve em PCs fracos).", Weight = 3, SpiGet = 0x0026, SpiSet = 0x0025, ViaUiParam = true },
            new Fx { Id = "peek", Title = "Espiar a área de trabalho (Aero Peek)", Desc = "Pré-visualização ao passar o mouse no canto da barra.", Weight = 2, RegPath = @"Software\Microsoft\Windows\DWM", RegName = "EnableAeroPeek", RegOn = 1, RegOff = 0 },
            new Fx { Id = "thumbnails", Title = "Miniaturas em vez de ícones", Desc = "Desligado: o Explorer não gera prévias de fotos e vídeos (pastas abrem mais rápido).", Weight = 2, RegPath = Adv, RegName = "IconsOnly", RegOn = 0, RegOff = 1 },
            new Fx { Id = "alphaselect", Title = "Retângulo de seleção translúcido", Desc = "", Weight = 1, RegPath = Adv, RegName = "ListviewAlphaSelect", RegOn = 1, RegOff = 0 },
            new Fx { Id = "iconshadow", Title = "Sombra nos nomes dos ícones da área de trabalho", Desc = "", Weight = 1, RegPath = Adv, RegName = "ListviewShadow", RegOn = 1, RegOff = 0 },
            new Fx { Id = "transparency", Title = "Efeitos de transparência (Mica/Acrylic)", Desc = "Barra de tarefas e menus translúcidos.", Weight = 3, RegPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", RegName = "EnableTransparency", RegOn = 1, RegOff = 0 },
            new Fx { Id = "fontsmooth", Title = "Suavizar bordas das fontes (ClearType)", Desc = "Recomendado manter: sem isso o texto fica serrilhado.", Weight = 0, SpiGet = 0x004A, SpiSet = 0x004B, ViaUiParam = true, KeepInPerformance = true },
        };

        static RegistryKey Cu() => Registry.CurrentUser;

        static bool Get(Fx f)
        {
            if (f.Id == "minanimate")
            {
                var ai = new Native.ANIMATIONINFO { cbSize = 8 };
                Native.SystemParametersInfo(0x0048, 8, ref ai, 0);
                return ai.iMinAnimate != 0;
            }
            if (f.RegPath != null)
                using (var k = Cu().OpenSubKey(f.RegPath))
                {
                    var v = k?.GetValue(f.RegName);
                    if (v == null) return true; // ausente = padrão do Windows (ligado)
                    return Convert.ToString(v) == Convert.ToString(f.RegOn);
                }
            int val = 0;
            SystemParametersInfo(f.SpiGet, 0, ref val, 0);
            return val != 0;
        }

        static void Set(Fx f, bool on)
        {
            const uint ini = 3; // SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
            if (f.Id == "minanimate")
            {
                var ai = new Native.ANIMATIONINFO { cbSize = 8, iMinAnimate = on ? 1 : 0 };
                Native.SystemParametersInfo(0x0049, 8, ref ai, ini);
                return;
            }
            if (f.RegPath != null)
            {
                using (var k = Cu().CreateSubKey(f.RegPath)) k.SetValue(f.RegName, on ? f.RegOn : f.RegOff, f.Kind);
                return;
            }
            if (f.ViaUiParam) SystemParametersInfo(f.SpiSet, on ? 1u : 0u, IntPtr.Zero, ini);
            else SystemParametersInfo(f.SpiSet, 0, on ? new IntPtr(1) : IntPtr.Zero, ini);
        }

        static void Notify()
        {
            // "Personalizado" no painel do Windows + avisa o Explorer
            using (var k = Cu().CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects")) k.SetValue("VisualFXSetting", 3, RegistryValueKind.DWord);
            Native.PostMessage(Native.HWND_BROADCAST, 0x001A, IntPtr.Zero, IntPtr.Zero);
        }

        public static object List() => All.Select(f => (object)new Dictionary<string, object> {
            { "id", f.Id }, { "title", f.Title }, { "desc", f.Desc }, { "weight", f.Weight }, { "on", Get(f) }, { "keep", f.KeepInPerformance } }).ToList();

        public static object SetOne(string id, bool on) { Set(All.First(f => f.Id == id), on); Notify(); return List(); }

        /// <summary>performance = sem "beleza"; balanced = tira só os pesados; beauty = tudo ligado (padrão do Windows).</summary>
        public static object Preset(string preset)
        {
            foreach (var f in All)
            {
                bool on = preset == "beauty" || f.KeepInPerformance || (preset == "balanced" && f.Weight <= 1 && f.Id != "menufade");
                if (preset == "balanced" && (f.Id == "thumbnails" || f.Id == "dragfull" || f.Id == "fontsmooth")) on = true;
                if (Get(f) != on) Set(f, on);
            }
            Notify();
            return List();
        }
    }
}
