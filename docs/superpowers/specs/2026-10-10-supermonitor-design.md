# Supermonitor — Design

Data: 2026-10-10
Status: aprovado em conversa, aguardando revisão da spec

## 1. Objetivo

Supermonitor é um app desktop para Windows 11 voltado a gamers. Ele oferece:

- Dashboard detalhado de desempenho: CPU, RAM, GPU, discos, rede e processos.
- Visualização customizável.
- Overlay in-game customizável.
- Limitador de FPS e recursos para gamers: Modo Jogo automático, benchmark de sessão, hotkeys globais, limpeza da standby list de RAM.
- Consumo mínimo de recursos do próprio app.

### Critérios de sucesso

1. Com o dashboard aberto e atualizando a cada 1 s, o app usa menos de 1% de CPU em um Ryzen 7 7800X3D.
2. Com o dashboard fechado (só bandeja + overlay), o app usa menos de 0,3% de CPU.
3. Valores de CPU, RAM, disco e rede batem com o Gerenciador de Tarefas (tolerância de ±2 pontos percentuais ou ±5%).
4. O overlay mostra FPS e frametime reais de um jogo em modo janela ou borderless.
5. O limitador de FPS funciona na GPU AMD RX 9070 XT do usuário.

### Fora do escopo

- Overlay em fullscreen exclusivo (exigiria injeção no processo do jogo).
- Injeção de DLL ou hook de `Present()`.
- Pacote MSIX e Microsoft Store.
- Idiomas além de PT-BR.
- Processo agente separado (arquitetura de processo único foi escolhida).

## 2. Decisões

| Tema | Decisão |
|---|---|
| Arquitetura | Processo único WinUI 3 com ícone na bandeja |
| Linguagem/runtime | C#, .NET 10, Windows App SDK (última estável), x64 |
| Empacotamento | Unpackaged (`WindowsPackageType=None`), Windows App SDK self-contained |
| Privilégio | `requireAdministrator` no manifest; autostart por tarefa agendada com "executar com privilégios mais altos" |
| Sensores de hardware | LibreHardwareMonitorLib (driver assinado PawnIO) |
| Limitador de FPS | Driver: ADLX (AMD) e NVAPI (NVIDIA); sem injeção |
| Captura de FPS | Sessão ETW em tempo real (TraceEvent) |
| Overlay | Janela Win32 layered, click-through, Direct2D/DirectWrite via Vortice |
| Gráficos | Controle próprio em Win2D |
| MVVM | CommunityToolkit.Mvvm |
| Configurações | JSON em `%LocalAppData%\Supermonitor\settings.json`, System.Text.Json com source generator |
| Idioma da UI | PT-BR |

## 3. Arquitetura

### 3.1 Projetos

```
Supermonitor.sln
├─ src/Supermonitor            WinUI 3: janelas, páginas, ViewModels, bandeja, controles de gráfico
├─ src/Supermonitor.Core       Sem UI: sensores, SensorHub, FPS ETW, jogos, limitador, benchmark, RAM, hotkeys, configurações
├─ src/Supermonitor.Overlay    Janela overlay Win32 + renderização Direct2D
└─ tests/Supermonitor.Tests    xUnit para lógica do Core
```

Dependências: `Supermonitor` → `Supermonitor.Core`, `Supermonitor.Overlay`. `Supermonitor.Overlay` → `Supermonitor.Core`. `Supermonitor.Core` não depende de WinUI.

### 3.2 SensorHub

- Uma thread de fundo com `PeriodicTimer`.
- Três níveis de amostragem:
  - **Rápido** (padrão 250 ms): agregação de FPS/frametime.
  - **Normal** (padrão 1 s, configurável de 250 ms a 2 s): CPU, RAM, GPU, disco, rede.
  - **Lento** (5 s): espaço em disco, lista de adaptadores, dados SMART.
- Cada amostra é um struct gravado em um `RingBuffer<T>` pré-alocado. Histórico de 10 minutos. Nenhuma alocação por tick no caminho quente.
- Consumidores (dashboard, overlay, Modo Jogo, benchmark) assinam um evento `SampleReady` e leem o último valor ou o histórico. O hub não conhece os consumidores.
- Métricas caras só são amostradas quando algum consumidor as pede. Exemplos: lista de processos e uso por núcleo só com o dashboard visível; ETW só com jogo detectado ou overlay mostrando FPS.

### 3.3 Provedores

Cada provedor implementa `ISensorProvider` (`Initialize`, `Sample`, `IsAvailable`, `UnavailableReason`). Uma falha em um provedor nunca derruba o hub. A métrica afetada mostra "N/D" e o erro vai para o log.

| Área | Fonte | Métricas |
|---|---|---|
| CPU | PDH `\Processor Information(*)\% Processor Utility` (consulta única em lote) | Uso total e por núcleo lógico |
| CPU | LHM | Clock por núcleo, temperatura (Tctl/Tdie, CCD), potência do pacote, voltagem |
| RAM | `GlobalMemoryStatusEx`, `GetPerformanceInfo`, `NtQuerySystemInformation(SystemMemoryListInformation)` | Em uso, disponível, standby, livre, modificada, commit, cache |
| GPU | PDH `\GPU Engine(*)\Utilization Percentage`, `\GPU Adapter Memory(*)\*` | Uso por engine (3D, cópia, vídeo), uso por processo, VRAM dedicada e compartilhada |
| GPU | LHM | Temperatura (edge, hotspot, memória), clock, fan (RPM e %), potência |
| Disco | PDH `\PhysicalDisk(*)\*`, `DriveInfo` | Leitura/escrita por segundo, tempo ativo, fila, espaço livre |
| Disco | LHM | Temperatura SMART |
| Rede | `GetIfTable2` (deltas de `MIB_IF_ROW2`) | Upload/download por adaptador |
| Rede | ICMP para host configurável (padrão `1.1.1.1`, a cada 2 s) | Latência, jitter, perda |
| Processos | `NtQuerySystemInformation(SystemProcessInformation)` + PDH GPU por PID | Top N por CPU, RAM, GPU |
| FPS | ETW: `Microsoft-Windows-DxgKrnl` (eventos de present/flip) e `Microsoft-Windows-DXGI` | FPS, frametime, por processo |

### 3.4 Captura de FPS

- Sessão ETW nomeada `Supermonitor-FPS`. Ao iniciar, o app para qualquer sessão órfã com esse nome.
- Eventos DxgKrnl cobrem DX9–12, Vulkan e OpenGL. DXGI complementa para apps DXGI.
- O alvo é o processo do jogo em primeiro plano. Frametime é o intervalo entre presents consecutivos do processo alvo.
- `FrameTimeAggregator` (lógica pura, testável) recebe timestamps e produz FPS médio da janela, frametime atual e um buffer de frametimes para o mini-gráfico e o benchmark.

### 3.5 Overlay

- Janela Win32 com `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`. Não recebe cliques nem foco e não aparece no Alt+Tab.
- Renderização: Direct2D desenha em um DIB 32 bits premultiplicado, depois `UpdateLayeredWindow`. Texto via DirectWrite com antialiasing em tons de cinza.
- Redesenha só quando um valor exibido muda, limitado à taxa configurada (padrão 4 Hz; mini-gráfico de frametime até 30 Hz).
- Funciona em jogos em janela e borderless. Fullscreen exclusivo fica fora do escopo.

### 3.6 Ciclo de vida e consumo

- Fechar a janela principal destrói a janela e mantém o app na bandeja.
- Sem janela visível, o processo entra em EcoQoS (`SetProcessInformation` com `ProcessPowerThrottling`) e para a amostragem exclusiva do dashboard.
- Build: ReadyToRun, Tiered PGO, GC de workstation não concorrente.
- Instância única: um mutex nomeado. A segunda instância pede à primeira para abrir o dashboard e encerra.

## 4. Interface

### 4.1 Navegação

`NavigationView` à esquerda com as páginas:

1. **Visão geral**: grade de cards.
2. **CPU**: uso por núcleo, clocks, temperaturas, potência.
3. **Memória**: composição da RAM, commit, top processos por RAM, botão "Limpar standby".
4. **GPU**: uso por engine, VRAM, temperaturas, clock, fan, potência, uso por processo.
5. **Discos**: atividade e espaço por disco, temperatura.
6. **Rede**: tráfego por adaptador, latência/jitter/perda.
7. **Processos**: tabela ordenável por CPU, RAM, GPU.
8. **Jogos**: lista de jogos detectados e perfis, configuração do Modo Jogo.
9. **Benchmarks**: lista de sessões, gráfico de frametime, comparação entre sessões.
10. **Overlay**: editor com preview ao vivo.
11. **Configurações**: aparência, amostragem, unidades, hotkeys, inicialização, sensores.

### 4.2 Customização do dashboard

- Cards: mostrar, ocultar e reordenar por arraste; tamanho compacto, normal ou grande.
- Gráfico por card: linha, área ou barra.
- Janela de histórico: 1, 5 ou 10 minutos.
- Intervalo de atualização: 250 ms, 500 ms, 1 s ou 2 s.
- Tema: claro, escuro ou do sistema. Cor de destaque. Fundo Mica, Acrylic ou sólido.
- Unidades: bits ou bytes para rede; °C ou °F.
- Limiares de cor por métrica (ex.: temperatura acima de 80 °C em vermelho).

### 4.3 Gráficos

Controle `SparklineChart` e `TimeSeriesChart` próprios sobre Win2D `CanvasControl`. Leem diretamente do ring buffer. Pontos são reaproveitados entre frames, sem alocação por desenho. O desenho para quando o controle não está visível.

### 4.4 Customização do overlay

- Monitor alvo e posição: 9 âncoras + deslocamento em pixels.
- Métricas exibidas e ordem: FPS, frametime, 1% low, CPU uso/temp/clock/potência, GPU uso/temp/clock/potência/VRAM, RAM, rede, ping, relógio, estado do limitador.
- Layout: horizontal, vertical ou compacto.
- Fonte, tamanho, cor do rótulo e do valor por métrica, limiares de cor.
- Fundo: cor, opacidade, raio dos cantos, padding.
- Mini-gráfico de frametime liga/desliga, largura e altura.
- Taxa de atualização.
- Presets: Mínimo (só FPS), Padrão, Completo. O usuário pode salvar presets próprios.
- O editor mostra preview ao vivo usando o mesmo renderizador do overlay.

## 5. Recursos para gamers

### 5.1 Limitador de FPS

Interface `IFrameLimiter` (`IsSupported`, `UnsupportedReason`, `Apply(int fps)`, `Clear()`, `CurrentLimit`).

- **AMD (`AdlxFrameLimiter`)**: carrega `amdadlx64.dll` instalado pelo driver e chama ADLX pela ABI C (vtables via ponteiros de função). Usa Frame Rate Target Control quando suportado; senão usa Radeon Chill com mínimo = máximo = limite. O limite é global do driver.
- **NVIDIA (`NvapiFrameLimiter`)**: `nvapi64.dll`, Driver Settings (DRS), configuração `FRL_FPS` (0x10835002) no perfil da aplicação do jogo, ou no perfil base quando não há jogo. Marcado como experimental: sem hardware NVIDIA para teste.
- Sem GPU suportada: limitador desativado com o motivo na UI.
- Antes de alterar o driver, o app grava o valor original em `pending-restore.json`. Ao sair, ou ao iniciar após crash, restaura esse valor.

### 5.2 Modo Jogo

- **Detecção** (`GameDetector`, lógica testável): o processo em primeiro plano é jogo se:
  1. estiver na lista de jogos do usuário; ou
  2. o executável estiver em uma biblioteca Steam, Epic, GOG, EA ou Ubisoft conhecida; ou
  3. o processo apresentar frames via ETW com janela cobrindo o monitor inteiro.
  Lista de exclusão do usuário vence as regras 2 e 3. Jogos detectados pelas regras 2 e 3 entram na lista para o usuário confirmar.
- **Perfil por jogo** com ações opcionais:
  - plano de energia (ex.: Desempenho Máximo);
  - prioridade do processo do jogo (Normal, Acima do normal, Alta);
  - limite de FPS;
  - limpar standby ao iniciar;
  - mostrar overlay;
  - iniciar benchmark automaticamente.
- Perfil padrão usado para jogos sem perfil próprio.
- Ao sair do jogo, todas as alterações são revertidas. O estado alterado também vai para `pending-restore.json`.

### 5.3 Benchmark de sessão

- Início e fim por hotkey, pelo botão na UI ou pelo Modo Jogo.
- Grava todos os frametimes do processo alvo em buffer pré-alocado que cresce em blocos.
- `BenchmarkStatistics` (lógica pura) calcula:
  - FPS médio = quantidade de frames / duração;
  - 1% low = FPS correspondente ao percentil 99 de frametime;
  - 0,1% low = FPS correspondente ao percentil 99,9 de frametime;
  - FPS mínimo e máximo, frametime médio e mediano;
  - stutters = frames com frametime maior que 2× a mediana móvel.
- Saída em `Documentos\Supermonitor\Benchmarks\<jogo>_<data-hora>.csv` (frametimes, CPU/GPU uso e temperatura por segundo) + `.json` (resumo).
- Página Benchmarks: lista, gráfico de frametime, comparação lado a lado de sessões.

### 5.4 Hotkeys globais

`RegisterHotKey` em uma janela message-only. Ações configuráveis com padrões:

| Ação | Padrão |
|---|---|
| Mostrar/ocultar overlay | `Ctrl+Shift+O` |
| Ligar/desligar limitador | `Ctrl+Shift+L` |
| Iniciar/parar benchmark | `Ctrl+Shift+B` |
| Limpar standby | `Ctrl+Shift+M` |
| Abrir dashboard | `Ctrl+Shift+S` |

Conflito de registro: a UI marca a hotkey como indisponível.

### 5.5 Limpeza da standby list

- `NtSetSystemInformation(SystemMemoryListInformation, MemoryPurgeStandbyList)` com o privilégio `SeProfileSingleProcessPrivilege` habilitado.
- Manual: botão na página Memória e hotkey.
- Automático (desligado por padrão): a cada 5 s, limpa se standby > X GB **e** livre < Y GB. Padrões: X = 1 GB, Y = 1 GB. Intervalo mínimo de 60 s entre limpezas.

## 6. Configurações e persistência

- `AppSettings` com número de versão e migrações (`SettingsMigrator`, testável).
- Gravação com debounce de 500 ms e escrita atômica (arquivo temporário + rename).
- Arquivo corrompido: renomeado para `.bak` e o app usa os padrões, com aviso.
- Logs em `%LocalAppData%\Supermonitor\logs`, rotação diária, 7 dias.

## 7. Tratamento de erros

| Situação | Comportamento |
|---|---|
| Driver PawnIO ausente | Métricas LHM mostram "N/D"; banner com botão para instalar |
| ADLX/NVAPI ausente ou sem suporte | Limitador desativado com o motivo |
| Falha ao criar sessão ETW | FPS "N/D"; motivo na UI; nova tentativa a cada 30 s |
| Contador PDH ausente | Métrica "N/D" |
| Exceção em provedor | Capturada no hub, log, provedor marcado indisponível após 5 falhas seguidas |
| Crash com alterações aplicadas | `pending-restore.json` restaurado no próximo início |
| Hotkey já em uso | Marcada como indisponível na UI |

## 8. Testes

### 8.1 Unitários (xUnit, `Supermonitor.Tests`)

- `RingBuffer<T>`: capacidade, sobrescrita, leitura de janela.
- `FrameTimeAggregator`: eventos sintéticos de present → FPS e frametime.
- `BenchmarkStatistics`: média, 1% low, 0,1% low, stutters com dados conhecidos.
- `GameDetector`: lista do usuário, bibliotecas, exclusões.
- `SettingsMigrator` e serialização de `AppSettings`.
- `StandbyCleanerPolicy`: limiares e intervalo mínimo.
- Formatação de unidades.

### 8.2 Fumaça na máquina real

Teste marcado com trait `Hardware`, fora da execução padrão: inicializa cada provedor e verifica `IsAvailable` e valores dentro de faixas plausíveis.

### 8.3 Verificação manual

1. Comparar CPU, RAM, disco e rede com o Gerenciador de Tarefas.
2. Medir CPU do próprio app com o dashboard aberto e fechado (critérios 1 e 2).
3. Abrir um jogo borderless, conferir overlay e FPS.
4. Aplicar limite de FPS e conferir no overlay.
5. Rodar um benchmark e conferir CSV e resumo.
6. Encerrar o app à força com Modo Jogo ativo e conferir a restauração no próximo início.
