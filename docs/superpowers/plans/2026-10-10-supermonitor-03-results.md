# Supermonitor Plano 3 — Resultados

Data: 2026-10-11
Build: Release x64, branch `feat/fps-overlay`, executado como administrador.
Máquina: Ryzen 7 7800X3D, RX 9070 XT, Windows 11 26H2, monitor principal 2560×1440 a 240 Hz.

## Testes

`dotnet test --project tests/Supermonitor.Tests` (unidade + hardware, shell sem elevação): 328 no total, 326 aprovados, 0 falhas, 2 ignorados (testes ETW que exigem administrador).

Os testes ETW (`FrameCaptureSmokeTests`) foram rodados também com elevação (pwsh lançado com UAC): 3 aprovados e 1 ignorado (o caso "sem administrador", que só vale fora de elevação). A sessão órfã é substituída e a sessão some no `Dispose`.

## Desempenho (60 s por cenário, `tools/measure-cpu.ps1`, intervalo 1 s)

| Cenário | CPU | Working set | Private | Critério | Resultado |
|---|---|---|---|---|---|
| Janela principal aberta (não conferi qual página) + overlay ligado (Default) | 0,21% | 292 MB | 282 MB | < 1% | Passou |
| Só bandeja + overlay ligado (Default, gráfico a 30 Hz, janela animando em primeiro plano) | 0,15% | 302 MB | 291 MB | < 0,3% | Passou |
| Só bandeja, overlay desligado | 0,01% | 300 MB | 289 MB | < 0,3% | Passou |

Duas medições anteriores de "só bandeja" (0,19% com overlay ligado e 0,15–0,17% com overlay desligado) foram descartadas: a janela principal ainda estava aberta (conferido com `MainWindowHandle`). Com a janela de fato fechada e o overlay desligado, o custo voltou a 0,01%, o mesmo do Plano 2, e a sessão ETW não existe (`logman query -ets` sem `Supermonitor-FPS`), o que mostra que as leases de FPS e de hardware foram liberadas. O custo do overlay ligado (≈0,14 ponto) vem do mini-gráfico a 30 Hz e do processamento dos eventos ETW. O preset Minimal (4 Hz, sem gráfico) não foi medido.

## FPS real (critério 4 da spec)

Verificado pelo usuário com um jogo em modo janela/borderless: o FPS do overlay bate com o do jogo e o overlay funciona. Não houve comparação numérica com uma segunda ferramenta além dessa confirmação. DX11/DX12 funcionou só com o evento `Present` do DxgKrnl, então o fallback pelo DXGI não foi necessário.

## Recuperação após falha

Com o overlay ligado, `Stop-Process -Force` no app deixou a sessão `Supermonitor-FPS` ativa (`logman query -ets`). Ao reabrir o app, o log mostra "Frame capture started" e a lista de sessões tem uma única `Supermonitor-FPS`. O teste de hardware `Etw_source_replaces_an_orphan_session_with_the_same_name` cobre o mesmo caso.

## Verificações manuais (Tasks 8 e 9)

Confirmadas pelo usuário: o overlay funciona (liga e desliga pelo item "Show overlay" da bandeja, repetidas vezes, e continua com a janela principal fechada). A página Overlay (editor, presets, preview) compilou e abre, mas o usuário não relatou o roteiro completo do Step 6 da Task 9; ele fica como verificação pendente.

## Desvios do plano e decisões

- Captura só pelo evento `Present` (id 184) do DxgKrnl; o DXGI da spec 3.4 não é usado. Motivo: o evento DXGI não chegou para o processo testado (Chromium usa DirectComposition) e o DxgKrnl traz o PID correto. O fallback ficou documentado e não foi preciso.
- FPS, frametime e 1% low não são métricas da `MetricStore` (não há cards de FPS no dashboard); o overlay lê direto do `FrameCaptureService`.
- O item "estado do limitador" do overlay (spec 4.4) fica para o Plano 4, com o `IFrameLimiter`.
- O alvo do FPS é o processo em primeiro plano, qualquer app. A restrição a jogos detectados vem com o `GameDetector` do Plano 4.
- O teste existente `Version_one_overview_cards_become_v2_cards` passou a afirmar `SettingsMigrator.CurrentVersion` (um documento v1 agora migra até o v3); o plano não previa essa mudança.
- Uma instância elevada do app mantém o overlay em EcoQoS quando a janela fecha; a medição acima mostra que o custo continua dentro do critério.
