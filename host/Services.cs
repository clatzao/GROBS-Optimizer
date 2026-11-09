using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>Serviços da Microsoft dispensáveis para a maioria dos usuários. Desativar = parar + tipo "Desabilitado"; restaurar = tipo padrão do Windows.</summary>
    internal static class Services
    {
        // Keep: quando o perfil automático deve manter o serviço (printer, bluetooth, biometric, laptop, xbox, scanner, smartcard, search)
        internal sealed class Svc { public string Name, Title, Desc, Group, Risk = "low", Default = "manual", Keep; }

        static Svc S(string name, string group, string title, string desc, string def = "manual", string risk = "low", string keep = null)
            => new Svc { Name = name, Group = group, Title = title, Desc = desc, Default = def, Risk = risk, Keep = keep };

        internal static IEnumerable<Svc> Catalog => List_;

        static readonly List<Svc> List_ = new List<Svc>
        {
            S("DiagTrack", "Telemetria", "Experiências do usuário conectado e telemetria", "Envia dados de uso e diagnóstico para a Microsoft.", "auto"),
            S("dmwappushservice", "Telemetria", "Roteamento de mensagens WAP Push", "Usado pela telemetria; dispensável fora de empresas."),
            S("diagnosticshub.standardcollector.service", "Telemetria", "Coletor do Hub de Diagnóstico", "Coleta rastreamentos para o Visual Studio e diagnóstico."),
            S("WerSvc", "Telemetria", "Relatório de Erros do Windows", "Envia relatórios de falhas para a Microsoft."),
            S("RetailDemo", "Inúteis", "Serviço de Demonstração de Varejo", "Só serve para PCs de vitrine em lojas."),
            S("MapsBroker", "Inúteis", "Gerenciador de Mapas Baixados", "Atualiza mapas offline do app Mapas.", "auto"),
            S("lfsvc", "Inúteis", "Serviço de Geolocalização", "Localização para apps. Desative se não usa Mapas/Clima por GPS.", "manual", "medium"),
            S("Fax", "Inúteis", "Fax", "Envio e recebimento de fax."),
            S("WMPNetworkSvc", "Inúteis", "Compartilhamento de rede do Windows Media Player", "Compartilha bibliotecas do WMP na rede."),
            S("RemoteRegistry", "Inúteis", "Registro Remoto", "Permite editar o registro pela rede — também é um risco de segurança.", "disabled"),
            S("PhoneSvc", "Inúteis", "Serviço de Telefonia", "Chamadas pelo app Seu Telefone/Vincular ao Celular."),
            S("WpcMonSvc", "Inúteis", "Controle dos Pais", "Restrições de conta infantil da Microsoft Family."),
            S("SEMgrSvc", "Inúteis", "Gerenciador de Pagamentos e NFC", "Pagamentos por NFC (quase nenhum PC tem)."),
            S("XblAuthManager", "Xbox", "Gerenciador de Autenticação Xbox Live", "Necessário só para jogos da Xbox/Game Pass.", "manual", "low", "xbox"),
            S("XblGameSave", "Xbox", "Jogo Salvo do Xbox Live", "Sincroniza saves de jogos Xbox na nuvem.", "manual", "low", "xbox"),
            S("XboxNetApiSvc", "Xbox", "Serviço de Rede Xbox Live", "Rede para jogos Xbox.", "manual", "low", "xbox"),
            S("XboxGipSvc", "Xbox", "Gerenciamento de Acessórios Xbox", "Atualiza firmware de controles Xbox. Mantenha se usa controle Xbox.", "manual", "low", "xbox"),
            S("Spooler", "Opcionais", "Spooler de Impressão", "Desative só se você NÃO usa impressora (nem PDF virtual).", "auto", "medium", "printer"),
            S("WSearch", "Opcionais", "Windows Search (indexação)", "Indexa arquivos para a busca. Desativar deixa a busca do Iniciar mais lenta.", "auto", "medium"),
            S("SysMain", "Opcionais", "SysMain (Superfetch)", "Pré-carrega apps na RAM. Só ajuda desativar em HD muito lento com pouca RAM.", "auto", "high"),
            S("wisvc", "Inúteis", "Serviço do Windows Insider", "Só necessário para quem participa do programa Insider."),
            S("bthserv", "Opcionais", "Suporte a Bluetooth", "Desative só se o PC não usa nenhum dispositivo Bluetooth.", "manual", "medium", "bluetooth"),
            S("TrkWks", "Inúteis", "Cliente de Rastreamento de Link Distribuído", "Mantém atalhos funcionando quando arquivos mudam de lugar em redes de empresa.", "auto"),
            S("SharedAccess", "Inúteis", "Compartilhamento de Conexão com a Internet", "Transforma o PC em roteador para outros aparelhos.", "manual"),
            S("icssvc", "Opcionais", "Ponto de Acesso Móvel (hotspot)", "Desative se não compartilha a internet do PC pelo Wi-Fi.", "manual", "medium", "laptop"),
            S("TapiSrv", "Inúteis", "Telefonia (TAPI)", "Modems discados e telefonia antiga.", "manual", "medium"),
            S("AJRouter", "Inúteis", "Roteador AllJoyn", "Protocolo de casa inteligente praticamente abandonado."),
            S("ALG", "Inúteis", "Gateway de Camada de Aplicativo", "Usado só pelo compartilhamento de conexão antigo."),
            S("WalletService", "Inúteis", "Serviço de Carteira", "Carteira digital do Windows, sem uso no Brasil."),
            S("diagsvc", "Telemetria", "Execução de Diagnóstico", "Roda diagnósticos pedidos pela Microsoft.", "manual"),
            S("PcaSvc", "Opcionais", "Assistente de Compatibilidade de Programas", "Avisa sobre programas antigos incompatíveis. Pouco útil e roda em segundo plano.", "auto", "medium"),
            S("WinRM", "Inúteis", "Gerenciamento Remoto do Windows", "Administração remota de empresas. Desativar também reduz riscos.", "manual"),
            S("SCardSvr", "Opcionais", "Cartão Inteligente", "Leitores de smart card (certificado digital em cartão, e-CPF/e-CNPJ A3).", "manual", "medium", "smartcard"),
            S("ScDeviceEnum", "Opcionais", "Enumeração de Dispositivos de Cartão Inteligente", "Parte do suporte a smart card / token A3.", "manual", "medium", "smartcard"),
            S("stisvc", "Opcionais", "Aquisição de Imagens do Windows (scanner)", "Desative só se você não usa scanner nem câmera conectada.", "manual", "medium", "scanner"),
            S("WbioSrvc", "Opcionais", "Biometria do Windows", "Impressão digital e reconhecimento facial do Windows Hello.", "manual", "medium", "biometric"),
            S("SensorService", "Opcionais", "Serviço de Sensores", "Rotação de tela e brilho automático de notebooks e tablets.", "manual", "medium", "laptop"),
            S("SensrSvc", "Opcionais", "Monitoramento de Sensores", "Sensor de luz ambiente para brilho automático.", "manual", "medium", "laptop"),
            S("SensorDataService", "Opcionais", "Dados de Sensores", "Entrega dados de sensores aos apps (notebooks e tablets).", "manual", "medium", "laptop"),
            S("CscService", "Inúteis", "Arquivos Offline", "Cópias locais de pastas de rede de empresa.", "manual"),
            S("lmhosts", "Opcionais", "Auxiliar NetBIOS sobre TCP/IP", "Compartilhamento de arquivos com PCs muito antigos na rede local.", "manual", "medium"),
            S("RemoteAccess", "Inúteis", "Roteamento e Acesso Remoto", "Servidor de VPN/roteamento (já vem desativado na maioria).", "disabled"),
        };

        static int StartValue(string kind) => kind == "auto" ? 2 : kind == "disabled" ? 4 : 3;

        static string StartKind(int v) => v == 2 ? "auto" : v == 4 ? "disabled" : v == 3 ? "manual" : v <= 1 ? "boot" : "manual";

        static RegistryKey SvcKey(string name, bool write) =>
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default)
                .OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name, write);

        public static object List()
        {
            var all = ServiceController.GetServices().ToDictionary(s => s.ServiceName, StringComparer.OrdinalIgnoreCase);
            bool admin = Program.IsAdmin;
            var r = new List<object>();
            foreach (var s in List_)
            {
                ServiceController sc;
                if (!all.TryGetValue(s.Name, out sc)) continue; // não existe nesta versão do Windows
                int start;
                using (var k = SvcKey(s.Name, false)) start = Convert.ToInt32(k?.GetValue("Start") ?? 3);
                string status;
                try { status = sc.Status == ServiceControllerStatus.Running ? "running" : sc.Status == ServiceControllerStatus.Stopped ? "stopped" : "pending"; } catch { status = "unknown"; }
                r.Add(new Dictionary<string, object> {
                    { "name", s.Name }, { "title", s.Title }, { "desc", s.Desc }, { "group", s.Group }, { "risk", s.Risk },
                    { "start", StartKind(start) }, { "default", s.Default }, { "status", status }, { "disabled", start == 4 }, { "locked", !admin } });
            }
            foreach (var sc in all.Values) sc.Dispose();
            return r;
        }

        /// <summary>disable=true: para e desabilita. disable=false: volta ao tipo padrão e inicia se for automático.</summary>
        public static object Set(string name, bool disable)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            var s = List_.First(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            PrivRun.WithKey(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\" + s.Name, () =>
            {
                using (var k = SvcKey(s.Name, true))
                {
                    if (k == null) throw new InvalidOperationException("Serviço não encontrado");
                    k.SetValue("Start", disable ? 4 : StartValue(s.Default), RegistryValueKind.DWord);
                }
            });
            using (var sc = new ServiceController(s.Name))
            {
                try
                {
                    if (disable && sc.Status != ServiceControllerStatus.Stopped && sc.CanStop) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15)); }
                    if (!disable && s.Default == "auto" && sc.Status == ServiceControllerStatus.Stopped) { sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15)); }
                }
                catch { /* alguns serviços dependentes recusam parar na hora; o tipo de inicialização já vale após reiniciar */ }
            }
            return true;
        }

        /// <summary>Tipo de inicialização de qualquer serviço (2 = automático, 3 = manual, 4 = desativado), parando ou iniciando na hora.</summary>
        public static void SetRaw(string name, int start)
        {
            if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
            PrivRun.WithKey(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\" + name, () =>
            {
                using (var k = SvcKey(name, true))
                {
                    if (k == null) throw new InvalidOperationException("Serviço não encontrado");
                    k.SetValue("Start", start, RegistryValueKind.DWord);
                }
            });
            try
            {
                using (var sc = new ServiceController(name))
                {
                    if (start == 4 && sc.Status != ServiceControllerStatus.Stopped && sc.CanStop) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15)); }
                    if (start == 2 && sc.Status == ServiceControllerStatus.Stopped) { sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15)); }
                }
            }
            catch { }
        }

        /// <summary>Usado pelo Windows Update e pelo limpador: para/inicia sem mexer no tipo de inicialização.</summary>
        public static bool StopTemporarily(string name)
        {
            try
            {
                using (var sc = new ServiceController(name))
                {
                    if (sc.Status == ServiceControllerStatus.Stopped) return false;
                    sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    return true;
                }
            }
            catch { return false; }
        }

        public static void StartIf(string name, bool wasRunning)
        {
            if (!wasRunning) return;
            try { using (var sc = new ServiceController(name)) if (sc.Status == ServiceControllerStatus.Stopped) sc.Start(); } catch { }
        }
    }
}
