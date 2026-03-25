# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## O Projeto

**Proxos** é uma biblioteca .NET de alto desempenho que implementa o padrão Mediator/CQRS como substituto do MediatR v12.x (motivação: licença comercial na v13+). Publicada no NuGet como `Proxos` e `Proxos.Testing`.

- **Multi-target:** net8.0, net9.0, net10.0
- **Solução:** `Proxos.slnx` (formato moderno)

## Comandos

### Build e Testes

```bash
# Restaurar dependências
dotnet restore

# Build
dotnet build -c Release

# Todos os testes (todos os frameworks)
dotnet test tests/Proxos.Tests/Proxos.Tests.csproj -c Release

# Teste em framework específico
dotnet test tests/Proxos.Tests/Proxos.Tests.csproj -c Release -f net9.0

# Teste único (por nome)
dotnet test tests/Proxos.Tests/Proxos.Tests.csproj -c Release --filter "FullyQualifiedName~SendTests"

# Teste com cobertura
dotnet test tests/Proxos.Tests/Proxos.Tests.csproj -c Release -f net8.0 \
  --collect "XPlat Code Coverage" --results-directory ./coverage
```

### Empacotamento NuGet

```bash
dotnet pack src/Proxos/Proxos.csproj -c Release -o ./nupkgs
dotnet pack src/Proxos.Testing/Proxos.Testing.csproj -c Release -o ./nupkgs
```

**Publicação:** Feita automaticamente via GitHub Actions ao criar tag `v*.*.*`.

## Arquitetura

### Projetos

| Projeto | Target | Papel |
|---------|--------|-------|
| `src/Proxos` | net8/9/10 | Biblioteca principal — mediator, pipeline, DI |
| `src/Proxos.Generator` | netstandard2.0 | Source Generator (registro compile-time) + Roslyn Analyzer |
| `src/Proxos.Testing` | net8/9/10 | `FakeMediator` com API fluente para testes |
| `tests/Proxos.Tests` | net8/9/10 | Suite xUnit (16 classes de teste) |

### Hot Path (Despacho Sem Reflection)

O caminho crítico de desempenho evita reflection usando um `FrozenDictionary<Type, RequestHandlerWrapperBase>`:

1. **Startup:** `WarmupService` (IHostedService) registra todos os handlers em `WrapperRegistry` e congela o dicionário.
2. **Runtime:** `Mediator.Send()` faz lookup O(1) no dicionário, obtém `RequestHandlerWrapper<TRequest, TResponse>` e executa o pipeline.
3. **Pipeline em `HandlerWrapper.cs`:** Pre-Processors → Behaviors → Handler → Post-Processors → Exception Handlers/Actions.

O `ServiceCollectionExtensions.cs` usa reflection **apenas no startup** para escanear assemblies.

### Source Generator e Analyzer (`src/Proxos.Generator`)

- **`HandlerRegistrationGenerator`**: Gera método `AddProxosGenerated()` com registros estáticos em compile-time, substituindo o scan por reflection.
- **`MissingHandlerAnalyzer`**: Emite diagnósticos PRX001/PRX002 se existir `IRequest<T>` ou `IStreamRequest<T>` sem handler correspondente.

### Contratos Públicos (`src/Proxos/Interfaces`)

| Interface | Propósito |
|-----------|-----------|
| `IRequest<TResponse>` | Request tipado com resposta |
| `IRequest` | Alias para `IRequest<Unit>` |
| `IStreamRequest<TResponse>` | Request retornando `IAsyncEnumerable<T>` |
| `INotification` | Evento fire-and-forget (N handlers) |
| `IPipelineBehavior<TReq, TRes>` | Middleware do pipeline |
| `IConditionalBehavior<TReq, TRes>` | Behavior com `ShouldHandle()` para filtro runtime |
| `IRequestPreProcessor<TReq>` | Executa antes do pipeline |
| `IRequestPostProcessor<TReq, TRes>` | Executa após o handler |
| `IRequestExceptionHandler<TReq, TRes, TEx>` | Pode suprimir ou transformar exceção |
| `IRequestExceptionAction<TReq, TEx>` | Side-effect em exceção (sempre relança) |

### Atributos Declarativos

```csharp
[RequestTimeout(5000)]                        // Cancela após 5 s automaticamente
[IgnoreBehavior(typeof(LoggingBehavior<,>))]  // Pula behavior específico por request
[BehaviorOrder(1)]                            // Controla ordem de execução dos behaviors
```

### Contexto de Pipeline

`IPipelineContextAccessor` — estado compartilhado via `AsyncLocal<T>` entre todos os estágios do pipeline sem threading de parâmetros.

### Estratégias de Publish

`PublishStrategy.ForeachAwait` (padrão), `WhenAll`, `WhenAllContinueOnError` — configurável globalmente em `ProxosConfiguration`.

### OpenTelemetry Embutido

- Traces: `ActivitySource` com nome `"Proxos"`
- Métricas: `Meter` com nome `"Proxos"` — counters e histogram de duração em `ProxosDiagnostics.cs`

## CI/CD

- **`ci.yml`**: Executa em Ubuntu + Windows, testa net8/9/10, faz upload de cobertura ao Codecov.
- **`publish.yml`**: Acionado por tag `v*.*.*` — testa, empacota e publica no NuGet.org.
