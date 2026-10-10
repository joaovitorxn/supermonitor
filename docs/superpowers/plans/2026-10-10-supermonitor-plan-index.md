# Supermonitor — Índice de Planos

Spec: `docs/superpowers/specs/2026-10-10-supermonitor-design.md`

A spec cobre quatro subsistemas. Cada plano entrega software funcional e testável por si. Os planos 2–4 são escritos depois que o plano anterior é concluído, porque dependem de APIs e nomes reais criados antes.

| # | Plano | Entrega | Seções da spec |
|---|---|---|---|
| 1 | `2026-10-10-supermonitor-01-foundation.md` | Solução, Core (ring buffer, métricas, SensorHub, configurações, log), provedores CPU/RAM/GPU (PDH)/disco/rede/ping, app WinUI 3 com bandeja, instância única, EcoQoS, autostart, página Overview e Settings básicas | 2, 3.1, 3.2, 3.3 (exceto LHM e processos), 3.6, 4.1 (Overview/Settings), 6, 7 (parcial), 8 |
| 2 | `…-02-hardware-and-dashboard.md` | LibreHardwareMonitor (PawnIO, banner de instalação), páginas CPU/Memory/GPU/Disks/Network/Processes, DemandTracker, customização completa do dashboard (cards, gráficos, histórico, tema, destaque, fundo, unidades, limiares) | 3.3 (LHM, processos), 4.1–4.3 |
| 3 | `…-03-fps-and-overlay.md` | Sessão ETW, FrameTimeAggregator, janela overlay Direct2D, editor com preview, presets | 3.4, 3.5, 4.4 |
| 4 | `…-04-gamer-features.md` | GameDetector, Modo Jogo e perfis, pending-restore, limitador ADLX/NVAPI, benchmark e página, hotkeys, limpeza de standby | 5, 7 (restante) |
