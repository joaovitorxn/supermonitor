# Supermonitor Plano 2 — Resultados

Data: 2026-10-10 (medições em 2026-10-11 pelo relógio do host)
Build: Release x64, branch `feat/hardware-dashboard`, executado como administrador.
Máquina: Ryzen 7 7800X3D, RX 9070 XT, Windows 11 26H2.

## Testes

`dotnet test --project tests/Supermonitor.Tests` (unidade + hardware): 215 aprovados, 0 falhas, 0 ignorados.

## Desempenho (60 s por cenário, `tools/measure-cpu.ps1`)

| Cenário | CPU | Working set | Private | Critério | Resultado |
|---|---|---|---|---|---|
| Overview aberto, intervalo 1 s | 0,27% | 291 MB | 275 MB | < 1% | Passou |
| Página Processes aberta | 0,31% | 301 MB | 286 MB | sem critério | Referência para o Plano 3 |
| Janela fechada (só bandeja) | 0,01% | 301 MB | 285 MB | < 0,3% | Passou |

O cenário só-bandeja confirma que as leases de LHM e de processos são liberadas ao fechar a janela.

## Verificações manuais (Tasks 10–14)

Verificadas pelo usuário no app em execução: aparência (Mica, sem banner do PawnIO, cards ao vivo), página de configurações, tema claro, cor de destaque em todos os controles WinUI, categorias, página Processes e reordenação de cards (arraste e menu Mover antes/depois).

## Desvios do plano e decisões

- Task 1: `CpuProvider` limita o uso por núcleo a 0..100, como o Gerenciador de Tarefas; o PDH `% Processor Utility` chegava a 119% com turbo.
- Task 10: `RestartArgumentsTests` usa linhas de comando separadas por espaço, porque `InlineData(string[])` gera CS0182. Configurações v1 migradas mantêm os 8 cards do usuário, então cards de temperatura não aparecem no Overview até serem adicionados.
- Task 11: o commit `0d6925b` entrou com dois arquivos não gravados e o `app.sh` mascarou a falha de build; corrigido em commit seguinte e o script agora sai com o código do build. Um commit intermediário quebrado permanece no histórico.
- Task 11: o arraste OLE do WinUI não funciona em apps elevados (UIPI). A reordenação usa captura de ponteiro. Depois disso, `ObservableCollection.Move` não gerava novo layout no `ItemsRepeater`; a reordenação passou a usar `RemoveAt` + `Insert`. O card segue o ponteiro durante o arraste, desliza até o novo lugar ao soltar (animação de `Translation` em `CardWrapLayout`, chaveada por `MetricId`) e volta ao lugar se solto fora de um card.
- Task 14: o tema claro travava (AV 0xc0000005 em CoreMessaging) por `Apply` reentrante a partir de `ActualThemeChanged` trocando o `SystemBackdrop`; corrigido sem teste unitário por ser comportamento nativo do WinUI.
- Task 14: a cor de destaque personalizada foi estendida de gráficos para todos os controles WinUI via overrides de `SystemAccentColor*` (TDD em `AccentShades`), com refresh por troca de tema.
