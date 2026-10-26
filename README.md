<p align="center">
  <img src="docs/img/optimizer-512.png" width="160" alt="GROBS Optimizer" />
</p>

<h1 align="center">GROBS Optimizer</h1>

<p align="center">Otimizador, monitor e central de manutenção para Windows — leve, animado, sem anúncios e sem telemetria.</p>

<p align="center">
  <a href="https://github.com/clatzao/GROBS-Optimizer/releases/latest/download/GROBS-Optimizer-Setup.exe"><b>⬇️ Baixar o instalador (Windows 10/11, 32 e 64 bits)</b></a>
</p>

<p align="center">
  <a href="https://github.com/clatzao/GROBS-Optimizer/releases/latest/download/GROBS-Optimizer-x64.exe">Portátil 64 bits</a> ·
  <a href="https://github.com/clatzao/GROBS-Optimizer/releases/latest/download/GROBS-Optimizer-x86.exe">Portátil 32 bits</a> ·
  <a href="https://github.com/clatzao/GROBS-Optimizer/releases/latest">Todas as versões</a>
</p>

---

## Instalar

1. Baixe o **[GROBS-Optimizer-Setup.exe](https://github.com/clatzao/GROBS-Optimizer/releases/latest/download/GROBS-Optimizer-Setup.exe)** (~4,5 MB).
2. Abra e aprove o pedido de administrador.
3. Escolha os módulos — *Tudo*, *Gamer*, *Só monitor*, *Só drivers* ou do seu jeito — e clique em **Instalar**.

Os módulos podem ser ligados e desligados depois em **Configurações → Módulos**.
Requisitos: Windows 10/11 (já trazem .NET 4.8 e WebView2). Windows 7 SP1: instale antes o .NET 4.8 e o WebView2 Runtime 109.

## O que tem

| Área | Recursos |
|---|---|
| **Painel** | Score de saúde, verificação com um clique, gráficos em tempo real |
| **Monitor** | CPU por núcleo, RAM, GPU, disco, rede, temperaturas, sensores completos (LibreHardwareMonitor), S.M.A.R.T., processos com *finalizar à força* e lista de apps "Não respondendo" |
| **Diagnóstico** | Lê os registros do Windows (telas azuis, desligamentos, erros de disco/hardware, travamentos, falhas do Update, drivers) e resolve com um clique — ou *Resolver tudo* |
| **Limpeza** | Temporários, caches de navegadores/apps, Windows Update, Lixeira — sem limpeza de registro |
| **Otimização** | Inicialização, ajustes de desempenho e privacidade, planos de energia, Modo Turbo |
| **Serviços / Recursos** | Desativar serviços e recursos da Microsoft (Copilot, Recall, Cortana, Widgets, anúncios, apps pré-instalados) — tudo reversível |
| **Efeitos visuais** | Desempenho máximo, equilibrado ou padrão; efeito por efeito |
| **Windows Update** | Pausar, adiar, desativar (reversível), limpar cache manual ou agendado |
| **Segurança** | Microsoft Defender, firewall, UAC, hosts, proxy |
| **Gamer** | Busca automática de jogos (Steam, Epic, GOG, Ubisoft, EA, Riot, Xbox...), otimização por jogo (GPU de alto desempenho, tela cheia exclusiva, prioridade, Turbo automático, perfil do driver NVIDIA), teste de ping/jitter/perda, DNS mais rápido, Nagle |
| **Overlay de FPS** | FPS, 1% low, frametime, CPU/GPU, temperaturas, RAM — escolha o que mostrar (Ctrl+Shift+F10) |
| **Overclock** | GPU NVIDIA: clock, memória, limite de energia e ventoinha, com aviso de risco |
| **Drivers** | Dispositivos sem driver, catálogo do Windows Update, versões mais novas da NVIDIA/Intel |
| **Embelezar** | Modo escuro, cor de destaque, barra de tarefas, menu de contexto clássico, Explorer |
| **Ferramentas** | Desinstalador com ícones e limpeza de resíduos, analisador de disco (treemap), utilitários |

**Leve:** ~3–4% de CPU com o painel aberto num i5 de 3ª geração; ~0% minimizado (a interface é suspensa).

## Compilar

Requer Node.js 18+ e .NET SDK 6+:

```bat
build.cmd
```

Gera em `dist\` o instalador e as versões portáteis x64/x86.

- `ui/` — interface React + Vite (componentes de React Bits, Magic UI/21st.dev e Uiverse.io)
- `host/` — C# .NET 4.8 + WebView2 (acesso ao sistema)
- `installer/` — instalador com escolha de módulos (`--silent` para scripts)
- `docs/PESQUISA.md` — pesquisa de concorrentes que embasou o app
