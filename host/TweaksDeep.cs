using System.Collections.Generic;
using Microsoft.Win32;

namespace Grobs
{
    internal static partial class Tweaks
    {
        const string Blocked = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        static Val Block(string clsid) => V(LM, Blocked, clsid, "", null, RegistryValueKind.String);

        // ---------------- Debloat profundo: Explorador, menu do clique direito, Iniciar e espaço do sistema ----------------
        static readonly List<Tweak> Deep = new List<Tweak>
        {
            new Tweak { Id = "exp_home", Group = "Explorador e menu", Title = "Tirar \"Início\" e \"Galeria\" do Explorador", MinBuild = 22000, Admin = true, Explorer = true,
                Desc = "O painel lateral começa em Este Computador; some o atalho de arquivos recentes e a Galeria de fotos.",
                Values = new[] { V(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "HubMode", 1, null),
                                 V(CU, @"Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}", "System.IsPinnedToNameSpaceTree", 0, null) } },
            new Tweak { Id = "exp_recent", Group = "Explorador e menu", Title = "Sem arquivos e pastas recentes no Acesso rápido", Explorer = true,
                Desc = "O Explorador deixa de listar o que você abriu por último (mais privacidade, abre mais rápido).",
                Values = new[] { V(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 0, null), V(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", 0, null) } },
            new Tweak { Id = "menu_share", Group = "Explorador e menu", Title = "Clique direito sem \"Compartilhar\"", Admin = true, Explorer = true,
                Desc = "Remove o item de compartilhamento moderno do menu de contexto.", Values = new[] { Block("{e2bf9676-5f8f-435c-97eb-11607a5bedf7}") } },
            new Tweak { Id = "menu_access", Group = "Explorador e menu", Title = "Clique direito sem \"Conceder acesso a\"", Admin = true, Explorer = true, Risk = "medium",
                Desc = "Remove o atalho de compartilhamento de pastas na rede (o compartilhamento continua pelas Propriedades).", Values = new[] { Block("{f81e9010-6ea4-11ce-a7ff-003048d6d2f7}") } },
            new Tweak { Id = "menu_cast", Group = "Explorador e menu", Title = "Clique direito sem \"Transmitir para dispositivo\"", Admin = true, Explorer = true,
                Desc = "Remove o envio de mídia para TVs pelo menu.", Values = new[] { Block("{7AD84985-87B4-4a16-BE58-8B72A5B390F7}") } },
            new Tweak { Id = "menu_prev", Group = "Explorador e menu", Title = "Clique direito sem \"Restaurar versões anteriores\"", Admin = true, Explorer = true, Risk = "medium",
                Desc = "Remove a aba e o item de versões anteriores (o Histórico de Arquivos continua funcionando).", Values = new[] { Block("{596AB062-B4D2-4215-9F74-E9109B0A8153}") } },
            new Tweak { Id = "menu_compat", Group = "Explorador e menu", Title = "Clique direito sem \"Solucionar problemas de compatibilidade\"", Admin = true, Explorer = true,
                Desc = "Item quase nunca usado nos executáveis.", Values = new[] { Block("{1d27f844-3a1f-4410-85ac-14651078412d}") } },
            new Tweak { Id = "menu_library", Group = "Explorador e menu", Title = "Clique direito sem \"Incluir na biblioteca\"", Admin = true, Explorer = true,
                Desc = "Bibliotecas do Windows são pouco usadas.", Values = new[] { Block("{3dad6c5d-2167-4cae-9914-f99e41c12cfa}") } },
            new Tweak { Id = "start_reco", Group = "Explorador e menu", Title = "Iniciar sem recomendações e sem arquivos recentes", MinBuild = 22000, Explorer = true,
                Desc = "Some a área \"Recomendado\" com sugestões de apps e arquivos, e os avisos da conta Microsoft no Iniciar.",
                Values = new[] { V(CU, Adv, "Start_IrisRecommendations", 0, null), V(CU, Adv, "Start_TrackDocs", 0, null), V(CU, Adv, "Start_AccountNotifications", 0, null) } },

            new Tweak { Id = "space_reserved", Group = "Liberar espaço do sistema", Title = "Desligar o Armazenamento Reservado (~7 GB)", MinBuild = 18362, Admin = true, Risk = "medium",
                Desc = "O Windows guarda cerca de 7 GB para atualizações. Liberando, as grandes atualizações podem pedir espaço livre na hora.",
                Values = new[] { V(LM, @"SOFTWARE\GROBS\Tweaks", "ReservedStorageOff", 1, null) },
                After = on => Power.Run(Power.Sys("dism.exe"), "/Online /Set-ReservedStorageState /State:" + (on ? "Disabled" : "Enabled"), 300000) },
        };
    }
}
