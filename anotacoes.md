# Anotações — Proxos

---

## Aguardando resposta do NuGet

### ⏳ Deleção permanente solicitada ao suporte do NuGet.org — aguardando e-mail
Mensagem enviada em 2026-03-17. Resposta virá pelo e-mail da conta larasantanadev.

Quando confirmar, publicar via tag git:
```bash
git tag v1.0.0
git push origin v1.0.0
```
O GitHub Actions empacota e publica automaticamente.

---

## 🔴 PRÓXIMA SESSÃO — Otimizar performance (fazer ANTES de tudo)

### Objetivo
Fazer o Proxos bater o MediatR em benchmark. Resultado atual (ruim):
- Send: MediatR 256ns/544B × Proxos 1.065µs/1.368B (4× mais lento)
- Publish: MediatR 226ns/448B × Proxos 562ns/928B (2.5× mais lento)

### Causa raiz identificada
Toda chamada a `Send()` paga, mesmo sem behaviors e sem OTel configurado:
1. `new ProxosPipelineContext { ... }` — alocação
2. `PipelineContextAccessor.SetContext()` — escreve `AsyncLocal.Value` → força cópia de `ExecutionContext` em todo `await` subsequente
3. `new ContextScope()` — IDisposable alocado
4. `Stopwatch.StartNew()` — alocação
5. `ProxosDiagnostics.RequestsTotal.Add(1, [tag])` — chamada desnecessária sem OTel
6. `ProxosDiagnostics.StartRequestActivity(...)` — chamada desnecessária sem OTel
7. `GetServices<IPipelineBehavior<TRequest, TResponse>>()` — aloca enumerable mesmo vazio
8. `.Where(b => !IsIgnored(b)).OrderBy(GetBehaviorOrder).ToArray()` — LINQ chain sempre
9. `GetServices<IRequestPreProcessor<>>()` — idem
10. `GetServices<IRequestPostProcessor<>>()` — idem
11. Lambda closure `() => handler.Handle(request, ct)` — alocada sempre

### Solução completa a implementar

#### 1. HandlerWrapper.cs — fast path estático por tipo (`src/Proxos/Internal/HandlerWrapper.cs`)

Campos estáticos a adicionar (em genérics, static é per-type instantiation — perfeito):
```csharp
// 0 = desconhecido, 1 = simples (sem behaviors/processors), 2 = completo
private static volatile int _pipelineState;
```

Método `Probe` (chamado uma vez na primeira invocação):
```csharp
private static int ProbePipeline(IServiceProvider sp)
{
    bool hasBehaviors    = sp.GetServices<IPipelineBehavior<TRequest, TResponse>>().Any();
    bool hasPreProc      = sp.GetServices<IRequestPreProcessor<TRequest>>().Any();
    bool hasPostProc     = sp.GetServices<IRequestPostProcessor<TRequest, TResponse>>().Any();
    int state = (!hasBehaviors && !hasPreProc && !hasPostProc) ? 1 : 2;
    _pipelineState = state; // int write é atomic
    return state;
}
```

Novo `Handle()` — não-async, delega para fast ou slow path:
```csharp
internal override Task<object?> Handle(
    object request, IServiceProvider sp, CancellationToken ct)
{
    var typedRequest = (TRequest)request;

    int state = _pipelineState;
    if (state == 0) state = ProbePipeline(sp);

    bool otelActive = ProxosDiagnostics.ActivitySource.HasListeners()
                   || ProxosDiagnostics.RequestsTotal.Enabled;

    if (state == 1 && !TimeoutMs.HasValue && !otelActive)
        return SimplePath(typedRequest, sp, ct);   // ← FAST PATH

    return FullPath(typedRequest, sp, ct, otelActive);
}
```

`SimplePath` — handler direto, sem nada:
```csharp
private static async Task<object?> SimplePath(
    TRequest request, IServiceProvider sp, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    var handler = sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
    return await handler.Handle(request, ct);
}
```

`FullPath` — conteúdo do `Handle()` atual, mas com gate de OTel:
```csharp
private static async Task<object?> FullPath(
    TRequest request, IServiceProvider sp, CancellationToken ct, bool otelActive)
{
    // timeout (igual ao atual)
    // context accessor: resolver lazy aqui, não no Mediator
    var contextAccessor = (PipelineContextAccessor)sp.GetRequiredService<IPipelineContextAccessor>();
    var context = new ProxosPipelineContext { Request = request, CancellationToken = effectiveCt };
    using var _ = PipelineContextAccessor.SetContext(context);

    // OTel: só se ativo
    Stopwatch? sw = null;
    Activity? activity = null;
    KeyValuePair<string, object?> tag = default;
    if (otelActive)
    {
        tag = new KeyValuePair<string, object?>("request.type", RequestName);
        ProxosDiagnostics.RequestsTotal.Add(1, [tag]);
        activity = ProxosDiagnostics.StartRequestActivity(RequestName);
        sw = Stopwatch.StartNew();
    }

    try { ... } // igual ao atual, mas usando activity?.Dispose() no finally
}
```

Assinatura do método base muda — REMOVER `contextAccessor` do parâmetro:
```csharp
// ANTES:
internal abstract Task<object?> Handle(object, IServiceProvider, PipelineContextAccessor, CancellationToken);

// DEPOIS:
internal abstract Task<object?> Handle(object, IServiceProvider, CancellationToken);
```

#### 2. Mediator.cs — remover contextAccessor do construtor e das chamadas

```csharp
// REMOVER campo:
private readonly PipelineContextAccessor _contextAccessor;

// REMOVER do construtor:
IPipelineContextAccessor contextAccessor

// TODAS as chamadas wrapper.Handle():
// ANTES: wrapper.Handle(request, _serviceProvider, _contextAccessor, ct)
// DEPOIS: wrapper.Handle(request, _serviceProvider, ct)
```

Opcionalmente, fazer `Send<TResponse>` não-async para sync handlers:
```csharp
public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(request);
    var wrapper = _registry.GetRequestWrapper(request.GetType());
    var task = wrapper.Handle(request, _serviceProvider, ct);

    if (task.IsCompletedSuccessfully)
        return Task.FromResult((TResponse)task.Result!);

    return AwaitAndCast<TResponse>(task);
}
private static async Task<TResponse> AwaitAndCast<TResponse>(Task<object?> task)
    => (TResponse)(await task)!;
```

#### 3. ServiceCollectionExtensions.cs — remover contextAccessor da factory do Mediator

```csharp
// ANTES:
services.TryAddScoped<IMediator>(sp => new Mediator(
    sp,
    sp.GetRequiredService<WrapperRegistry>(),
    sp.GetRequiredService<IPipelineContextAccessor>(),   // ← REMOVER
    sp.GetRequiredService<ProxosConfiguration>()));

// DEPOIS:
services.TryAddScoped<IMediator>(sp => new Mediator(
    sp,
    sp.GetRequiredService<WrapperRegistry>(),
    sp.GetRequiredService<ProxosConfiguration>()));
```

`IPipelineContextAccessor` fica registrado para injeção direta em behaviors — não remove o registro, só tira do Mediator.

#### 4. NotificationHandlerWrapper.cs — gate de OTel

```csharp
bool otelActive = ProxosDiagnostics.ActivitySource.HasListeners()
               || ProxosDiagnostics.NotificationsTotal.Enabled;

var tag = otelActive
    ? new KeyValuePair<string, object?>("notification.type", NotificationName)
    : default;

if (otelActive) ProxosDiagnostics.NotificationsTotal.Add(1, [tag]);
using var activity = otelActive
    ? ProxosDiagnostics.StartPublishActivity(NotificationName)
    : null;

// Remove o try/catch externo — activity?.Dispose() no finally
```

#### 5. Rodar benchmark e validar
```bash
cd benchmarks/Proxos.Benchmarks
dotnet run -c Release
```
Meta: Send < 256ns e < 544B (bater MediatR).

#### 6. Rodar testes após as mudanças
```bash
dotnet test tests/Proxos.Tests/Proxos.Tests.csproj --no-build -c Release
```
Os 58 testes devem continuar passando.

#### 7. Atualizar README com resultados reais
Substituir a tabela de benchmarks pelos novos números.

---

## Antes de postar no LinkedIn

### ✅ Benchmarks executados — resultado: Proxos é MAIS LENTO que MediatR no dispatch
Medido em .NET 9, Intel Core i7-1355U (2026-03-17):

| | MediatR 12.x | Proxos |
|---|---|---|
| Send() | 256 ns / 544 B | 1.065 µs / 1.368 B |
| Publish() | 226 ns / 448 B | 562 ns / 928 B |

**Causa:** overhead de DI maior (grafo de objetos Scoped mais pesado). Em produção essa diferença é irrelevante (< 1 µs vs milissegundos de I/O).

**O argumento de venda é a licença MIT**, não performance. README e post do LinkedIn já foram corrigidos para refletir isso.

### Não mencionar como "pronto para produção"
- "Projeto open source que criei como alternativa MIT ao MediatR"
- "Ainda em estágio inicial — feedback bem-vindo"
- Convide para contribuir e reportar issues

---

## Estado atual (v1.0.2)

- ✅ net8.0 + net9.0 + net10.0
- ✅ Namespace Proxos
- ✅ AddProxos() — nome consistente com o pacote
- ✅ AddProxos() mantido como obsoleto (backward compat)
- ✅ 58 testes passando nos 3 TFMs (174 execuções totais)
- ✅ Source Generator embutido no .nupkg
- ✅ Roslyn Analyzer HRM001 + HRM002
- ✅ Analyzer HRM001/HRM002 — fix cross-assembly
- ✅ Testes de edge case: handlers em classes aninhadas, TypeEvaluator, múltiplos assemblies
- ✅ OpenTelemetry built-in
- ✅ MIT license
- ✅ README.md atualizado + badges CI/NuGet/MIT
- ✅ CHANGELOG.md
- ✅ GitHub Actions CI (testes nos 3 TFMs em Ubuntu + Windows + coleta de cobertura)
- ✅ GitHub Actions publish (NuGet via tag v*.*.*)
- ✅ Projeto de benchmarks (BenchmarkDotNet vs MediatR 12.x) — `benchmarks/Proxos.Benchmarks/`
- ✅ ADR 001 — `docs/adr/001-hermes-vs-mediatr.md`
- ⏳ Versões unlisted — deleção permanente solicitada em 2026-03-17, aguardando e-mail
- ✅ Benchmarks executados — Proxos é mais lento no dispatch; argumento corrigido para licença MIT
