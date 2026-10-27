using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace Grobs
{
    internal static partial class Tweaks
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);

        static List<Tweak> _all;
        static List<Tweak> All => _all ?? (_all = Base.Concat(Features).Concat(Looks).Concat(More).ToList());

        static readonly int Build = ReadBuild();
        static int ReadBuild()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                int b; return int.TryParse(Convert.ToString(k?.GetValue("CurrentBuildNumber")), out b) ? b : 0;
            }
        }

        /// <summary>groups: filtra por grupos (vazio = todos). Só lista ajustes que existem nesta versão do Windows.</summary>
        public static object List(string[] groups = null)
        {
            bool admin = Program.IsAdmin;
            return All.Where(t => Build >= t.MinBuild && (groups == null || groups.Length == 0 || groups.Contains(t.Group)))
                .Select(t => (object)new Dictionary<string, object> {
                    { "id", t.Id }, { "group", t.Group }, { "title", t.Title }, { "desc", t.Desc }, { "risk", t.Risk },
                    { "admin", t.Admin }, { "locked", t.Admin && !admin }, { "restart", t.Restart }, { "explorer", t.Explorer },
                    { "on", t.Values.All(Matches) } }).ToList();
        }

        static bool Matches(Val v)
        {
            using (var root = Root(v.Hive))
            using (var k = root.OpenSubKey(v.Path))
            {
                if (v.KeyOnly) return k != null;
                var cur = k?.GetValue(v.Name);
                if (cur == null) return v.MissingOn;
                return Convert.ToString(cur) == Convert.ToString(v.On);
            }
        }

        public static object Set(string id, bool on)
        {
            var t = All.First(x => x.Id == id);
            if (t.Admin && !Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            if (on && t.Risk != "low") RestorePoints.Ensure(); // ponto de restauração antes do primeiro ajuste de risco da sessão
            foreach (var v in t.Values)
            {
                using (var root = Root(v.Hive))
                {
                    if (v.KeyOnly)
                    {
                        if (on) using (var k = root.CreateSubKey(v.Path)) k.SetValue("", "");
                        else root.DeleteSubKeyTree(v.DeleteTree ?? v.Path, false);
                    }
                    else if (on || v.Off != null)
                        using (var k = root.CreateSubKey(v.Path)) k.SetValue(v.Name, on ? v.On : v.Off, v.Kind);
                    else
                        using (var k = root.OpenSubKey(v.Path, true)) k?.DeleteValue(v.Name, false);
                }
            }
            t.After?.Invoke(on);
            // WM_SETTINGCHANGE com "ImmersiveColorSet" faz o tema claro/escuro e a cor de destaque mudarem na hora
            IntPtr res;
            SendMessageTimeout(Native.HWND_BROADCAST, 0x001A, IntPtr.Zero, "ImmersiveColorSet", 0x0002, 1000, out res);
            Native.PostMessage(Native.HWND_BROADCAST, 0x001A, IntPtr.Zero, IntPtr.Zero);
            return t.Values.All(Matches) == on;
        }

        const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string CDM = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

        // ---------------- Recursos do Windows (estilo hellzerg/optimizer e WinUtil) ----------------
        static readonly List<Tweak> Features = new List<Tweak>
        {
            new Tweak { Id = "copilot", Group = "IA e assistentes", Title = "Desativar o Copilot", MinBuild = 19045, Explorer = true,
                Desc = "Remove o Copilot da barra de tarefas e bloqueia por política.",
                Values = new[] { V(CU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null), V(CU, Adv, "ShowCopilotButton", 0, null) } },
            new Tweak { Id = "recall", Group = "IA e assistentes", Title = "Desativar o Recall e o Click to Do", MinBuild = 26100, Admin = true,
                Desc = "Impede o Windows de tirar capturas periódicas da tela para a busca por IA.",
                Values = new[] { V(CU, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null),
                                 V(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null),
                                 V(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0, null),
                                 V(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "TurnOffSavingSnapshots", 1, null),
                                 V(CU, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1, null) } },
            new Tweak { Id = "websearch", Group = "IA e assistentes", Title = "Sem resultados da web na pesquisa do Windows", Admin = true,
                Desc = "A pesquisa do Iniciar procura só no PC (sem Bing).",
                Values = new[] { V(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", 1, null),
                                 V(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 0, null),
                                 V(CU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0, null) } },
            new Tweak { Id = "cortana", Group = "IA e assistentes", Title = "Desativar a Cortana", Admin = true,
                Desc = "Bloqueia a assistente por política (Windows 10).",
                Values = new[] { V(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0, null) } },
            new Tweak { Id = "widgets", Group = "Barra de tarefas", Title = "Desativar Widgets / Notícias e Interesses", Admin = true, Explorer = true,
                Desc = "Some o painel de notícias e clima que consome RAM e internet.",
                Values = new[] { V(LM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0, null), V(CU, @"Software\Microsoft\Windows\CurrentVersion\Feeds", "ShellFeedsTaskbarViewMode", 2, null) } },
            new Tweak { Id = "chaticon", Group = "Barra de tarefas", Title = "Remover ícone do Chat/Teams", MinBuild = 22000, Explorer = true,
                Desc = "Tira o atalho do Teams pessoal da barra de tarefas.",
                Values = new[] { V(CU, Adv, "TaskbarMn", 0, null) } },
            new Tweak { Id = "taskview", Group = "Barra de tarefas", Title = "Ocultar botão Visão de Tarefas", Explorer = true,
                Desc = "O atalho Win+Tab continua funcionando.",
                Values = new[] { V(CU, Adv, "ShowTaskViewButton", 0, null) } },
            new Tweak { Id = "searchbox", Group = "Barra de tarefas", Title = "Ocultar caixa de pesquisa", Explorer = true,
                Desc = "Libera espaço na barra; a busca abre ao digitar no Iniciar.",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 0, null) } },
            new Tweak { Id = "consumer", Group = "Anúncios e sugestões", Title = "Bloquear apps promocionais automáticos", Admin = true,
                Desc = "Impede a instalação automática de jogos e apps patrocinados (Candy Crush etc.).",
                Values = new[] { V(LM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1, null) } },
            new Tweak { Id = "lockads", Group = "Anúncios e sugestões", Title = "Sem dicas e anúncios na tela de bloqueio",
                Desc = "Remove “curiosidades” e propagandas da tela de bloqueio.",
                Values = new[] { V(CU, CDM, "RotatingLockScreenOverlayEnabled", 0, null), V(CU, CDM, "SubscribedContent-338387Enabled", 0, null) } },
            new Tweak { Id = "welcome", Group = "Anúncios e sugestões", Title = "Sem tela de “boas-vindas” após atualizações",
                Desc = "Evita as telas de “vamos terminar de configurar” e dicas do Windows.",
                Values = new[] { V(CU, CDM, "SubscribedContent-310093Enabled", 0, null), V(CU, @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, null) } },
            new Tweak { Id = "tips", Group = "Anúncios e sugestões", Title = "Desativar dicas e truques do Windows",
                Desc = "Notificações de dicas e sugestões de uso.",
                Values = new[] { V(CU, CDM, "SubscribedContent-338393Enabled", 0, null), V(CU, CDM, "SubscribedContent-353694Enabled", 0, null), V(CU, CDM, "SubscribedContent-353696Enabled", 0, null) } },
            new Tweak { Id = "edgeboost", Group = "Sistema", Title = "Edge sem rodar em segundo plano", Admin = true,
                Desc = "Desliga o “Aumento de inicialização” e a execução em segundo plano do Edge.",
                Values = new[] { V(LM, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0, null), V(LM, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0, null) } },
            new Tweak { Id = "onedrive", Group = "Sistema", Title = "Desativar o OneDrive", Admin = true, Risk = "medium",
                Desc = "Impede o OneDrive de sincronizar. Seus arquivos na nuvem continuam no site.",
                Values = new[] { V(LM, @"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", 1, null) } },
            new Tweak { Id = "p2p", Group = "Sistema", Title = "Não compartilhar atualizações com outros PCs", Admin = true,
                Desc = "Otimização de Entrega só pela Microsoft: não usa sua internet para enviar updates a outras pessoas.",
                Values = new[] { V(LM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0, null) } },
            new Tweak { Id = "werdisable", Group = "Sistema", Title = "Desativar Relatório de Erros", Admin = true,
                Desc = "O Windows deixa de gerar e enviar relatórios de falha.",
                Values = new[] { V(LM, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1, null) } },
            new Tweak { Id = "stickykeys", Group = "Sistema", Title = "Sem aviso de Teclas de Aderência", MinBuild = 0,
                Desc = "Apertar Shift 5 vezes (comum em jogos) não abre mais o aviso.",
                Values = new[] { V(CU, @"Control Panel\Accessibility\StickyKeys", "Flags", "506", "510", RegistryValueKind.String) } },
            new Tweak { Id = "ink", Group = "Sistema", Title = "Desativar Windows Ink Workspace", Admin = true,
                Desc = "Recurso de caneta; inútil sem tela touch.",
                Values = new[] { V(LM, @"SOFTWARE\Policies\Microsoft\WindowsInkWorkspace", "AllowWindowsInkWorkspace", 0, null) } },
            new Tweak { Id = "gamebar", Group = "Sistema", Title = "Desativar a Xbox Game Bar (Win+G)",
                Desc = "Também evita o aviso “você precisa de um app para abrir este link ms-gamingoverlay”.",
                Values = new[] { V(CU, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0, null), V(CU, @"System\GameConfigStore", "GameDVR_Enabled", 0, 1) } },
        };

        // ---------------- Embelezar ----------------
        static readonly List<Tweak> Looks = new List<Tweak>
        {
            new Tweak { Id = "darkapps", Group = "Tema", Title = "Modo escuro nos aplicativos", MinBuild = 17763,
                Desc = "Configurações, Explorer e apps compatíveis em tema escuro.",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0, 1) } },
            new Tweak { Id = "darksystem", Group = "Tema", Title = "Modo escuro no Windows", MinBuild = 18362, Explorer = true,
                Desc = "Barra de tarefas, Iniciar e central de ações escuros.",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0, 1) } },
            new Tweak { Id = "transparencyon", Group = "Tema", Title = "Efeitos de transparência (Mica/Acrylic)",
                Desc = "Vidro fosco na barra de tarefas, Iniciar e janelas.",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1, 0) } },
            new Tweak { Id = "accenttaskbar", Group = "Tema", Title = "Cor de destaque no Iniciar e barra de tarefas",
                Desc = "Usa a cor de destaque escolhida (precisa do modo escuro do Windows).",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "ColorPrevalence", 1, 0) } },
            new Tweak { Id = "accenttitle", Group = "Tema", Title = "Cor de destaque nas barras de título",
                Desc = "Bordas e títulos das janelas coloridos.",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\DWM", "ColorPrevalence", 1, 0) } },
            new Tweak { Id = "taskbarleft", Group = "Barra de tarefas e Iniciar", Title = "Iniciar e ícones à esquerda", MinBuild = 22000, Explorer = true,
                Desc = "Alinha a barra de tarefas do Windows 11 como no Windows 10.",
                Values = new[] { V(CU, Adv, "TaskbarAl", 0, 1) } },
            new Tweak { Id = "seconds", Group = "Barra de tarefas e Iniciar", Title = "Mostrar segundos no relógio", Explorer = true,
                Desc = "Relógio da barra de tarefas com segundos.",
                Values = new[] { V(CU, Adv, "ShowSecondsInSystemClock", 1, null) } },
            new Tweak { Id = "endtask", Group = "Barra de tarefas e Iniciar", Title = "“Finalizar tarefa” no clique direito da barra", MinBuild = 22631,
                Desc = "Fecha um app travado direto pela barra de tarefas (Windows 11 23H2+).",
                Values = new[] { V(CU, Adv + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1, null) } },
            new Tweak { Id = "classicmenu", Group = "Explorador de Arquivos", Title = "Menu de contexto clássico (completo)", MinBuild = 22000, Explorer = true,
                Desc = "Volta o menu do botão direito completo, sem o “Mostrar mais opções”.",
                Values = new[] { new Val { Hive = CU, Path = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", KeyOnly = true, DeleteTree = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}" } } },
            new Tweak { Id = "extensions", Group = "Explorador de Arquivos", Title = "Mostrar extensões de arquivo", MinBuild = 0, Explorer = true,
                Desc = "Exibe .exe, .pdf, .jpg — também ajuda a identificar arquivos falsos.",
                Values = new[] { V(CU, Adv, "HideFileExt", 0, 1) } },
            new Tweak { Id = "hidden", Group = "Explorador de Arquivos", Title = "Mostrar arquivos ocultos", MinBuild = 0, Explorer = true,
                Desc = "", Values = new[] { V(CU, Adv, "Hidden", 1, 2) } },
            new Tweak { Id = "thispc", Group = "Explorador de Arquivos", Title = "Explorador abre em “Este Computador”", MinBuild = 0,
                Desc = "Em vez de Acesso rápido/Início.", Values = new[] { V(CU, Adv, "LaunchTo", 1, null) } },
            new Tweak { Id = "compactview", Group = "Explorador de Arquivos", Title = "Modo compacto no Explorador", MinBuild = 22000, Explorer = true,
                Desc = "Menos espaço entre os itens (Windows 11).", Values = new[] { V(CU, Adv, "UseCompactMode", 1, 0) } },
            new Tweak { Id = "desktopicons", Group = "Área de trabalho", Title = "Ícone “Este Computador” na área de trabalho", MinBuild = 0, Explorer = true,
                Desc = "", Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0, 1) } },
        };
    }
}
