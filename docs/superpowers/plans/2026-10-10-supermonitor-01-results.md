# Supermonitor Plano 1 — Resultados

Data: 2026-10-10
Máquina: Ryzen 7 7800X3D, 32 GB RAM, AMD Radeon RX 9070 XT, Windows 11 Pro 10.0.26340

## Desempenho (build Release, intervalo 1 s, medição de 60 s com `tools/measure-cpu.ps1`)

| Cenário | CPU | Working set | Privada | Critério | Resultado |
|---|---|---|---|---|---|
| Dashboard aberto (Overview) | 0,19% | 184 MB | 185 MB | < 1% | OK |
| Janela fechada (só bandeja, EcoQoS) | 0,01% | 184 MB | 182 MB | < 0,3% | OK |

Referência Debug, dashboard aberto: 0,59% CPU, 204 MB.

Observação: a memória não cai ao fechar a janela. O runtime WinUI 3 continua carregado no processo, como previsto na escolha da arquitetura de processo único.

## Testes

- Unidade: 92/92 passam (`dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`).
- Hardware: 6/6 passam (`--filter-trait "Category=Hardware"`): CPU, memória, disco, espaço em disco, rede, GPU.

## Verificação manual (feita pelo usuário)

- Valores de CPU, RAM, GPU, disco e rede batem com o Gerenciador de Tarefas.
- Minimizar pausa a atualização; restaurar retoma.
- Bandeja: abrir, fechar no X mantendo o processo, Exit encerra limpo (log "Supermonitor stopped").
- Settings: intervalo, host de ping com validação, iniciar com o Windows, iniciar minimizado.

## Desvios do plano

- xUnit v3 4.x só roda via Microsoft.Testing.Platform no .NET 10: `global.json` ganhou `"test": { "runner": "Microsoft.Testing.Platform" }`; removidos `Microsoft.NET.Test.Sdk` e `xunit.runner.visualstudio`; filtros viraram `--filter-trait` / `--filter-not-trait`.
- `tests/Supermonitor.Tests.csproj` ganhou `<Using Include="Xunit" />`.
- `Program.cs`: `_ = new App(...)` atribuía ao parâmetro `_` do lambda; trocado por `new App(args, instance);`.
- `.gitignore` inclui `.superpowers/`.
- Pacotes de componente do Windows App SDK (`WinUI` 2.3.9 + `Foundation` 2.3.12) funcionaram; o metapacote não foi necessário.
