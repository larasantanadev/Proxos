# Changelog

Todas as mudanças notáveis são documentadas aqui.

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/).
Segue [Semantic Versioning](https://semver.org/lang/pt-BR/).

---

## [Unreleased]

---

## [1.0.2] — 2026-03-17

### Adicionado
- Multi-targeting: suporte a `net8.0`, `net9.0` e `net10.0`
- `AddProxos()` — nome consistente com o pacote NuGet `Proxos`
- Fix do Analyzer HRM001/HRM002 para handlers em assemblies referenciados (cross-assembly)
- GitHub Actions: CI automático (testes nos 3 TFMs em Ubuntu e Windows)
- GitHub Actions: publicação automática no NuGet.org via tag `v*.*.*`
- CHANGELOG.md

### Alterado
- Namespace de `Proxos` → `Proxos` (todos os 52 arquivos)
- `PackageId` de `Proxos` → `Proxos`
- `PackageId` de `Proxos.Testing` → `Proxos.Testing`
- README.md atualizado com nomes de pacote corretos

### Obsoleto
- `AddProxos()` marcado como `[Obsolete]` — use `AddProxos()`. Será removido na v2.0.0.

---

## [1.0.1] — 2026-03-15

### Adicionado
- Source Generator `HandlerRegistrationGenerator` — gera `AddProxosGenerated()` em compile-time
- Roslyn Analyzer HRM001 — detecta `IRequest<T>` sem handler em compile-time
- Roslyn Analyzer HRM002 — detecta `IStreamRequest<T>` sem handler em compile-time
- `#nullable enable` no código gerado (fix CS8669)

### Alterado
- `BehaviorOrderAttribute` aplicado também a `IStreamPipelineBehavior` (estava só em `IPipelineBehavior`)
- Mensagem de erro do `Send(object)` melhorada quando o tipo não implementa `IRequest<T>`

---

## [1.0.0] — 2026-03-14

### Adicionado
- `IRequest<TResponse>` / `IRequest` (alias para `IRequest<Unit>`)
- `IRequestHandler<TRequest, TResponse>` / `IRequestHandler<TRequest>` (alias Unit)
- `INotification` / `INotificationHandler<T>`
- `IStreamRequest<TResponse>` / `IStreamRequestHandler<TRequest, TResponse>`
- `IPipelineBehavior<TRequest, TResponse>` com `BehaviorOrderAttribute`
- `IStreamPipelineBehavior<TRequest, TResponse>`
- `IConditionalBehavior<TRequest, TResponse>` — skip automático sem overhead
- `IRequestPreProcessor<TRequest>` / `IRequestPostProcessor<TRequest, TResponse>`
- `IRequestExceptionHandler<TRequest, TResponse, TException>` — pode suprimir exceção
- `IRequestExceptionAction<TRequest, TException>` — side-effect, sempre relança
- `[RequestTimeout(ms)]` — timeout declarativo por request
- `[IgnoreBehavior(typeof(...))]` — ignora behavior específico por request
- `IPipelineContextAccessor` — contexto compartilhado via `AsyncLocal<T>`
- `PublishStrategy`: `ForeachAwait`, `WhenAll`, `WhenAllContinueOnError`
- `FrozenDictionary` no hot path — zero reflection após startup
- `WarmupService` — congela o dicionário no `IHostedService.StartAsync`
- `OpenTelemetry` integrado: traces (`ActivitySource`) e métricas (`Meter`)
- `FakeMediator` para testes — setup fluente + assertions
- `AddProxos()` — extensão de DI (renomeada para `AddProxos()` na v1.0.2)
- MIT License
