# Pesquisa de mercado: otimização, monitoramento e antivírus para Windows

**Projeto:** GROBS Optimizer (app desktop leve de otimização para Windows 10/11, x64 e x86)
**Data da pesquisa:** 26/10/2026
**Método:** buscas na web (sites de review, laboratórios independentes, documentação da Microsoft, fóruns e repositórios). Cada afirmação traz o link da fonte. Preços variam por região, promoção de 1º ano e renovação: trate valores como **referência, não como cotação**. Itens marcados com **(não verificado)** não foram confirmados em fonte primária nesta pesquisa.

---

## Sumário executivo

1. **O mercado de "otimizadores" sofre de crise de confiança.** Os produtos mais conhecidos acumulam incidentes: malware distribuído pelo instalador oficial do CCleaner (2017), telemetria que não podia ser desligada (CCleaner 5.45, 2018), venda de dados de navegação pela Avast/Jumpshot (multa FTC de US$ 16,5 mi), minerador de criptomoeda no Norton 360, bundleware em IObit e Core Temp, migração forçada de Kaspersky para UltraAV nos EUA. **Transparência e ausência de bundle/nag são diferenciais reais.**
2. **Os ganhos reais de desempenho vêm de poucas ações**, e quase todas já existem no Windows: remover programas de inicialização, desinstalar apps não usados, liberar espaço em disco, manter Windows/drivers atualizados, verificar malware, plano de energia adequado e "Otimizar unidades" (TRIM no SSD). É exatamente a lista oficial da Microsoft ([Dicas para melhorar o desempenho do PC no Windows](https://support.microsoft.com/pt-br/windows/dicas-para-melhorar-o-desempenho-do-pc-no-windows-b3b3ef5b-5953-fb6a-2528-4bbed82fba96)).
3. **Muitas "otimizações" populares são mitos ou prejudiciais:** limpeza de registro (Microsoft não suporta), "limpadores de RAM", apagar Prefetch, desligar SysMain, desabilitar arquivo de paginação, desfragmentar SSD, desligar auto-tuning TCP, desativar Defender, "atualizadores de drivers" de terceiros.
4. **Antivírus:** em 2026 os grandes estão empatados no topo. No AV-TEST (jul-ago/2026) todos os 8 avaliados aqui são TOP PRODUCT; no AV-Comparatives Real-World (fev-mai/2026) Microsoft Defender teve 99,0% com **zero falsos positivos**. **O app não precisa (e não deve) ser antivírus**: deve integrar e exibir o status do Defender/antivírus instalado.
5. **Monitoramento de temperatura exige driver de kernel.** O driver WinRing0 (usado por LibreHardwareMonitor, Fan Control, etc.) passou a ser bloqueado pelo Defender em 2025 por vulnerabilidade (CVE-2020-14979). Escolha de backend de sensores é decisão arquitetural crítica.

---

## 1. Otimização e limpeza

### 1.1 Tabela comparativa

| App | Preço (referência) | Modelo | Leveza | Principais riscos/reclamações |
|---|---|---|---|---|
| CCleaner (Gen Digital/Avast) | Grátis; Pro Plus ~US$ 44,95/ano (3 PCs) | Freemium, assinatura | Média | Incidente 2017, telemetria 2018, renovação automática |
| BleachBit | Grátis (GPL) | Open source | Alta | Interface datada; poucas reviews |
| Advanced SystemCare (IObit) | Grátis; Pro pago anual | Freemium | Baixa | Bundleware, PUP (Malwarebytes), registro agressivo |
| Glary Utilities | Grátis; Pro a partir de ~US$ 19,97 | Freemium | Média | Muitas ferramentas redundantes |
| Wise Care 365 | Grátis; Pro pago **(não verificado)** | Freemium | Média | Nag de upgrade **(não verificado)** |
| AVG TuneUp | ~US$ 65,99/ano (1 PC) | Assinatura | Média/baixa | Caro, sem licença vitalícia |
| Avast Cleanup Premium | Assinatura **(não verificado)** | Assinatura | Média/baixa | Mesma empresa do caso Jumpshot |
| Ashampoo WinOptimizer 2026 | US$ 30 / € 30 (promo ~50%) | Licença por versão | Média | Upgrade de versão é pago |
| Microsoft PC Manager | Grátis | Oficial MS | Alta | Deep cleanup pode apagar Prefetch; "Proteção" só abre o Windows Security |
| Razer Cortex | Grátis | Freemium (ecossistema Razer) | Média | Ganho de FPS modesto |
| Process Lasso (Bitsum) | Grátis; Pro vitalício ~US$ 39,95 | Freemium | Alta | Curva de aprendizado |
| Chris Titus WinUtil | Grátis (open source) | Script PowerShell | N/A (roda e sai) | Tweaks avançados podem desligar Defender/serviços |
| O&O ShutUp10++ | Grátis (pessoal e comercial) | Freeware, portátil | Alta | Ajustes agressivos quebram Update/Search/OneDrive |
| Revo Uninstaller | Grátis; Pro pago | Freemium | Alta | Preço Pro atual **(não verificado)** |
| BCUninstaller | Grátis (open source) | Open source | Alta | Pode deixar mais resíduos que o Revo |
| WizTree | Grátis pessoal; comercial a partir de US$ 25 | Freeware/doação | Muito alta | Requer admin para leitura rápida da MFT |
| TreeSize (JAM Software) | Free; Personal/Pro pagos | Freemium | Alta | Free mais lento (varre o disco) |

### 1.2 Detalhes por produto

**CCleaner**
- Preço: Pro Plus listado a US$ 44,95/ano para 3 PCs ([TemperStack](https://www.temperstack.com/plans/ccleaner/)); outros valores por região ([Findstack](https://findstack.com/products/ccleaner/reviews?page=20), [Tom's Guide](https://www.tomsguide.com/computing/software/boost-your-devices-speed-and-efficiency-with-ccleaner-now-up-to-50-percent-off)). PCWorld considerou os planos pagos bom custo-benefício ([PCWorld](https://www.pcworld.com/article/2065602/ccleaner-professional-review-2.html)).
- Trustpilot: TrustScore 4,0 com ~27,7 mil avaliações; elogios à limpeza, reclamações recorrentes sobre **renovação automática** e cobranças duplicadas ([Trustpilot](https://uk.trustpilot.com/review/www.ccleaner.com?page=8), [Trustpilot Cleverbridge](https://ca.trustpilot.com/review/www.cleverbridge.com)).
- **Incidente 2017:** versões 5.33.6162 e Cloud 1.07.3191 distribuídas com o malware Floxif, assinado com certificado válido; ~2,27 milhões de PCs com a versão infectada, ataque à cadeia de build ([CIS/MS-ISAC](https://www.cisecurity.org/ms-isac/cyber-alert-ccleaner-software-supply-chain-compromise), [SecurityWeek](https://www.securityweek.com/millions-download-maliciously-modified-pc-utility/), [Kaspersky ICS-CERT](https://ics-cert.kaspersky.com/?p=6570)).
- **Incidente 2018:** a versão 5.45 reativava o "Active Monitoring" após ser desligado, removeu controles de privacidade da versão grátis e não fechava pelo X; a Avast retirou a versão e voltou para a 5.44 ([CSO Online](https://www.csoonline.com/article/565979/avast-pulls-ccleaner-version-that-lacked-privacy-options-after-backlash.html), [iTWire](https://itwire.com/security/avast-pulls-latest-ccleaner-version-over-privacy-issues), [Wikipedia](https://en.wikipedia.org/wiki/CCleaner)).
- Lição de design: monitoramento em segundo plano deve ser **opcional, desligável e respeitado**; o botão fechar deve fechar (ou deixar explícito que vai para a bandeja).

**BleachBit**
- Grátis e open source (Windows/Linux); destruição de arquivos (shred), limpeza de espaço livre, CLI e cleaners em XML (CleanerML) ([FileHorse](https://www.filehorse.com/download-bleachbit/screenshots), [Neowin](https://www.neowin.net/news/bleachbit-440/)). Poucas reviews formais (G2: 2 avaliações) ([G2](https://www.g2.com/sellers/bleachbit)).
- Lição: definições de limpeza declarativas (arquivo de regras) facilitam auditoria e atualização.

**Advanced SystemCare (IObit)**
- Elogiado pela interface; criticado pelo registro agressivo, adware e software empacotado ([AlternativeTo](https://www.alternativeto.net/software/advanced-systemcare-free/about/)). Disputa longa com a Malwarebytes, que passou a classificá-lo como PUP em 2017 ([SecurityBrief](https://securitybrief.com.au/story/iobit-malwarebytes-spat-continues-legitimate-program-blacklisted-pup)). Acusações de cópia de banco de dados da Malwarebytes (2009) são alegações, não decisões judiciais ([BleepingComputer fórum](https://www.bleepingcomputer.com/forums/t/642319/malwarebytes-vs-iobit/)).

**Glary Utilities**
- Capterra 4,6/5 (16 avaliações), Pro a partir de US$ 19,97, versão grátis disponível ([Capterra](https://www.capterra.co.uk/software/1011542/glary-utilities-pro)). Amostra pequena.

**Wise Care 365**
- Não foram encontradas reviews/preços confiáveis de 2025-2026 nesta pesquisa **(não verificado)**.

**AVG TuneUp / Avast Cleanup**
- TechRadar: bom conjunto de ferramentas, mas caro e sem licença vitalícia ([TechRadar](https://www.techradar.com/reviews/avg-tuneup)). Tech Advisor: US$ 65,99/ano para 1 PC, entre os mais caros ([Tech Advisor](https://www.techadvisor.com/article/722634/avg-tuneup-review.html)). Avast Cleanup: sem review recente encontrada **(não verificado)**; mesma controladora (Gen Digital) do caso Jumpshot (ver seção 3).

**Ashampoo WinOptimizer 2026**
- Versão 28.00.20 (13/01/2026), Windows 10/11 (não ARM), preço de lista US$ 30/€ 30, trial de 30 dias ([Ashampoo](https://www.ashampoo.com/en-ie/winoptimizer-2026)); promoções de ~50% ([Ashampoo GB](https://www.ashampoo.com/en-gb/winoptimizer)). TechRadar o lista entre os melhores e descreve a varredura em três categorias: limpar, otimizar, proteger ([TechRadar](https://www.techradar.com/uk/best/pc-optimizer)). Reclamação: licença da v27 não dá direito à v28 ([Tekpon](https://tekpon.com/software/winoptimizer-25/reviews)).

**Microsoft PC Manager**
- Grátis, oficial; "Boost" de um clique (opcionalmente automático, "Smart Boost") e "Deep cleanup" rápido (logs, cache DirectX, relatórios de erro, miniaturas, navegador) ([Windows Report](https://windowsreport.com/microsoft-pc-manager-review/), [Make Tech Easier](https://maketecheasier.com/pc-manager-maintenance-tool)). Ressalvas: a limpeza profunda pode apagar arquivos de Prefetch ([HotHardware](https://amp.hothardware.com/news/microsoft-pc-manager-could-speed-up-performance-or-slow-it-down)); a "proteção" apenas abre o Windows Security ([Windows Report](https://windowsreport.com/microsoft-pc-manager-review/)). A própria Microsoft o recomenda na página de dicas de desempenho ([Microsoft Suporte](https://support.microsoft.com/pt-br/windows/dicas-para-melhorar-o-desempenho-do-pc-no-windows-b3b3ef5b-5953-fb6a-2528-4bbed82fba96)).
- É o **concorrente direto mais relevante** para um otimizador leve e gratuito.

**Razer Cortex**
- TechRadar: ajuda a "espremer alguns FPS", mas não transforma PC fraco em forte ([TechRadar](https://www.techradar.com/reviews/razer-cortex)); teste da EarlyGame mediu +10-15 FPS médios em alguns jogos ([EarlyGame](https://earlygame.com/de/reviews/gaming/razer-cortex-review)); relatos de nenhuma melhoria em hardware forte ([Razer Insider](https://insider.razer.com/razer-cortex-39/razer-game-booster-82)).

**Process Lasso (Bitsum)**
- ProBalance (reduz temporariamente a prioridade de processos de fundo que prejudicam a responsividade) é grátis para sempre; Pro adiciona modo jogo automático, CPU Sets, perfis de energia por processo, serviço; vitalício ~US$ 39,95, reembolso em 30 dias ([Bitsum](https://bitsum.com/?p=4708), [gHacks](https://www.ghacks.net/?p=25572)).
- Lição: ajuste **dinâmico e reversível** de prioridade é uma otimização legítima, diferente de "matar processos".

**Chris Titus WinUtil**
- Script open source: instala apps via winget, aplica tweaks (cria ponto de restauração, desativa telemetria, serviços em manual) ([GitHub espelho](https://github.com/jadenblack/ChrisTitusTech-winutil)). Testes: impacto em RAM praticamente nulo (economia de no máximo 100-200 MB entre 4 debloaters) ([Yahoo Tech](https://tech.yahoo.com/computing/articles/tested-4-windows-debloating-tools-100000336.html)); serviços em manual podem gerar atrasos; opções avançadas podem desligar Defender, "questionável para a maioria" ([TechSpot](https://www.techspot.com/article/3095-stop-windows-slop/), [Club386](https://www.club386.com/how-to-debloat-windows-11-stop-spying/)).

**O&O ShutUp10++**
- Grátis para uso pessoal e comercial, portátil; permite desfazer, exportar perfis e criar ponto de restauração; não bloqueia toda telemetria; ajustes agressivos podem quebrar Update, Defender, Search, OneDrive ([O&O FAQ](https://manuals.oo-software.com/ooshutup10/docs/faq), [BinaryFork](https://binaryfork.com/o-and-o-shut-up-review-1433)). O próprio fabricante recomenda revisar item a item em vez de "aplicar todos os recomendados" ([O&O blog](https://blog.oo-software.com/en/?p=6905)).
- Lição: padrão **semáforo** (verde = recomendado, amarelo = com ressalvas, vermelho = avançado) é bem aceito.

**Revo Uninstaller e BCUninstaller**
- Revo: grátis + Pro (remoção forçada de restos de programas já desinstalados, extensões de navegador) ([gHacks](https://www.ghacks.net/?p=2799)). BCUninstaller: grátis e open source, ótimo para desinstalação em lote; usuários relatam que o Revo encontra mais resíduos ([MalwareTips](https://malwaretips.com/threads/uninstaller-clean-up-comparison-free-edition.100666/post-959540), [Wilders](https://www.wilderssecurity.com/goto/post?id=2464748)).

**WizTree e TreeSize**
- WizTree lê a MFT do NTFS diretamente (muito rápido, precisa de admin); grátis para uso pessoal, comercial a partir de US$ 25. TreeSize é mais completo (gráficos, duplicados), Free é mais lento; WinDirStat 2.x também passou a ler a MFT ([FolderSizes](https://www.foldersizes.com/features/wiztree), [heise](https://www.heise.de/download/product/wiztree), [Yo Motherboard](https://yomotherboard.com/question/should-i-try-wiztree-if-i-already-use-windirstat/)).
- Lição: um **analisador visual de espaço** (treemap) é dos recursos mais úteis e mais elogiados.

### 1.3 Contexto de reviews
- TechRadar 2026 coloca o Iolo System Mechanic como favorito e cita Ashampoo WinOptimizer como opção acessível ([TechRadar](https://www.techradar.com/best/pc-optimizer?source=in)). Diversos rankings 2026 são de afiliados ou gerados por IA (ex.: [iTechGuides](https://www.itechguides.com/pc-optimization-software/), [Gitnux](https://gitnux.org/best/pc-optimizer-software/)) e devem ter peso baixo.
- Não encontrei review da PCMag de 2025-2026 para os otimizadores citados.

---

## 2. Monitoramento (temperatura, uso, saúde)

| App | Preço | Destaques | Leveza | Pontos de atenção |
|---|---|---|---|---|
| HWiNFO | Grátis (pessoal); Pro a partir de US$ 29 (1º ano), vitalício a partir de US$ 199 | Sensores mais completos, log, OSD via RTSS | Alta | Free: memória compartilhada limitada a 12 h |
| HWMonitor (CPUID) | Grátis; Pro pago **(não verificado)** | Simples, mín/máx | Alta | Menos sensores/gráficos |
| Core Temp | Grátis | Temperatura por núcleo | Muito alta | **Instalador com adware**; usar a versão ZIP |
| MSI Afterburner + RTSS | Grátis | OC de GPU, overlay em jogo | Alta | Desenvolvimento depende de um desenvolvedor; incerteza desde 2022 |
| LibreHardwareMonitor | Grátis (open source) | Biblioteca reutilizável (.NET) | Alta | Driver WinRing0 marcado pelo Defender; substituído em versões novas |
| CrystalDiskInfo | Grátis (open source) | SMART, estado "Bom/Cuidado/Ruim" | Muito alta | Sem review recente encontrada |
| Speccy (Piriform) | Grátis; Pro pago | Resumo de hardware para leigos | Alta | Histórico de toolbars no instalador; atualizações espaçadas |
| NZXT CAM | Grátis | Overlay, controle de hardware NZXT | **Baixa** | Picos de 15-20% de CPU em versões específicas; telemetria |
| System Informer / Process Explorer | Grátis | Gerenciador de processos avançado | Alta | Público técnico |
| TrafficMonitor | Grátis (open source) | Velocidade de rede/CPU/RAM na barra de tarefas | Muito alta | Temperatura só na versão Standard (admin, usa LHM) |

**Fontes e detalhes**
- HWiNFO: grátis para uso pessoal; Pro adiciona relatórios/log automáticos, atualização automática, memória compartilhada ilimitada (Free limitada a 12 h) ([HWiNFO](https://www.hwinfo.com/?p=1212), [fitgap](https://us.fitgap.com/products/016418/hwinfo), [ComputerBase](https://www.computerbase.de/2021-03/hwinfo64-pro-7-0/)).
- Core Temp: instalador principal com adware (InstallIQ), recomenda-se a versão standalone ([AnandTech fórum](https://forums.anandtech.com/threads/is-coretemp-now-adware.2187454/post-37617305), [portablefreeware](https://portablefreeware.com/index.php?id=2347), [heise](https://www.heise.de/download/product/core-temp-49834)).
- MSI Afterburner: pagamentos da MSI ao desenvolvedor interrompidos em 2022 pela "situação política"; RTSS continua como projeto pessoal; em 2025 houve atualizações para RTX 50 e RDNA 4 ([Overclock3D](https://overclock3d.net/news/software/msi_afterburner_project_is_probably_dead_claims_lead_developer/), [TechSpot](https://www.techspot.com/community/topics/msi-afterburner-preparing-for-geforce-rtx-5080-with-expanded-fan-controller-support.290129/), [TechRadar](https://www.techradar.com/news/one-of-the-best-gpu-utilities-out-there-is-under-threat)).
- **WinRing0:** desde 2025 o Defender detecta "HackTool:Win32/Winring0" e coloca em quarentena, quebrando Fan Control, OpenRGB, LibreHardwareMonitor e outros; não é falso positivo puro (CVE-2020-14979). Fan Control migrou para o driver PawnIO; LibreHardwareMonitor substituiu o driver ([Neowin](https://www.neowin.net/news/windows-1110-is-flagging-winring0-on-your-pc-monitoring-fan-control-apps-heres-why/), [Slashdot](https://it.slashdot.org/story/25/03/14/1351225/windows-defender-now-flags-winring0-driver-as-security-threat-breaking-multiple-pc-monitoring-tools), [Fan Control GitHub](https://github.com/Rem0o/FanControl.Releases/discussions/3017), [SYNAXON](https://synaxon.com/de-en/services/managed-services/newsfeed/important-breaking-news-vulnerability-in-libre-hardware-monitor/), [Aqua Computer fórum](https://forum.aquacomputer.de/weitere-foren/english-forum/114319-microsoft-has-banned-kernel-driver-winring0-which-aquasuite-uses-for-hardware-monitoring/)).
- NZXT CAM: usuários relatam 15-20% de CPU e +10 °C após atualização ([NZXT Ideas](https://cam-ideas.nzxt.com/ideas/CAM-I-7124)); polling constante adiciona carga ([Guru3D](https://www.guru3d.com/review/nzxt-kraken-x63-review/page-8/)).
- Speccy: Piriform passou a empacotar toolbars, e sites passaram a hospedar só a versão portátil ([BleepingComputer/gHacks](https://www.ghacks.net/?p=18396), [Wikipedia](https://en.wikipedia.org/wiki/Speccy)).
- TrafficMonitor: janela flutuante ou integrada à barra de tarefas, skins, plugins, histórico de tráfego; versão Lite sem monitoramento de hardware; Standard exige admin ([README](https://git.oschina.net/anunnaki/TrafficMonitor/blob/master/README_en-us.md), [Chocolatey](https://community.chocolatey.org/packages/traffic-monitor)).

**Implicações para o design**
- Uso de CPU, RAM, disco, rede e GPU básica podem vir de APIs do Windows (contadores de desempenho, WMI, DXGI/PDH) **sem driver de kernel**. Temperatura de CPU, em geral, exige acesso a MSR/SMBus via driver; avaliar biblioteca LibreHardwareMonitor atualizada (sem WinRing0) ou PawnIO, e degradar com elegância ("temperatura indisponível") quando não houver driver.
- SMART de discos (estilo CrystalDiskInfo) é obtido sem driver próprio e é muito valorizado ("Saúde do SSD: 97%").
- O próprio monitor precisa ser leve: o exemplo do NZXT CAM mostra que polling agressivo **gera** o calor que pretende medir. Intervalo de 1-2 s em primeiro plano e mais longo em segundo plano.

---

## 3. Antivírus

### 3.1 Resultados de laboratórios (2026)

**AV-TEST, Windows 11, uso doméstico, julho-agosto 2026** (proteção / desempenho / usabilidade, máx. 6 cada) ([AV-TEST](https://www.av-test.org/en/antivirus/home-windows/)):

| Produto | Proteção | Desempenho | Usabilidade | Total | TOP PRODUCT |
|---|---|---|---|---|---|
| Microsoft Defender 4.18 | 6 | 5,5 | 6 | 17,5 | Sim |
| Bitdefender Total Security 27.0 | 6 | 6 | 6 | 18 | Sim |
| Kaspersky Premium 21.24/25 | 6 | 6 | 6 | 18 | Sim |
| Norton 360 26.6/26.7 | 6 | 6 | 6 | 18 | Sim |
| ESET Security Ultimate 19.1/19.2 | 6 | 6 | 6 | 18 | Sim |
| Avast Free Antivirus 26.6 | 6 | 6 | 6 | 18 | Sim |
| AVG Antivirus Free 26.6/26.7 | 6 | 6 | 6 | 18 | Sim |
| Malwarebytes Premium 5.5/5.6 | 6 | 6 | 6 | 18 | Sim |

**AV-Comparatives Real-World Protection, fevereiro-maio 2026** ([relatório](https://www.av-comparatives.org/tests/real-world-protection-test-february-may-2026/), [factsheet](https://av-comparatives.org/consumer-real-world-protection-test-february-may-2026/)):

| Produto | Proteção | Falsos positivos | Prêmio |
|---|---|---|---|
| Kaspersky | 99,8% | 3 | ADVANCED+ |
| Bitdefender | 99,5% | 5 | ADVANCED+ |
| Avast / AVG | 99,3% | 5 | ADVANCED+ |
| Norton | 99,3% | 5 | ADVANCED+ |
| Microsoft Defender | 99,0% | **0** | ADVANCED+ |
| ESET | 98,5% | 2 | ADVANCED |
| Malwarebytes | 98,8% | **39** | STANDARD (rebaixado por falsos alarmes) |

**AV-Comparatives Performance Test, abril 2026** (Impact Score: menor = mais leve; máquina modesta) ([relatório](https://www.av-comparatives.org/tests/performance-test-april-2026/), [resumo](https://av-comparatives.org/consumer-performance-test-april-2026/)):

| Produto | Impact Score | Prêmio |
|---|---|---|
| Kaspersky Premium | 3,5 | ADVANCED+ |
| ESET HOME Security Essential | 4,2 | ADVANCED+ |
| Norton Antivirus Plus | 5,3 | ADVANCED+ |
| Avast Free / AVG Free | 5,5 | ADVANCED+ |
| Bitdefender Total Security | 9,6 | ADVANCED+ |
| Microsoft Defender | 12,9 | ADVANCED |
| Malwarebytes Premium | 17,6 | ADVANCED |

Leitura: Defender protege no nível dos pagos e quase não gera falsos alarmes, mas pesa mais no desempenho que Kaspersky, ESET, Norton e Avast. ESET e Kaspersky são os mais leves.

### 3.2 Produto a produto

- **Microsoft Defender:** grátis e embutido. Reviews o consideram "mais que sólido", um pouco atrás dos melhores pagos em detecção em alguns testes ([TechRadar](https://www.techradar.com/news/is-microsoft-defender-good-enough-for-your-pc), [Tom's Guide](https://www.tomsguide.com/uk/reviews/windows-defender)).
- **Bitdefender:** Antivirus Plus a partir de ~US$ 24,99 no 1º ano ([Capterra](https://capterra.com/p/266585/Bitdefender-Antivirus-Plus/)); citado como Editors' Choice PCMag 2026 por fontes secundárias ([Techloy](https://www.techloy.com/10-best-antivirus-software-in-2026-tested-for-windows-mac-mobile/), [kluczesoft](https://kluczesoft.pl/wiedza/bezpieczenstwo/ranking-antywirusow-2026)) **(não verificado diretamente na PCMag)**. Foi o mais pesado entre os ADVANCED+ no teste de desempenho.
- **Kaspersky:** melhor proteção e um dos mais leves nos testes, mas **proibido nos EUA** desde 2024 (vendas a partir de 20/07/2024, atualizações a partir de 29/09/2024); clientes americanos foram migrados automaticamente para o UltraAV, com muitas reclamações ([TechCrunch](https://techcrunch.com/2024/09/23/some-kaspersky-customers-receive-surprise-forced-update-to-new-antivirus-software), [Dark Reading](https://darkreading.com/application-security/kaspersky-rolls-back-us-customers-ultraav), [Tom's Hardware](https://tomshardware.com/software/antivirus/us-customers-wake-up-to-find-kaspersky-antivirus-replaced-with-ultraav-switchover-caught-many-users-by-surprise)). PCMag deixou de testá-lo (segundo [kluczesoft](https://kluczesoft.pl/wiedza/bezpieczenstwo/ranking-antywirusow-2026)).
- **Norton:** AntiVirus Plus ~US$ 29,99 no 1º ano (renovação mais cara) ([tech-insider](https://tech-insider.org/ca/best-antivirus-software-canada/)); Editors' Choice PCMag citado por fontes secundárias. Controvérsia de 2021: minerador de Ethereum (Norton Crypto) com 15% de comissão, difícil de remover ([TechRadar](https://www.techradar.com/news/norton-antivirus-under-fire-again-over-crypto-mining-feature), [BankInfoSecurity](https://www.bankinfosecurity.com/norton-360-offering-contains-controversial-cryptominer-a-18271), [PC Gamer](https://www.pcgamer.com/au/norton-installs-an-ethereum-crypto-miner-with-its-360-security-suite)).
- **ESET:** NOD32 a partir de ~US$ 39,99/ano; Essential ~US$ 49,99 ([eSecurity Planet](https://www.esecurityplanet.com/de/products/bitdefender-vs-eset-antivirus/)). Reconhecido pelo baixo impacto (Impact Score 4,2).
- **Avast/AVG (Gen Digital):** versões grátis com proteção de topo. Porém a FTC multou a Avast em US$ 16,5 milhões por vender dados de navegação re-identificáveis via Jumpshot a mais de 100 terceiros, e proibiu a venda desses dados para publicidade ([FTC](https://ftc.gov/news-events/news/press-releases/2025/02/ftc-announces-refund-claims-process-avast-customers-impacted-deceptive-privacy-claims), [CBS News](https://cbsnews.com/news/avast-antivirus-refund-ftc-what-to-know)).
- **Malwarebytes:** Premium ~US$ 44,99/ano (segundo guia PCMag 2025 citado em [pcrisk](https://www.pcrisk.com/blog/tips/14094-best-cheap-antivirus-programs)); proteção boa, mas **39 falsos positivos** no Real-World e o mais pesado dos avaliados aqui no teste de desempenho.

### 3.3 Implicações para o design
- Não implementar motor antivírus. Mostrar no painel o **status do Windows Security** (proteção em tempo real ligada, definições atualizadas, última verificação) e oferecer botão "Verificação rápida" que aciona o Defender. A Microsoft lista "verificar malware" entre as dicas oficiais de desempenho ([Microsoft Suporte](https://support.microsoft.com/pt-br/windows/dicas-para-melhorar-o-desempenho-do-pc-no-windows-b3b3ef5b-5953-fb6a-2528-4bbed82fba96)).
- Nunca oferecer "desativar Defender" como otimização (ver seção 4).
- O caso PC Manager ("Proteção" que só abre o Windows Security) foi visto como "isca" ([Windows Report](https://windowsreport.com/microsoft-pc-manager-review/)): se for apenas um atalho, rotular honestamente.

---

## 4. Otimizações: comprovadas vs. mitos/prejudiciais

### 4.1 Comprovadas / recomendadas pela Microsoft
Fonte principal: [Dicas para melhorar o desempenho do PC no Windows (Microsoft)](https://support.microsoft.com/pt-br/windows/dicas-para-melhorar-o-desempenho-do-pc-no-windows-b3b3ef5b-5953-fb6a-2528-4bbed82fba96), que lista, nesta ordem:

| # | Ação | Observação para o app |
|---|---|---|
| 1 | Windows Update e drivers opcionais | Mostrar atualizações pendentes; abrir o Windows Update |
| 2 | Verificar malware (Windows Security) | Integrar status/scan do Defender |
| 3 | Liberar espaço (Sensor de Armazenamento, Limpeza de Disco, temporários) | Núcleo do recurso de limpeza |
| 4 | Desinstalar apps não usados | Desinstalador com lista por tamanho/último uso |
| 5 | Desativar apps de inicialização | Gerenciador de inicialização com impacto estimado |
| 6 | Desativar apps em segundo plano | Lista com reversão |
| 7 | Efeitos visuais para melhor desempenho | Opcional, para PCs fracos |
| 8 | Modo de energia de melhor desempenho | Avisar: consome mais bateria e esquenta notebooks |
| 9 | Otimizar unidades | Desfragmentação só em HDD; TRIM em SSD |
| 10 | Monitorar recursos (Gerenciador de Tarefas) | Monitor em tempo real |
| 11 | Desativar notificações desnecessárias | Ganho pequeno |
| 12 | Fechar apps e reiniciar | Lembrete de tempo ligado (uptime) |

Complementos com fonte:
- **Sensor de Armazenamento (Storage Sense):** limpa temporários e Lixeira; vem desligado por padrão; atua apenas na unidade do sistema ([Microsoft Suporte](https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/manage-drive-space-with-storage-sense)). Bom candidato para "ativar com um clique".
- **Modo de Jogo:** ligado por padrão; reduz atividade em segundo plano e impede o Windows Update de instalar drivers ou pedir reinício durante o jogo ([Microsoft Suporte](https://support.microsoft.com/en-us/windows/using-game-mode-on-your-pc-5c94107e-a537-206e-e9e9-20600523bc43)). O "modo jogo" do app deve **complementar** (pausar tarefas do próprio app, ajustar plano de energia, prioridade estilo ProBalance), não duplicar.
- **SSD:** a operação padrão do `Optimize-Volume` para SSD é TRIM, não desfragmentação ([Microsoft TechNet](https://technet.microsoft.com/EN-US/library/hh848675(v=wps.620).aspx)); o Windows roda otimização semanal por padrão ([PCWorld](https://www.pcworld.com/article/2918994/optimize-your-ssds-speed-with-this-obscure-windows-setting.html)).
- **Ajuste dinâmico de prioridade** (ProBalance): reduz temporariamente a prioridade de processos de fundo que travam a interface ([Bitsum](https://bitsum.com/?p=4708)).
- **Ponto de restauração antes de mudanças**: prática adotada por WinUtil e O&O ShutUp10++ ([GitHub WinUtil](https://github.com/jadenblack/ChrisTitusTech-winutil), [O&O FAQ](https://manuals.oo-software.com/ooshutup10/docs/faq)).

### 4.2 Mitos ou prejudiciais

| "Otimização" | Veredito | Evidência |
|---|---|---|
| **Limpeza de registro** | Evitar | A Microsoft **não suporta** limpadores de registro; podem causar problemas graves que exigem reinstalar o Windows ([Microsoft KB 2563254](https://support.microsoft.com/en-us/topic/2563254)). Reclamações de instabilidade com o ASC ([AlternativeTo](https://www.alternativeto.net/software/advanced-systemcare-free/about/)). |
| **"Limpadores de RAM" / esvaziar lista standby** | Mito, contraproducente | RAM livre não é RAM útil; o Windows usa memória ociosa como cache; descartá-la força releitura do disco ([Make Tech Easier](https://maketecheasier.com/harmful-windows-tools/)). Exceção técnica: limpar a standby list apenas como diagnóstico de bugs específicos ([fórum Bitsum](https://community.bitsum.com/forum/index.php?msg=30475)). |
| **Apagar a pasta Prefetch** | Mito | A pasta se mantém sozinha (máx. ~128 entradas); apagar deixa Windows e apps mais lentos na próxima inicialização ([Microsoft Q&A](https://learn.microsoft.com/en-us/answers/a/2711969), [Computerworld](https://www.computerworld.com/article/1498081/big-lies-about-windows-performance-boosters.html), [Wikipedia](https://en.wikipedia.org/wiki/Prefetcher)). O próprio PC Manager foi criticado por isso ([HotHardware](https://amp.hothardware.com/news/microsoft-pc-manager-could-speed-up-performance-or-slow-it-down)). |
| **Desativar SysMain (Superfetch)** | Não recomendado como padrão | Não há documentação oficial da Microsoft recomendando desativar; o padrão do Windows é ativado. Conselhos para desativar vêm de fóruns, com efeito colateral de abertura mais lenta de apps ([Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/3163566/superfetch-disable), [Tech Community](https://techcommunity.microsoft.com/discussions/windowsinsiderprogram/system-windows-ram-problem-fix/4506405)). Aceitável apenas como passo de diagnóstico, com reversão. |
| **Desativar / fixar arquivo de paginação "1,5× RAM"** | Mito | Tamanho depende do commit peak e do tipo de despejo de memória, "não pode ser generalizado"; manter gerenciado pelo sistema ([Microsoft Learn](https://learn.microsoft.com/cs-cz/troubleshoot/windows-client/performance/how-to-determine-the-appropriate-page-file-size-for-64-bit-versions-of-windows)). |
| **Tweaks de rede (desligar auto-tuning TCP, etc.)** | Prejudicial | Em computadores de uso geral "os valores padrão não devem ser alterados" ([Microsoft Learn](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/tcpip-performance-known-issues)); nível padrão "Normal"; desligar impede a janela de crescer ([Microsoft Suporte](https://support.microsoft.com/en-us/help/4494445/tcp-receive-auto-tuning-level-feature-in-windows)); caso de download lento resolvido voltando ao normal ([Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/3910669/about-internet-speed)). |
| **Desfragmentar SSD** | Prejudicial (desgaste, sem ganho) | Windows faz TRIM em SSD por padrão ([Microsoft TechNet](https://technet.microsoft.com/EN-US/library/hh848675(v=wps.620).aspx), [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/2355339/ssd-drive-optimize)). |
| **Desativar Defender / VBS / Integridade de memória "para FPS"** | Perigoso | Integridade de memória (HVCI) é proteção de kernel ([Microsoft Learn VBS](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/oem-vbs)); desligar troca segurança por desempenho e é só passo de troubleshooting ([Microsoft Q&A](https://learn.microsoft.com/en-au/answers/questions/5866432/game-stuttering-on-windows-11-despite-stable-fps-p)); desativar Defender é "questionável" ([TechSpot](https://www.techspot.com/article/3095-stop-windows-slop/)). |
| **"Atualizadores de drivers" de terceiros** | Evitar | Marcam drivers como desatualizados sem necessidade e muitas vezes vêm com bundle/PUP; recomenda-se Windows Update ou site do fabricante ([Malwarebytes](https://www.malwarebytes.com/blog/news/2015/06/driver-updaters-digital-snake-oil-part-2), [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/a/2451748)). |
| **Debloat agressivo / serviços em manual** | Ganho mínimo, risco de quebra | Economia de no máximo 100-200 MB de RAM; atrasos de uso ([Yahoo Tech](https://tech.yahoo.com/computing/articles/tested-4-windows-debloating-tools-100000336.html)); grandes atualizações podem desfazer ([Club386](https://www.club386.com/how-to-debloat-windows-11-stop-spying/)). |
| **"Game boosters" que matam processos** | Ganho modesto | Poucos FPS em PCs fracos, nada em PCs fortes ([TechRadar](https://www.techradar.com/reviews/razer-cortex), [FileHorse](https://www.filehorse.com/download-razer-cortex-game-booster/)). |
| **HAGS ligar/desligar como "otimização"** | Depende do sistema | Recurso de redução de overhead da GPU ([DirectX Blog](https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/)), mas há relatos de stutter com ele ligado ([Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/5704471/valorant-on-windows-11-is-experiencing-subtle-but-persistent-frame-drops-and-stuttering-with-hags-enabled)). Apenas expor, não "otimizar" automaticamente. |

---

## 5. Padrões de UX que os usuários mais valorizam

Observados nos produtos líderes e nas reclamações/elogios:

1. **Verificação com um clique + resultado agrupado.** WinOptimizer agrupa em limpar/otimizar/proteger ([TechRadar](https://www.techradar.com/uk/best/pc-optimizer)); PC Manager tem "Boost" de um clique ([Windows Report](https://windowsreport.com/microsoft-pc-manager-review/)). Mostrar **o que será feito antes de fazer** e permitir desmarcar itens.
2. **Score de saúde**, desde que honesto: derivado de métricas reais (espaço livre, itens de inicialização, atualizações pendentes, status do antivírus, SMART do disco, temperatura) e não inflado para vender a versão paga. Reclamações de "assustar para vender" são o padrão do nagware.
3. **Gauges/gráficos em tempo real** de CPU, RAM, disco, GPU, rede e temperatura, com mínimo/máximo (estilo HWMonitor/HWiNFO) e histórico curto.
4. **Ícone/overlay na bandeja ou barra de tarefas** com velocidade de rede e uso (TrafficMonitor é muito elogiado justamente por isso) ([README](https://git.oschina.net/anunnaki/TrafficMonitor/blob/master/README_en-us.md)); tooltip com resumo; menu rápido.
5. **Modo jogo** automático por detecção de processo (Process Lasso Pro, Razer Cortex), com reversão automática ao sair do jogo.
6. **Semáforo de risco e reversibilidade:** recomendado / com ressalvas / avançado (padrão O&O ShutUp10++), botão "desfazer" por item e ponto de restauração automático ([O&O FAQ](https://manuals.oo-software.com/ooshutup10/docs/faq)).
7. **Analisador visual de disco** (treemap estilo WizTree) e **desinstalador com limpeza de restos** (Revo/BCU).
8. **Transparência e respeito:** sem bundle, sem pré-marcação de ofertas, telemetria opcional (desligada por padrão), fechar significa fechar, sem renovação automática escondida. Contraexemplos: CCleaner 5.45 ([CSO](https://www.csoonline.com/article/565979/avast-pulls-ccleaner-version-that-lacked-privacy-options-after-backlash.html)), Core Temp ([AnandTech](https://forums.anandtech.com/threads/is-coretemp-now-adware.2187454/post-37617305)), renovações CCleaner ([Trustpilot](https://uk.trustpilot.com/review/www.ccleaner.com?page=8)), migração UltraAV ([TechCrunch](https://techcrunch.com/2024/09/23/some-kaspersky-customers-receive-surprise-forced-update-to-new-antivirus-software)).
9. **Leveza do próprio app:** consumo baixo em segundo plano (exemplo negativo: NZXT CAM com 15-20% de CPU, [NZXT Ideas](https://cam-ideas.nzxt.com/ideas/CAM-I-7124)); versão portátil disponível é bem vista (O&O, WizTree, Core Temp ZIP).

---

## 6. Considerações de plataforma (Windows 10/11, x64 e x86)

- Windows 10 encerrou o suporte em 14/10/2025; o ESU gratuito para consumidores foi estendido até **12/10/2027** (Windows 10 22H2) ([BleepingComputer](https://bleepingcomputer.com/news/microsoft/microsoft-quietly-extends-free-windows-10-esu-support-to-october-2027), [Help Net Security](https://www.helpnetsecurity.com/2026/06/26/microsoft-windows-10-free-security-updates-esu-program/)). Uma parcela relevante de usuários continuará no Windows 10 até lá; o app pode exibir o status do ESU.
- O Windows 11 é somente 64 bits; o suporte a x86 serve essencialmente ao Windows 10 32 bits (máquinas antigas com pouca RAM), reforçando a necessidade de baixo consumo.
- Ashampoo WinOptimizer 2026 não suporta ARM ([Ashampoo](https://www.ashampoo.com/en-ie/winoptimizer-2026)); ARM64 não está no escopo deste projeto, mas fica registrado.
- Drivers de kernel para sensores precisam ser assinados e compatíveis com HVCI/lista de bloqueio de drivers vulneráveis; para x86, verificar se a biblioteca de sensores escolhida oferece driver 32 bits.

---

## 7. Recomendações consolidadas para o GROBS Optimizer

### 7.1 Recursos indispensáveis (prioridade)
1. Painel com score de saúde honesto e verificação com um clique (com pré-visualização e seleção).
2. Limpeza segura de arquivos temporários, caches, Lixeira, logs, relatórios de erro, cache de miniaturas/DirectX, restos do Windows Update (via mecanismos oficiais), com tamanho estimado; integração com o Sensor de Armazenamento.
3. Gerenciador de inicialização (apps, tarefas agendadas, serviços de terceiros) com impacto e reversão.
4. Monitor em tempo real: CPU, RAM, disco, rede, GPU; temperatura quando disponível; SMART dos discos.
5. Status de segurança (Defender/antivírus instalado, Firewall, Windows Update) com atalhos para as ferramentas oficiais.
6. Desinstalador com varredura de restos e desinstalação em lote.
7. Analisador de espaço em disco (treemap, maiores arquivos/pastas).
8. Modo jogo/desempenho: plano de energia, prioridade dinâmica, pausar tarefas do próprio app, reversão automática.
9. Bandeja/barra de tarefas com indicadores e menu rápido.
10. Privacidade: ajustes de telemetria do Windows em semáforo, com ponto de restauração e desfazer.

### 7.2 Recursos a evitar
Limpeza de registro; "RAM cleaner"; apagar Prefetch; desligar SysMain, arquivo de paginação, Defender, VBS/HVCI ou Windows Update como "otimização"; tweaks de rede (auto-tuning, etc.); desfragmentar SSD; atualizador de drivers de terceiros; debloat agressivo de serviços; driver WinRing0; bundle de software, ofertas pré-marcadas, telemetria obrigatória, alertas alarmistas para vender upgrade, renovação automática escondida.

### 7.3 Lacunas desta pesquisa
- Notas atuais da PCMag não foram acessadas diretamente; menções de Editors' Choice vêm de fontes secundárias.
- Preços atuais de Wise Care 365, Avast Cleanup, Revo Pro, HWMonitor Pro e TreeSize não foram confirmados.
- Não foi encontrada documentação oficial da Microsoft específica sobre SysMain nem sobre "limpadores de RAM"; as conclusões se baseiam no comportamento padrão do Windows e em fontes técnicas secundárias.
