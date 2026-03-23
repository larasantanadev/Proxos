# Proxos

[![CI](https://github.com/larasantanadev/Proxos/actions/workflows/ci.yml/badge.svg)](https://github.com/larasantanadev/Proxos/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Proxos)](https://www.nuget.org/packages/Proxos)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

MIT-licensed mediator for .NET 8, 9 and 10 — compile-time safety via Source Generator and Roslyn Analyzer, built-in OpenTelemetry, API compatible with MediatR.

## Por que Proxos?

| Aspecto | MediatR | Proxos |
|---|---|---|
| Dispatch | Reflection em toda chamada | Source Generator gera tabela em compile-time — zero reflection no startup scanning |
| Startup | Assembly scanning em runtime (lento) | Registro estático gerado — startup instantâneo |
| Handler não encontrado | Exceção em runtime, mensagem genérica | Analyzer **PRX001** alerta em compile-time |
| Ordem de behaviors | Depende da ordem de registro — frágil | Determinística e explícita |
| Behaviors condicionais | Apenas via generic constraint | `IConditionalBehavior` para condições em runtime |
| Timeout por request | Manual | `[RequestTimeout(ms)]` declarativo |
| Ignorar behavior | Não existe | `[IgnoreBehavior(typeof(...))]` por request |
| Diagnósticos | Nenhum built-in | `ActivitySource` + `Meter` integrados (OpenTelemetry) |
| Contexto no pipeline | Não existe | `IPipelineContextAccessor` injetável |
| Testes | Requer mocks verbosos | `FakeMediator` com API fluente |
| Exception side-effects | `IRequestExceptionAction` (sempre relança) | ✅ igual |
| Licença | Comercial a partir da v13 | MIT |

## Instalação

```bash
dotnet add package Proxos
dotnet add package Proxos.Testing  # apenas em projetos de teste
```

## Configuração

```csharp
builder.Services.AddProxos(cfg => cfg
    .RegisterServicesFromAssembly(typeof(Program).Assembly)
    .AddOpenBehavior(typeof(LoggingBehavior<,>)));
```

## Uso

### Request com retorno

```csharp
public record GetUserQuery(int Id) : IRequest<User>;

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, User>
{
    public Task<User> Handle(GetUserQuery request, CancellationToken ct)
        => _db.Users.FindAsync(request.Id, ct);
}

// Envio
var user = await mediator.Send(new GetUserQuery(42));
```

### Command (sem retorno)

```csharp
public record CreateUserCommand(string Name) : IRequest;

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand>
{
    public async Task<Unit> Handle(CreateUserCommand request, CancellationToken ct)
    {
        await _db.Users.AddAsync(new User(request.Name), ct);
        return Unit.Value;
    }
}
```

### Notificações

```csharp
public record UserCreated(int UserId) : INotification;

// Múltiplos handlers — todos recebem a notificação
public class SendWelcomeEmail : INotificationHandler<UserCreated> { ... }
public class UpdateAuditLog  : INotificationHandler<UserCreated> { ... }

await mediator.Publish(new UserCreated(userId));
```

### Streams

```csharp
public record GetOrdersQuery(int CustomerId) : IStreamRequest<Order>;

public class GetOrdersHandler : IStreamRequestHandler<GetOrdersQuery, Order>
{
    public async IAsyncEnumerable<Order> Handle(GetOrdersQuery request, CancellationToken ct)
    {
        await foreach (var order in _db.GetOrdersAsync(request.CustomerId, ct))
            yield return order;
    }
}

await foreach (var order in mediator.CreateStream(new GetOrdersQuery(customerId)))
    Process(order);
```

## Pipeline Behaviors

```csharp
public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        var response = await next();
        logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
        return response;
    }
}

// Registro
cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
```

### Behavior Condicional

```csharp
public class CacheBehavior<TRequest, TResponse>(ICache cache)
    : IConditionalBehavior<TRequest, TResponse>
    where TRequest : ICacheableRequest
{
    // Se retornar false, o behavior é pulado automaticamente — sem overhead
    public bool ShouldHandle(TRequest request) => !request.BypassCache;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (cache.TryGet(request.CacheKey, out TResponse? cached)) return cached!;
        var result = await next();
        cache.Set(request.CacheKey, result);
        return result;
    }
}
```

## Atributos

### Timeout automático

```csharp
[RequestTimeout(5000)] // cancela automaticamente após 5 segundos
public record GetReportQuery : IRequest<Report>;
```

### Ignorar behavior por request

```csharp
[IgnoreBehavior(typeof(LoggingBehavior<,>))]
public record HealthCheckQuery : IRequest<bool>;
```

## Tratamento de Exceções

### Handler (pode suprimir a exceção)

```csharp
public class NotFoundHandler : IRequestExceptionHandler<GetUserQuery, User, NotFoundException>
{
    public Task Handle(GetUserQuery request, NotFoundException ex,
        RequestExceptionHandlerState<User> state, CancellationToken ct)
    {
        state.SetHandled(User.Anonymous); // suprime a exceção e retorna alternativo
        return Task.CompletedTask;
    }
}
```

### Action (side-effect, sempre relança)

```csharp
public class ErrorLogger : IRequestExceptionAction<GetUserQuery, Exception>
{
    public Task Execute(GetUserQuery request, Exception ex, CancellationToken ct)
    {
        logger.LogError(ex, "Erro ao processar {Request}", request);
        return Task.CompletedTask; // exceção é repropagada automaticamente
    }
}
```

## Pre/Post Processors

```csharp
public class ValidationPreProcessor<TRequest>(IValidator<TRequest> validator)
    : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    public async Task Process(TRequest request, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(request, ct);
        if (!result.IsValid) throw new ValidationException(result.Errors);
    }
}
```

## Contexto de Pipeline

```csharp
public class AuditBehavior<TRequest, TResponse>(IPipelineContextAccessor accessor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        accessor.Context!.Set("userId", GetCurrentUserId());
        return await next();
    }
}

// No handler ou em outro behavior:
var userId = accessor.Context?.Get<string>("userId");
```

## Dispatch Dinâmico

```csharp
// Útil quando o tipo é descoberto em runtime (ex: mediator genérico)
object request = ResolveRequest(commandName);
object? result = await mediator.Send(request);

object notification = new SomeEvent();
await mediator.Publish(notification);
```

## Estratégias de Publicação

```csharp
cfg.DefaultPublishStrategy = PublishStrategy.ForeachAwait;       // sequencial (padrão)
cfg.DefaultPublishStrategy = PublishStrategy.WhenAll;            // paralelo, agrega exceções
cfg.DefaultPublishStrategy = PublishStrategy.WhenAllContinueOnError; // paralelo, ignora falhas
```

## TypeEvaluator — Filtrar Assembly Scanning

```csharp
cfg.RegisterServicesFromAssembly(typeof(Program).Assembly)
   .TypeEvaluator = t => !t.Namespace?.StartsWith("MyApp.Legacy") ?? true;
```

## Source Generator (registro zero-reflection)

O Proxos inclui um Source Generator que escaneia o assembly em **compile-time** e gera
um método `AddProxosGenerated()` com registro estático de todos os handlers.

Isso elimina completamente o assembly scanning em runtime — startup instantâneo.

O generator é automaticamente incluído ao instalar o pacote `Proxos`.

## Analyzer PRX001

Quando um `IRequest` não tem handler correspondente, o analyzer emite um aviso **em compile-time**:

```
warning PRX001: 'CreateUserCommand' implementa 'IRequest<Unit>' mas não existe
'IRequestHandler<CreateUserCommand, Unit>' neste projeto.
```

## OpenTelemetry

Proxos emite traces e métricas automaticamente. Configure o seu provider:

```csharp
services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource(ProxosDiagnostics.ActivitySourceName))
    .WithMetrics(b => b.AddMeter(ProxosDiagnostics.MeterName));
```

Métricas expostas:
- `proxos.requests.total` — total de requests enviados
- `proxos.requests.failed` — requests com falha
- `proxos.request.duration` — duração em ms (histograma)
- `proxos.notifications.total` — notificações publicadas
- `proxos.timeouts.total` — requests cancelados por timeout

## Proxos.Testing

```csharp
var fake = new FakeMediator()
    .Setup<GetUserQuery, User>(q => new User(q.Id, "Alice"))
    .Returns<DeleteUserCommand, Unit>(Unit.Value);

// Verificações
Assert.True(fake.WasSent<GetUserQuery>());
Assert.True(fake.WasSent<GetUserQuery>(q => q.Id == 42));
Assert.Equal(1, fake.CountSent<GetUserQuery>());
Assert.True(fake.WasPublished<UserDeleted>());
```

## Benchmarks

Medido com BenchmarkDotNet em .NET 9, Intel Core i7-1355U.
O benchmark inclui criação de scope DI + resolução do mediator + dispatch — cenário realístico de ASP.NET Core.

### Send()

| Método | Tempo | Alocações |
|---|---|---|
| MediatR 12.x | 300 ns | 544 B |
| **Proxos** | **382 ns** | **568 B** |

### Publish()

| Método | Tempo | Alocações |
|---|---|---|
| MediatR 12.x | 338 ns | 448 B |
| **Proxos** | **588 ns** | **440 B** ✅ |

> Medido com BenchmarkDotNet em .NET 9, Intel Core i7-1355U.
> Inclui criação de scope DI + resolução do mediator + dispatch — cenário realístico de ASP.NET Core.
>
> **Alocações**: Proxos aloca apenas 4.4% a mais no Send e **menos** que o MediatR no Publish.
> O overhead de tempo (~1.3–1.7×) é irrelevante em produção — qualquer I/O (banco, HTTP) é 100–1000× mais lento que o dispatch.
> A principal vantagem é **licença MIT**, segurança em compile-time e funcionalidades que o MediatR não oferece.

Para rodar localmente:
```bash
cd benchmarks/Proxos.Benchmarks
dotnet run -c Release
```

## Licença

MIT
