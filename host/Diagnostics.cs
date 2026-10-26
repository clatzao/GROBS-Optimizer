using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using Microsoft.Win32;

namespace Grobs
{
    /// <summary>Lê os registros do Windows, o Gerenciador de Dispositivos e o estado do sistema; aponta problemas e a correção.</summary>
    internal static class Diagnostics
    {
        sealed class Issue
        {
            public string Id, Level, Area, Title, Detail, Advice, Fix, FixLabel;
        }

        static string Pnp(int code)
        {
            switch (code)
            {
                case 1: case 18: case 28: return "driver não instalado ou corrompido";
                case 3: return "driver corrompido ou memória insuficiente";
                case 10: return "o dispositivo não pode iniciar";
                case 12: return "conflito de recursos";
                case 22: return "dispositivo desativado";
                case 24: return "dispositivo ausente ou com defeito";
                case 31: return "driver não carregou";
                case 39: return "driver corrompido ou ausente";
                case 43: return "o Windows parou o dispositivo após falhas";
                case 45: return "dispositivo desconectado";
                default: return "erro " + code;
            }
        }

        static List<EventRecord> Events(string log, string xpathFilter, int days, int max = 200)
        {
            var list = new List<EventRecord>();
            try
            {
                long ms = (long)days * 86400000L;
                var q = new EventLogQuery(log, PathType.LogName, "*[System[" + xpathFilter + " and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]") { ReverseDirection = true };
                using (var r = new EventLogReader(q))
                    for (EventRecord e; (e = r.ReadEvent()) != null && list.Count < max;) list.Add(e);
            }
            catch { }
            return list;
        }

        public static object Scan()
        {
            var issues = new List<Issue>();
            Action<Issue> add = issues.Add;

            // 1) Gerenciador de Dispositivos
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, ConfigManagerErrorCode, PNPDeviceID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
                    foreach (ManagementObject o in s.Get())
                    {
                        int code = Convert.ToInt32(o["ConfigManagerErrorCode"]);
                        if (code == 45) continue; // desconectado: normal para pendrives/fones removidos
                        string name = Convert.ToString(o["Name"] ?? "Dispositivo desconhecido");
                        add(new Issue
                        {
                            Id = "pnp:" + o["PNPDeviceID"], Level = code == 22 ? "info" : "warn", Area = "Dispositivos",
                            Title = name + ": " + Pnp(code),
                            Detail = "Código " + code + " no Gerenciador de Dispositivos.",
                            Advice = code == 22 ? "Se foi você que desativou, ignore. Senão, reative o dispositivo." : "Procure o driver na aba Drivers ou no site do fabricante.",
                            Fix = code == 22 ? "enableDevice:" + o["PNPDeviceID"] : "drivers", FixLabel = code == 22 ? "Reativar" : "Buscar driver",
                        });
                    }
            }
            catch { }

            // 2) Telas azuis e desligamentos inesperados (30 dias)
            var bsod = Events("System", "Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and (EventID=1001)", 30);
            if (bsod.Count > 0)
            {
                string code = "";
                try { code = bsod[0].FormatDescription() ?? ""; int i = code.IndexOf("0x", StringComparison.Ordinal); code = i >= 0 ? code.Substring(i, Math.Min(10, code.Length - i)) : ""; } catch { }
                add(new Issue { Id = "bsod", Level = "bad", Area = "Estabilidade", Title = bsod.Count + " tela(s) azul(is) nos últimos 30 dias",
                    Detail = "Última em " + bsod[0].TimeCreated?.ToString("dd/MM/yyyy HH:mm") + (code.Length > 0 ? " · código " + code : ""),
                    Advice = "Causas comuns: driver de vídeo/rede, memória RAM ou overclock instável. Atualize os drivers e, se continuar, teste a memória.",
                    Fix = "memtest", FixLabel = "Testar memória" });
            }
            var kp = Events("System", "Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41)", 30);
            if (kp.Count > 0)
                add(new Issue { Id = "kp41", Level = kp.Count > 3 ? "bad" : "warn", Area = "Estabilidade", Title = kp.Count + " desligamento(s) inesperado(s) em 30 dias",
                    Detail = "O PC desligou sem encerrar o Windows (queda de energia, travamento ou botão).",
                    Advice = "Se não foi falta de luz: verifique a fonte, a temperatura e um possível overclock instável. A Inicialização Rápida do Windows também costuma causar desligamentos e travas ao ligar.",
                    Fix = "fastStartup", FixLabel = "Desativar Inicialização Rápida" });

            // 3) Erros de hardware (WHEA)
            var whea = Events("System", "Provider[@Name='Microsoft-Windows-WHEA-Logger']", 30);
            if (whea.Count > 0)
                add(new Issue { Id = "whea", Level = whea.Count > 5 ? "bad" : "warn", Area = "Hardware", Title = whea.Count + " erro(s) de hardware registrados (WHEA)",
                    Detail = "Último em " + whea[0].TimeCreated?.ToString("dd/MM/yyyy HH:mm") + ".",
                    Advice = "Indica instabilidade de CPU, memória ou PCIe. Desfaça overclocks/XMP agressivos e atualize a BIOS.", Fix = "memtest", FixLabel = "Testar memória" });

            // 4) Disco
            // só Crítico/Erro/Aviso: no Windows 10/11 o NTFS 98 também é gravado como Informação ("volume íntegro")
            var disk = Events("System", "(Provider[@Name='disk'] or Provider[@Name='Ntfs'] or Provider[@Name='Microsoft-Windows-Ntfs']) and (EventID=7 or EventID=11 or EventID=51 or EventID=153 or EventID=55 or EventID=98) and (Level=1 or Level=2 or Level=3)", 30);
            if (disk.Count > 0)
                add(new Issue { Id = "disk", Level = "bad", Area = "Disco", Title = disk.Count + " erro(s) de leitura/sistema de arquivos",
                    Detail = "Último em " + disk[0].TimeCreated?.ToString("dd/MM/yyyy HH:mm") + ". Pode ser cabo, setores defeituosos ou o disco no fim da vida.",
                    Advice = "Faça backup e verifique o sistema de arquivos. Veja a saúde S.M.A.R.T. no Monitor.", Fix = "chkdsk", FixLabel = "Verificar disco" });

            // 5) Programas travando
            var crashes = Events("Application", "Provider[@Name='Application Error'] and (EventID=1000)", 7, 300);
            foreach (var g in crashes.Select(e => { try { return e.Properties.Count > 0 ? Convert.ToString(e.Properties[0].Value) : null; } catch { return null; } })
                .Where(n => !string.IsNullOrEmpty(n)).GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 2).OrderByDescending(g => g.Count()).Take(5))
                add(new Issue { Id = "crash:" + g.Key, Level = "warn", Area = "Programas", Title = g.Key + " travou " + g.Count() + " vezes nesta semana",
                    Detail = "Registro de falhas de aplicativos.", Advice = "Atualize ou reinstale o programa. Se for um jogo, verifique os arquivos pela loja (Steam/Epic). O histórico mostra a causa de cada falha.",
                    Fix = "reliability", FixLabel = "Ver causa das falhas" });

            // 6) Falhas do Windows Update
            var wuFail = Events("System", "Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and (EventID=20)", 30);
            if (wuFail.Count >= 2)
                add(new Issue { Id = "wu", Level = "warn", Area = "Windows Update", Title = wuFail.Count + " atualizações falharam em 30 dias",
                    Detail = "Atualizações que tentaram instalar e deram erro.", Advice = "Limpar o cache do Windows Update costuma resolver. Se persistir, rode o reparo DISM.",
                    Fix = "cleanWu", FixLabel = "Limpar cache do Update" });

            // 7) Reinicialização pendente
            bool pending = false;
            using (var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default))
            {
                pending |= lm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") != null;
                pending |= lm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") != null;
            }
            if (pending)
                add(new Issue { Id = "reboot", Level = "info", Area = "Sistema", Title = "Reinicialização pendente", Detail = "Atualizações ou instalações estão esperando o PC reiniciar.",
                    Advice = "Reinicie quando puder para concluir as atualizações.", Fix = "restart", FixLabel = "Reiniciar em 1 min" });

            // 8) Espaço livre
            try
            {
                var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                double free = (double)c.AvailableFreeSpace / c.TotalSize;
                if (free < 0.1)
                    add(new Issue { Id = "space", Level = "bad", Area = "Disco", Title = "Unidade do sistema quase cheia (" + Math.Round(free * 100) + "% livre)",
                        Detail = "Com menos de 10% livre o Windows fica lento e as atualizações falham.", Advice = "Rode a Limpeza e veja o que ocupa espaço.", Fix = "cleaner", FixLabel = "Abrir Limpeza" });
            }
            catch { }

            // 9) Internet / DNS
            bool dnsOk = true;
            try { Dns.GetHostEntry("www.microsoft.com"); } catch { dnsOk = false; }
            if (!dnsOk)
                add(new Issue { Id = "dns", Level = "warn", Area = "Rede", Title = "Não foi possível resolver nomes de sites (DNS)",
                    Detail = "Sem internet ou o DNS falhou.", Advice = "Limpe o cache DNS; se não resolver, redefina a pilha de rede (requer reiniciar).", Fix = "flushDns", FixLabel = "Limpar DNS" });

            // 10) Integridade do sistema (resultado do último SFC)
            try
            {
                var cbs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Logs\CBS\CBS.log");
                if (File.Exists(cbs) && (DateTime.Now - File.GetLastWriteTime(cbs)).TotalDays < 60)
                {
                    string tail;
                    using (var fs = new FileStream(cbs, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Seek(Math.Max(0, fs.Length - 400000), SeekOrigin.Begin);
                        tail = new StreamReader(fs).ReadToEnd();
                    }
                    if (tail.Contains("Cannot repair member file"))
                        add(new Issue { Id = "sfc", Level = "bad", Area = "Sistema", Title = "Arquivos do Windows corrompidos que o SFC não reparou",
                            Detail = "Encontrado no último relatório do SFC (CBS.log).", Advice = "Rode o reparo DISM e depois o SFC de novo.", Fix = "dism", FixLabel = "Reparar (DISM)" });
                }
            }
            catch { }

            int bad = issues.Count(i => i.Level == "bad"), warn = issues.Count(i => i.Level == "warn");
            return new Dictionary<string, object> {
                { "issues", issues.Select(i => (object)new Dictionary<string, object> {
                    { "id", i.Id }, { "level", i.Level }, { "area", i.Area }, { "title", i.Title }, { "detail", i.Detail },
                    { "advice", i.Advice }, { "fix", i.Fix }, { "fixLabel", i.FixLabel } }).ToList() },
                { "bad", bad }, { "warn", warn }, { "when", DateTime.Now.ToString("dd/MM/yyyy HH:mm") } };
        }

        /// <summary>Executa a correção sugerida. Ações longas abrem uma janela própria para o usuário acompanhar.</summary>
        public static object Fix(string fix)
        {
            if (fix.StartsWith("enableDevice:"))
            {
                if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
                string id = fix.Substring("enableDevice:".Length);
                string o = Power.Run(Power.Sys("pnputil.exe"), "/enable-device \"" + id + "\"", 30000);
                return new Dictionary<string, object> { { "output", o.Trim() } };
            }
            switch (fix)
            {
                case "flushDns": return Tools.FlushDns();
                case "cleanWu": return WinUpdate.CleanCache();
                case "dism": return Tools.RepairSystem("dism");
                case "sfc": return Tools.RepairSystem("sfc");
                case "chkdsk":
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Power.Sys("cmd.exe"), "/k title GROBS - Verificar disco && chkdsk " + Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\') + " /scan") { UseShellExecute = true, Verb = "runas" });
                    return true;
                case "fastStartup":
                    if (!Program.IsAdmin) throw new UnauthorizedAccessException("Requer administrador");
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default))
                    using (var k = root.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power"))
                        k.SetValue("HiberbootEnabled", 0, RegistryValueKind.DWord);
                    return true;
                case "reliability": // Monitor de Confiabilidade do Windows: causa de cada travamento
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Power.Sys("perfmon.exe"), "/rel") { UseShellExecute = true });
                    return true;
                case "restart":
                    Power.Run(Power.Sys("shutdown.exe"), "/r /t 60 /c \"GROBS Optimizer: reiniciando para concluir atualizações. Para cancelar: shutdown /a\"", 10000);
                    return true;
                case "cancelRestart":
                    Power.Run(Power.Sys("shutdown.exe"), "/a", 10000);
                    return true;
                case "memtest":
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Power.Sys("mdsched.exe")) { UseShellExecute = true });
                    return true;
            }
            throw new ArgumentException("Correção desconhecida");
        }
    }
}
