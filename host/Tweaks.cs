using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>Ajustes reversíveis e documentados. Cada um lê o estado real do registro.</summary>
    internal static partial class Tweaks
    {
        sealed class Val
        {
            public string Hive, Path, Name; public object On, Off; // Off == null => apagar o valor (padrão do Windows)
            public RegistryValueKind Kind = RegistryValueKind.DWord;
            public bool MissingOn; // valor ausente equivale ao ajuste ativo (padrão do Windows)
            public bool KeyOnly; public string DeleteTree; // ajuste = existência da chave (ex.: menu de contexto clássico)
        }

        sealed class Tweak
        {
            public string Id, Group, Title, Desc, Risk = "low"; public bool Admin, Restart, Explorer;
            public int MinBuild = 10240; // 0 = também no Windows 7/8; 22000 = só Windows 11
            public Val[] Values; public Action<bool> After;
        }

        static Val V(string hive, string path, string name, object on, object off, RegistryValueKind kind = RegistryValueKind.DWord)
            => new Val { Hive = hive, Path = path, Name = name, On = on, Off = off, Kind = kind };

        const string CU = "HKCU", LM = "HKLM";

        static readonly List<Tweak> Base = new List<Tweak>
        {
            // ---------------- Desempenho ----------------
            new Tweak { Id = "gamemode", Group = "Desempenho", Title = "Modo de Jogo do Windows",
                Desc = "Prioriza o jogo em primeiro plano e evita que o Windows Update instale drivers durante a partida.",
                Values = new[] { new Val { Hive = CU, Path = @"Software\Microsoft\GameBar", Name = "AutoGameModeEnabled", On = 1, Off = 0, MissingOn = true } } },
            new Tweak { Id = "gamedvr", Group = "Desempenho", Title = "Desativar gravação em segundo plano (Game DVR)",
                Desc = "A Xbox Game Bar deixa de gravar a tela continuamente, liberando GPU e disco em jogos.",
                Values = new[] { V(CU, @"System\GameConfigStore", "GameDVR_Enabled", 0, 1), V(CU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, 1) } },
            new Tweak { Id = "hags", Group = "Desempenho", Title = "Agendamento de GPU acelerado por hardware",
                Desc = "Reduz latência em GPUs compatíveis (WDDM 2.7+). Requer reiniciar.", Admin = true, Restart = true, Risk = "medium",
                Values = new[] { V(LM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, 1) } },
            new Tweak { Id = "transparency", Group = "Desempenho", Title = "Desativar efeitos de transparência",
                Desc = "Menos trabalho para a GPU integrada em PCs mais fracos.",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, 1) } },
            new Tweak { Id = "animations", MinBuild = 0, Group = "Desempenho", Title = "Desativar animações de janelas",
                Desc = "Janelas abrem e minimizam instantaneamente.",
                Values = new[] { V(CU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", "1", RegistryValueKind.String) },
                After = on => { var ai = new Native.ANIMATIONINFO { cbSize = 8, iMinAnimate = on ? 0 : 1 }; Native.SystemParametersInfo(0x0049, 8, ref ai, 3); } },
            new Tweak { Id = "menudelay", MinBuild = 0, Group = "Desempenho", Title = "Menus mais rápidos",
                Desc = "Reduz o atraso de abertura dos submenus de 400 ms para 100 ms.",
                Values = new[] { V(CU, @"Control Panel\Desktop", "MenuShowDelay", "100", "400", RegistryValueKind.String) },
                After = on => Native.SystemParametersInfo(0x006B, on ? 100u : 400u, IntPtr.Zero, 3) },
            new Tweak { Id = "mouseaccel", MinBuild = 0, Group = "Desempenho", Title = "Desativar aceleração do mouse",
                Desc = "Movimento 1:1 (desliga \"Aumentar precisão do ponteiro\"). Preferido por jogadores.",
                Values = new[] { V(CU, @"Control Panel\Mouse", "MouseSpeed", "0", "1", RegistryValueKind.String),
                                 V(CU, @"Control Panel\Mouse", "MouseThreshold1", "0", "6", RegistryValueKind.String),
                                 V(CU, @"Control Panel\Mouse", "MouseThreshold2", "0", "10", RegistryValueKind.String) },
                After = on => Native.SystemParametersInfo(0x0004, 0, on ? new[] { 0, 0, 0 } : new[] { 6, 10, 1 }, 3) },
            new Tweak { Id = "bgapps", Group = "Desempenho", Title = "Bloquear apps da Store em segundo plano",
                Desc = "Apps da Microsoft Store não rodam quando fechados (Windows 10).",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, 0) } },
            new Tweak { Id = "storagesense", Group = "Desempenho", Title = "Ativar Sensor de Armazenamento",
                Desc = "O próprio Windows limpa temporários e a Lixeira automaticamente.",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 1, 0) } },

            // ---------------- Privacidade ----------------
            new Tweak { Id = "telemetry", Group = "Privacidade", Title = "Telemetria no mínimo",
                Desc = "Envia apenas dados de diagnóstico obrigatórios e para o serviço DiagTrack.", Admin = true, Risk = "medium",
                Values = new[] { V(LM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, null),
                                 V(LM, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 4, 2) } },
            new Tweak { Id = "adid", Group = "Privacidade", Title = "Desativar ID de publicidade",
                Desc = "Apps deixam de usar seu ID para anúncios personalizados.",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1) } },
            new Tweak { Id = "tailored", Group = "Privacidade", Title = "Desativar experiências personalizadas",
                Desc = "A Microsoft não usa dados de diagnóstico para dicas e anúncios.",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1) } },
            new Tweak { Id = "suggestions", Group = "Privacidade", Title = "Remover sugestões e anúncios do Iniciar",
                Desc = "Sem apps sugeridos, dicas e instalações silenciosas de apps promocionais.",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0, 1),
                                 V(CU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0, 1),
                                 V(CU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0, 1),
                                 V(CU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled", 0, 1),
                                 V(CU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0, 1) } },
            new Tweak { Id = "bing", Group = "Privacidade", Title = "Desativar pesquisa Bing no menu Iniciar",
                Desc = "A busca do Iniciar procura só no PC — mais rápida e privada.",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0, null),
                                 V(CU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, null) } },
            new Tweak { Id = "activity", Group = "Privacidade", Title = "Desativar histórico de atividades",
                Desc = "O Windows não registra nem envia os apps e arquivos que você usa.", Admin = true,
                Values = new[] { V(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0, null),
                                 V(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0, null) } },
            new Tweak { Id = "feedback", Group = "Privacidade", Title = "Não pedir feedback",
                Desc = "Remove as notificações de pesquisa de satisfação do Windows.",
                Values = new[] { V(CU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, null) } },
        };

        static RegistryKey Root(string hive) => RegistryKey.OpenBaseKey(hive == LM ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
            Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);



    }
}
