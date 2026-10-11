# Supermonitor Plan 1 — Results

Date: 2026-10-10
Machine: Ryzen 7 7800X3D, 32 GB RAM, AMD Radeon RX 9070 XT, Windows 11 Pro 10.0.26340

## Performance (Release build, 1 s interval, 60 s measurement with `tools/measure-cpu.ps1`)

| Scenario | CPU | Working set | Private | Criterion | Result |
|---|---|---|---|---|---|
| Dashboard open (Overview) | 0.19% | 184 MB | 185 MB | < 1% | OK |
| Window closed (tray only, EcoQoS) | 0.01% | 184 MB | 182 MB | < 0.3% | OK |

Debug reference, dashboard open: 0.59% CPU, 204 MB.

Note: memory does not drop when the window is closed. The WinUI 3 runtime stays loaded in the process, as expected given the single-process architecture choice.

## Tests

- Unit: 92/92 pass (`dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`).
- Hardware: 6/6 pass (`--filter-trait "Category=Hardware"`): CPU, memory, disk, disk space, network, GPU.

## Manual verification (done by the user)

- CPU, RAM, GPU, disk and network values match Task Manager.
- Minimizing pauses updates; restoring resumes them.
- Tray: open, closing with the X keeps the process, Exit shuts down cleanly (log "Supermonitor stopped").
- Settings: interval, ping host with validation, start with Windows, start minimized.
- Second run of the exe brings the existing window to the front, with no second process.
- Tray icon comes back on its own after restarting Windows Explorer.

## Deviations from the plan

- xUnit v3 4.x only runs via Microsoft.Testing.Platform on .NET 10: `global.json` gained `"test": { "runner": "Microsoft.Testing.Platform" }`; `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` were removed; filters became `--filter-trait` / `--filter-not-trait`.
- `tests/Supermonitor.Tests.csproj` gained `<Using Include="Xunit" />`.
- `Program.cs`: `_ = new App(...)` assigned to the lambda parameter `_`; changed to `new App(args, instance);`.
- `.gitignore` includes `.superpowers/`.
- The Windows App SDK component packages (`WinUI` 2.3.9 + `Foundation` 2.3.12) worked; the metapackage was not needed.
