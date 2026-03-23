# Proxos — Plano Completo de Implementação

Substituto do MediatR com namespace próprio, target net10.0, sem reflection em hot path,
diagnósticos integrados e segurança em tempo de compilação.

---

## Por que Proxos supera o MediatR

| Aspecto                      | MediatR                                                 | Proxos                                                                                 |
| ---------------------------- | ------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| Dispatch                     | Reflection + `Activator.CreateInstance` em toda chamada | Source Generator gera tabela de dispatch em compile-time — zero reflection no hot path |
| Registro de handlers         | Assembly scanning em runtime (lento no startup)         | Source Generator gera método de registro estático — startup instantâneo                |
| Handler não encontrado       | Exceção em runtime, mensagem genérica                   | Analyzer `PRX001` alerta em **compile-time** com sugestão de código                    |
| Ordem de behaviors           | Depende da ordem de registro no DI — frágil             | Parâmetro `order:` explícito e determinístico                                          |
| Behaviors condicionais       | Só por generic constraint (compile-time)                | `IConditionalBehavior` para condições em runtime                                       |
| Timeout por request          | Não existe — implementar manualmente                    | Atributo `[RequestTimeout(ms)]` direto na classe                                       |
| Ignorar behavior por request | Não existe                                              | Atributo `[IgnoreBehavior(typeof(TBehavior))]` na classe do request                    |
| Diagnósticos                 | Nenhum built-in                                         | `ActivitySource` + `Meter` integrados (OpenTelemetry nativo)                           |
| Contexto entre behaviors     | Não existe                                              | `IPipelineContextAccessor` injetável em qualquer handler/behavior                      |
| Testes                       | Requer Moq/NSubstitute verboso                          | `Proxos.Testing` com `FakeMediator` fluente                                            |
| Framework                    | net6+                                                   | net10.0 — usa todos os recursos modernos                                               |

---

## 1. Estrutura do Repositório

```
Proxos/
├── src/
│   ├── Proxos/
│   │   ├── Proxos.csproj
│   │   ├── Interfaces/
│   │   │   ├── IRequest.cs
│   │   │   ├── INotification.cs
│   │   │   ├── IRequestHandler.cs
│   │   │   ├── INotificationHandler.cs
│   │   │   ├── IPipelineBehavior.cs
│   │   │   ├── IConditionalBehavior.cs
│   │   │   ├── IStreamPipelineBehavior.cs
│   │   │   ├── IRequestPreProcessor.cs
│   │   │   ├── IRequestPostProcessor.cs
│   │   │   ├── IRequestExceptionHandler.cs
│   │   │   ├── IStreamRequestHandler.cs
│   │   │   ├── IMediator.cs
│   │   │   ├── ISender.cs
│   │   │   └── IPublisher.cs
│   │   ├── Attributes/
│   │   │   ├── RequestTimeoutAttribute.cs
│   │   │   └── IgnoreBehaviorAttribute.cs
│   │   ├── Context/
│   │   │   ├── ProxosPipelineContext.cs
│   │   │   ├── IPipelineContextAccessor.cs
│   │   │   └── PipelineContextAccessor.cs
│   │   ├── Internal/
│   │   │   ├── HandlerWrapper.cs
│   │   │   ├── NotificationHandlerWrapper.cs
│   │   │   ├── WarmupService.cs
│   │   │   └── RequestHandlerDelegate.cs
│   │   ├── Diagnostics/
│   │   │   └── ProxosDiagnostics.cs
│   │   ├── Configuration/
│   │   │   ├── MediatorConfiguration.cs
│   │   │   └── PublishStrategy.cs
│   │   ├── Unit.cs
│   │   ├── Mediator.cs
│   │   └── Extensions/
│   │       └── ServiceCollectionExtensions.cs
│   │
│   ├── Proxos.Generator/
│   │   ├── Proxos.Generator.csproj       ← netstandard2.0, Roslyn
│   │   ├── Registration/
│   │   │   └── HandlerRegistrationGenerator.cs
│   │   ├── Dispatch/
│   │   │   └── DispatchTableGenerator.cs
│   │   └── Analyzers/
│   │       ├── MissingHandlerAnalyzer.cs
│   │       └── DiagnosticDescriptors.cs
│   │
│   └── Proxos.Testing/
│       ├── Proxos.Testing.csproj
│       ├── FakeMediator.cs
│       └── FakeMediatorExtensions.cs
│
├── tests/
│   └── Proxos.Tests/
│       ├── Proxos.Tests.csproj
│       ├── SendTests.cs
│       ├── PublishTests.cs
│       ├── PipelineBehaviorTests.cs
│       ├── ConditionalBehaviorTests.cs
│       ├── PrePostProcessorTests.cs
│       ├── ExceptionHandlerTests.cs
│       ├── StreamTests.cs
│       ├── TimeoutTests.cs
│       ├── DiagnosticsTests.cs
│       └── FakeMediatorTests.cs
├── Proxos.sln
└── PLAN.md
```

---

## 2. Configuração dos Projetos (.csproj)

### src/Proxos/Proxos.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>

    <PackageId>Proxos</PackageId>
    <Version>1.0.0</Version>
    <Description>High-performance mediator for .NET 10 — zero-reflection hot path, built-in OpenTelemetry, compile-time safety</Description>
    <PackageTags>mediator;cqrs;pipeline;proxos;dispatcher</PackageTags>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
    <PackageReference Include="System.Diagnostics.DiagnosticSource" Version="10.0.0" />
  </ItemGroup>

  <!-- Empacota o source generator dentro do pacote NuGet do Proxos -->
  <ItemGroup>
    <ProjectReference Include="..\Proxos.Generator\Proxos.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

### src/Proxos.Generator/Proxos.Generator.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Obrigatório: analyzers e generators devem ser netstandard2.0 -->
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

### src/Proxos.Testing/Proxos.Testing.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <PackageId>Proxos.Testing</PackageId>
    <Version>1.0.0</Version>
    <Description>Testing utilities for Proxos — FakeMediator with fluent assertion API</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Proxos\Proxos.csproj" />
  </ItemGroup>
</Project>
```

### tests/Proxos.Tests/Proxos.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
    <ProjectReference Include="..\..\src\Proxos\Proxos.csproj" />
    <ProjectReference Include="..\..\src\Proxos.Testing\Proxos.Testing.csproj" />
  </ItemGroup>
</Project>
```

---

## 3. Interfaces — Contratos Públicos

### 3.1 Marcadores de Request/Notification

```csharp
namespace Proxos;

// Request sem retorno — retorna Unit internamente
public interface IRequest : IRequest<Unit> { }

// Request com retorno tipado
public interface IRequest<out TResponse> { }

// Request que retorna stream de dados
public interface IStreamRequest<out TResponse> { }

// Evento/notificação — sem retorno, pode ter N handlers
public interface INotification { }
```

### 3.2 Handlers

```csharp
namespace Proxos;

// Handler principal com retorno
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

// Convenience: handler sem retorno explícito
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest<Unit>
{ }

// Handler de notificações
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}

// Handler de streams
public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
```

### 3.3 Pipeline

```csharp
namespace Proxos;

// Delegate que representa o próximo passo na cadeia
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

// Delegate para streams
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<TResponse>();

// Behavior padrão — envolve o handler (como middleware)
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

// Behavior condicional — pode se auto-desabilitar em runtime
// Se ShouldHandle retornar false, o mediator chama next() diretamente e pula este behavior
public interface IConditionalBehavior<in TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    bool ShouldHandle(TRequest request);
}

// Behavior para streams
public interface IStreamPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

// Pre-processor: executa antes do handler (dentro do núcleo do pipeline)
public interface IRequestPreProcessor<in TRequest>
    where TRequest : notnull
{
    Task Process(TRequest request, CancellationToken cancellationToken);
}

// Post-processor: executa após o handler retornar, antes de voltar pelos behaviors
public interface IRequestPostProcessor<in TRequest, in TResponse>
    where TRequest : notnull
{
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}

// Exception handler: intercepta exceções lançadas dentro do pipeline
public interface IRequestExceptionHandler<in TRequest, TResponse, in TException>
    where TRequest : notnull
    where TException : Exception
{
    Task Handle(
        TRequest request,
        TException exception,
        RequestExceptionHandlerState<TResponse> state,
        CancellationToken cancellationToken);
}

public sealed class RequestExceptionHandlerState<TResponse>
{
    public bool IsExceptionHandled { get; private set; }
    public TResponse? Response { get; private set; }

    // Chame este método para marcar a exceção como tratada e definir o resultado
    public void SetHandled(TResponse response)
    {
        Response = response;
        IsExceptionHandled = true;
    }
}
```

### 3.4 IMediator, ISender, IPublisher

```csharp
namespace Proxos;

public interface ISender
{
    // Envio tipado com retorno
    Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    // Envio sem retorno explícito
    Task Send(IRequest request, CancellationToken cancellationToken = default);

    // Envio dinâmico (request como object — útil em cenários de dispatch genérico)
    Task<object?> Send(object request, CancellationToken cancellationToken = default);

    // Stream tipado
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}

public interface IPublisher
{
    Task Publish(INotification notification, CancellationToken cancellationToken = default);
    Task Publish(object notification, CancellationToken cancellationToken = default);
}

// IMediator herda ambos — é o ponto de entrada principal
public interface IMediator : ISender, IPublisher { }
```

---

## 4. Unit

```csharp
namespace Proxos;

/// Representa ausência de valor em contextos genéricos (substitui void).
[Serializable]
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>, IComparable
{
    public static readonly Unit Value = new();

    // Cached task — evita alocação em handlers síncronos
    public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(Value);

    public int    CompareTo(Unit other)  => 0;
    public int    CompareTo(object? obj) => 0;
    public bool   Equals(Unit other)     => true;
    public override bool   Equals(object? obj) => obj is Unit;
    public override int    GetHashCode()       => 0;
    public override string ToString()          => "()";

    public static bool operator ==(Unit left, Unit right) => true;
    public static bool operator !=(Unit left, Unit right) => false;
}
```

---

## 5. Atributos

### 5.1 [RequestTimeout]

Aplicado na classe do request. O mediator detecta e cria um `CancellationTokenSource`
vinculado com o timeout especificado. Não requer nenhum behavior adicional.

```csharp
namespace Proxos;

/// Define um timeout automático para o request.
/// O CancellationToken passado ao handler será cancelado após o tempo especificado.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequestTimeoutAttribute : Attribute
{
    public int Milliseconds { get; }
    public RequestTimeoutAttribute(int milliseconds) => Milliseconds = milliseconds;
}

// Uso:
// [RequestTimeout(5000)]  // handler cancelado se demorar > 5s
// public class GetReportQuery : IRequest<Report> { }
```

### 5.2 [IgnoreBehavior(typeof(...))]

Aplicado na classe do request para pular behaviors específicos sem alterar o behavior.
O mediator verifica o atributo durante a montagem do pipeline.

> **Por que não genérico:** C# 11 suporta atributos genéricos, mas apenas com tipos
> fechados. `[IgnoreBehavior<LoggingBehavior<,>>]` é tipo aberto → erro de compilação.
> A solução correta usa `Type` como parâmetro do atributo.

```csharp
namespace Proxos;

/// Instrui o mediator a não executar o behavior do tipo especificado para este request.
/// Aceita tipos abertos: typeof(LoggingBehavior<,>) ou fechados: typeof(LoggingBehavior<Req, Res>).
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class IgnoreBehaviorAttribute : Attribute
{
    public Type BehaviorType { get; }
    public IgnoreBehaviorAttribute(Type behaviorType) => BehaviorType = behaviorType;
}

// Uso:
// [IgnoreBehavior(typeof(LoggingBehavior<,>))]
// public class HealthCheckQuery : IRequest<bool> { }
```

> **Implementação no mediator:** ao montar o pipeline, comparar o tipo aberto do behavior
> concreto (`b.GetType().GetGenericTypeDefinition()`) com os tipos registrados nos atributos.

---

## 6. IPipelineContextAccessor — Contexto Compartilhado

Permite que behaviors e handlers compartilhem dados durante a execução de um request,
sem poluir a assinatura do handler nem o objeto de request.

Funciona via `AsyncLocal<T>` — seguro em cenários concorrentes com múltiplas threads.

```csharp
namespace Proxos.Context;

public sealed class ProxosPipelineContext
{
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");
    public Type RequestType { get; init; } = typeof(object);
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    // Bag de dados para comunicação entre behaviors/handlers
    // Ex: behavior de auth guarda o UserId aqui; handler de audit lê
    public IDictionary<string, object?> Items { get; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}

public interface IPipelineContextAccessor
{
    // Contexto do request em andamento; null fora de um pipeline Proxos
    ProxosPipelineContext? Context { get; }
}

// Implementação interna — usa AsyncLocal para isolamento por cadeia async
internal sealed class PipelineContextAccessor : IPipelineContextAccessor
{
    private static readonly AsyncLocal<ProxosPipelineContext?> _current = new();
    public ProxosPipelineContext? Context => _current.Value;

    internal static void Set(ProxosPipelineContext ctx)  => _current.Value = ctx;
    internal static void Clear()                          => _current.Value = null;
}

// Uso em um behavior:
// public class AuditBehavior<TRequest, TResponse>(IPipelineContextAccessor accessor)
//     : IPipelineBehavior<TRequest, TResponse>
// {
//     public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
//     {
//         accessor.Context!.Items["userId"] = GetCurrentUserId();
//         return await next();
//     }
// }
```

---

## 7. Diagnósticos — OpenTelemetry + Metrics

Built-in, zero configuração adicional. Funciona com qualquer collector OpenTelemetry.

```csharp
namespace Proxos.Diagnostics;

internal static class ProxosDiagnostics
{
    internal const string ActivitySourceName = "Proxos";
    internal const string MeterName          = "Proxos";

    // Versão lida do assembly em runtime — sem dependência de Nerdbank.GitVersioning ou MinVer
    private static readonly string _version =
        typeof(ProxosDiagnostics).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    internal static readonly ActivitySource ActivitySource =
        new(ActivitySourceName, _version);

    internal static readonly Meter Meter =
        new(MeterName, _version);

    // Contadores e histogramas
    internal static readonly Counter<long> RequestsHandled =
        Meter.CreateCounter<long>(
            "proxos.requests.total",
            description: "Total de requests processados pelo Proxos");

    internal static readonly Counter<long> RequestsFailed =
        Meter.CreateCounter<long>(
            "proxos.requests.failed",
            description: "Total de requests que resultaram em exceção");

    internal static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>(
            "proxos.request.duration",
            unit: "ms",
            description: "Duração de execução dos requests");

    internal static readonly Counter<long> NotificationsPublished =
        Meter.CreateCounter<long>(
            "proxos.notifications.total",
            description: "Total de notificações publicadas");
}

// O que é gerado automaticamente em cada Send():
// Activity "Proxos.Send" com tags:
//   proxos.request.type  = "CreateUserCommand"
//   proxos.success       = true/false
// Histogram: duração em ms
// Counter: +1 a cada Send(), +1 ao RequestsFailed em caso de exceção
```

---

## 8. Configuração

### 8.1 PublishStrategy

```csharp
namespace Proxos.Configuration;

public enum PublishStrategy
{
    /// Handlers executam um por um. Exceção no primeiro interrompe a cadeia.
    Sequential = 0,

    /// Handlers executam em paralelo via Task.WhenAll.
    /// Múltiplas falhas são agregadas em AggregateException.
    Parallel = 1,

    /// Dispara handlers sem aguardar. Exceções são apenas logadas, nunca propagadas.
    /// Use apenas para notificações que realmente podem ser perdidas.
    FireAndForget = 2
}
```

### 8.2 MediatorConfiguration

```csharp
namespace Proxos.Configuration;

public sealed class MediatorConfiguration
{
    internal List<Assembly> AssembliesToScan { get; } = [];
    internal List<BehaviorRegistration> BehaviorRegistrations { get; } = [];

    public PublishStrategy PublishStrategy { get; set; } = PublishStrategy.Sequential;
    public ServiceLifetime HandlerLifetime  { get; set; } = ServiceLifetime.Scoped;
    public ServiceLifetime MediatorLifetime { get; set; } = ServiceLifetime.Transient;

    // Habilita diagnósticos automáticos (Activity + Metrics). Padrão: true.
    public bool EnableDiagnostics { get; set; } = true;

    // --- Registro de assemblies ---

    public MediatorConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        AssembliesToScan.Add(assembly);
        return this;
    }

    public MediatorConfiguration RegisterServicesFromAssemblies(params ReadOnlySpan<Assembly> assemblies)
    {
        AssembliesToScan.AddRange(assemblies);
        return this;
    }

    // --- Registro de behaviors com ordem explícita ---
    // Menor order = mais externo (executa antes do handler, retorna depois)
    // Behaviors sem order explícito recebem order = int.MaxValue (mantém ordem de registro)

    public MediatorConfiguration AddBehavior(
        Type behaviorType,
        ServiceLifetime lifetime = ServiceLifetime.Transient,
        int order = int.MaxValue)
    {
        BehaviorRegistrations.Add(new(
            behaviorType, lifetime, order,
            IsOpenGeneric: false,
            RegistrationIndex: BehaviorRegistrations.Count));
        return this;
    }

    public MediatorConfiguration AddBehavior<TBehavior>(
        ServiceLifetime lifetime = ServiceLifetime.Transient,
        int order = int.MaxValue)
        where TBehavior : class
        => AddBehavior(typeof(TBehavior), lifetime, order);

    public MediatorConfiguration AddOpenBehavior(
        Type openGenericBehaviorType,
        ServiceLifetime lifetime = ServiceLifetime.Transient,
        int order = int.MaxValue)
    {
        if (!openGenericBehaviorType.IsGenericTypeDefinition)
            throw new ArgumentException(
                $"{openGenericBehaviorType.Name} precisa ser um tipo genérico aberto " +
                $"(ex: typeof(LoggingBehavior<,>)).",
                nameof(openGenericBehaviorType));

        BehaviorRegistrations.Add(new(
            openGenericBehaviorType, lifetime, order,
            IsOpenGeneric: true,
            RegistrationIndex: BehaviorRegistrations.Count));
        return this;
    }
}

// Registro interno de um behavior com sua ordem e índice de registro (desempate)
internal sealed record BehaviorRegistration(
    Type BehaviorType,
    ServiceLifetime Lifetime,
    int Order,
    bool IsOpenGeneric,
    int RegistrationIndex);
```

---

## 9. Internal — Wrappers e Cache

### 9.1 Estratégia de Cache (dois estágios)

**Estágio 1 — warm-up (startup):**
`WarmupService` (implementa `IHostedService`) percorre os tipos registrados no DI logo na inicialização.
Para cada `IRequestHandler<,>` e `INotificationHandler<>` encontrado, pré-cria o wrapper correspondente.
Ao final do warm-up, converte o `ConcurrentDictionary` em `FrozenDictionary` — leituras ~2× mais rápidas.

**Estágio 2 — hot path:**
`Mediator.Send()` acessa o `FrozenDictionary`. Zero reflection, zero alocação de tipo.
Tipos descobertos em runtime (cenários avançados) voltam para o `ConcurrentDictionary` fallback.

```csharp
// Internal/WarmupService.cs
internal sealed class WarmupService(IServiceProvider sp, MediatorCache cache) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        cache.Freeze(sp);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

// Internal/MediatorCache.cs
internal sealed class MediatorCache
{
    private FrozenDictionary<Type, RequestHandlerBase>?      _frozenRequests;
    private FrozenDictionary<Type, NotificationHandlerBase>? _frozenNotifications;

    private readonly ConcurrentDictionary<Type, RequestHandlerBase>      _requests      = new();
    private readonly ConcurrentDictionary<Type, NotificationHandlerBase> _notifications = new();

    internal void Freeze(IServiceProvider sp)
    {
        // Pré-aquece todos os tipos registrados
        var descriptors = sp.GetServices<HandlerTypeDescriptor>();
        foreach (var d in descriptors)
            EnsureRequestWrapper(d.RequestType, d.ResponseType);

        _frozenRequests      = new Dictionary<Type, RequestHandlerBase>(_requests).ToFrozenDictionary();
        _frozenNotifications = new Dictionary<Type, NotificationHandlerBase>(_notifications).ToFrozenDictionary();
    }

    internal RequestHandlerBase GetOrCreateRequestWrapper(Type requestType, Type responseType)
    {
        // Tenta FrozenDictionary primeiro (caminho feliz após warm-up)
        if (_frozenRequests is not null && _frozenRequests.TryGetValue(requestType, out var frozen))
            return frozen;

        return _requests.GetOrAdd(requestType, _ => CreateWrapper(requestType, responseType));
    }

    internal NotificationHandlerBase GetOrCreateNotificationWrapper(Type notificationType)
    {
        if (_frozenNotifications is not null && _frozenNotifications.TryGetValue(notificationType, out var frozen))
            return frozen;

        return _notifications.GetOrAdd(notificationType, t =>
        {
            var wrapperType = typeof(NotificationHandlerWrapper<>).MakeGenericType(t);
            return (NotificationHandlerBase)Activator.CreateInstance(wrapperType)!;
        });
    }

    private static RequestHandlerBase CreateWrapper(Type requestType, Type responseType)
    {
        var wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(requestType, responseType);
        return (RequestHandlerBase)Activator.CreateInstance(wrapperType)!;
    }
}
```

> **Nota:** O Source Generator (seção 13) elimina a necessidade de `Activator.CreateInstance`
> para projetos que usam `AddProxosGenerated()`. O cache com `FrozenDictionary` é o fallback
> para cenários de registro manual ou dinâmico.

### 9.2 HandlerWrapper — Implementação

```csharp
// Internal/HandlerWrapper.cs

internal abstract class RequestHandlerBase
{
    public abstract Task<object?> Handle(
        object request,
        IServiceProvider sp,
        CancellationToken ct);
}

internal sealed class RequestHandlerWrapper<TRequest, TResponse> : RequestHandlerBase
    where TRequest : IRequest<TResponse>
{
    public override async Task<object?> Handle(object request, IServiceProvider sp, CancellationToken ct)
    {
        var typedRequest = (TRequest)request;

        // 1. Verificar [IgnoreBehavior(typeof(...))] no tipo do request
        var ignoredOpenTypes = GetIgnoredBehaviorTypes(typeof(TRequest));

        // 2. Resolver behaviors respeitando a ordem configurada, excluindo os ignorados
        var allBehaviors = sp.GetServices<IPipelineBehavior<TRequest, TResponse>>()
            .Where(b => !IsIgnored(b.GetType(), ignoredOpenTypes))
            .ToList();

        // 3. Aplicar IConditionalBehavior — remover os que decidirem não rodar
        var activeBehaviors = allBehaviors
            .Where(b => b is not IConditionalBehavior<TRequest, TResponse> cb || cb.ShouldHandle(typedRequest))
            .Reverse(); // pipeline é montado de dentro para fora

        // 4. Resolver pre/post processors e exception handlers
        var preProcessors      = sp.GetServices<IRequestPreProcessor<TRequest>>();
        var postProcessors     = sp.GetServices<IRequestPostProcessor<TRequest, TResponse>>();
        var exceptionHandlers  = sp.GetServices<IRequestExceptionHandler<TRequest, TResponse, Exception>>();

        var handler = sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

        // 5. Montar núcleo do pipeline (pre → handler → post)
        RequestHandlerDelegate<TResponse> core = async () =>
        {
            foreach (var pre in preProcessors)
                await pre.Process(typedRequest, ct);

            TResponse response;
            try
            {
                response = await handler.Handle(typedRequest, ct);
            }
            catch (Exception ex)
            {
                var state = new RequestExceptionHandlerState<TResponse>();
                foreach (var eh in exceptionHandlers)
                {
                    await eh.Handle(typedRequest, ex, state, ct);
                    if (state.IsExceptionHandled)
                        return state.Response!;
                }
                throw; // nenhum handler tratou — propaga
            }

            foreach (var post in postProcessors)
                await post.Process(typedRequest, response, ct);

            return response;
        };

        // 6. Envolver com behaviors
        RequestHandlerDelegate<TResponse> pipeline = core;
        foreach (var behavior in activeBehaviors)
        {
            var next = pipeline;
            var b = behavior;
            pipeline = () => b.Handle(typedRequest, next, ct);
        }

        return await pipeline();
    }

    private static HashSet<Type> GetIgnoredBehaviorTypes(Type requestType)
    {
        // Lê [IgnoreBehavior(typeof(LoggingBehavior<,>))] do tipo do request
        // Normaliza tudo para tipo aberto para permitir comparação uniforme
        return requestType
            .GetCustomAttributes<IgnoreBehaviorAttribute>(inherit: false)
            .Select(a => a.BehaviorType.IsGenericType && !a.BehaviorType.IsGenericTypeDefinition
                ? a.BehaviorType.GetGenericTypeDefinition()   // fechado → abre
                : a.BehaviorType)                              // já é aberto ou não-genérico
            .ToHashSet();
    }

    private static bool IsIgnored(Type behaviorConcreteType, HashSet<Type> ignoredOpenTypes)
    {
        if (ignoredOpenTypes.Count == 0) return false;
        // Normaliza o tipo concreto do behavior para tipo aberto e verifica
        var openType = behaviorConcreteType.IsGenericType
            ? behaviorConcreteType.GetGenericTypeDefinition()
            : behaviorConcreteType;
        return ignoredOpenTypes.Contains(openType) || ignoredOpenTypes.Contains(behaviorConcreteType);
    }
}

// Internal/NotificationHandlerWrapper.cs

internal abstract class NotificationHandlerBase
{
    public abstract Task Handle(
        object notification,
        IServiceProvider sp,
        PublishStrategy strategy,
        ILogger? logger,
        CancellationToken ct);
}

internal sealed class NotificationHandlerWrapper<TNotification> : NotificationHandlerBase
    where TNotification : INotification
{
    public override async Task Handle(
        object notification,
        IServiceProvider sp,
        PublishStrategy strategy,
        ILogger? logger,
        CancellationToken ct)
    {
        var handlers = sp.GetServices<INotificationHandler<TNotification>>().ToList();

        switch (strategy)
        {
            case PublishStrategy.Sequential:
                foreach (var h in handlers)
                    await h.Handle((TNotification)notification, ct);
                break;

            case PublishStrategy.Parallel:
                // Task.WhenAll preserva e agrega exceções em AggregateException
                await Task.WhenAll(handlers.Select(h =>
                    h.Handle((TNotification)notification, ct)));
                break;

            case PublishStrategy.FireAndForget:
                foreach (var h in handlers)
                {
                    var handler = h;
                    // CancellationToken.None intencional — é fire-and-forget
                    _ = Task.Run(async () =>
                    {
                        try   { await handler.Handle((TNotification)notification, CancellationToken.None); }
                        catch (Exception ex)
                        {
                            logger?.LogError(ex,
                                "Proxos FireAndForget: handler {Handler} falhou para {Notification}",
                                handler.GetType().Name,
                                typeof(TNotification).Name);
                        }
                    }, CancellationToken.None);
                }
                break;
        }
    }
}
```

---

## 10. Mediator — Implementação Central

```csharp
namespace Proxos;

// DECISÃO DE ESCOPO: o Mediator NÃO cria IServiceScope por request.
// Handlers são resolvidos do mesmo IServiceProvider que o Mediator recebe.
// Consequência: o Mediator (Transient) deve ser consumido em contextos Scoped
// (ex: dentro de uma requisição HTTP). Se usado em Singleton, handlers Scoped
// falharão com CaptiveDependencyException. Documentar isso claramente.
public sealed class Mediator : IMediator
{
    private readonly IServiceProvider  _sp;
    private readonly MediatorCache     _cache;
    private readonly PublishStrategy   _strategy;
    private readonly bool              _diagnosticsEnabled;
    private readonly ILogger<Mediator>? _logger; // opcional — usado apenas no FireAndForget

    public Mediator(
        IServiceProvider sp,
        MediatorCache cache,
        MediatorConfiguration config,
        ILogger<Mediator>? logger = null)
    {
        _sp                 = sp;
        _cache              = cache;
        _strategy           = config.PublishStrategy;
        _diagnosticsEnabled = config.EnableDiagnostics;
        _logger             = logger;
    }

    // --- Send<TResponse> ---
    public async Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();

        // Contexto do pipeline
        var ctx = new ProxosPipelineContext { RequestType = requestType };
        PipelineContextAccessor.Set(ctx);

        // Timeout automático via [RequestTimeout]
        using var cts = BuildCancellationSource(requestType, cancellationToken);
        var ct = cts?.Token ?? cancellationToken;

        using var activity = _diagnosticsEnabled
            ? ProxosDiagnostics.ActivitySource.StartActivity($"Proxos.Send.{requestType.Name}")
            : null;
        activity?.SetTag("proxos.request.type", requestType.FullName);

        // Stopwatch.GetTimestamp() é zero-cost quando diagnósticos estão desabilitados
        var startTimestamp = _diagnosticsEnabled ? Stopwatch.GetTimestamp() : 0L;

        try
        {
            var responseType = typeof(TResponse);
            var wrapper      = _cache.GetOrCreateRequestWrapper(requestType, responseType);
            var result       = await wrapper.Handle(request, _sp, ct);

            activity?.SetTag("proxos.success", true);
            if (_diagnosticsEnabled)
            {
                ProxosDiagnostics.RequestsHandled.Add(1,
                    new("request.type", requestType.Name),
                    new("success", true));
                ProxosDiagnostics.RequestDuration.Record(
                    Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                    new("request.type", requestType.Name));
            }

            return (TResponse)result!;
        }
        catch
        {
            activity?.SetTag("proxos.success", false);
            if (_diagnosticsEnabled)
            {
                ProxosDiagnostics.RequestsFailed.Add(1,
                    new("request.type", requestType.Name));
            }
            throw;
        }
        finally
        {
            PipelineContextAccessor.Clear();
        }
    }

    // --- Send (sem retorno) ---
    public Task Send(IRequest request, CancellationToken cancellationToken = default)
        => Send<Unit>(request, cancellationToken);

    // --- Send dinâmico ---
    public async Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var iface = requestType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                                 i.GetGenericTypeDefinition() == typeof(IRequest<>))
            ?? throw new InvalidOperationException(BuildHandlerNotFoundMessage(requestType));

        var responseType = iface.GetGenericArguments()[0];
        var wrapper      = _cache.GetOrCreateRequestWrapper(requestType, responseType);
        return await wrapper.Handle(request, _sp, cancellationToken);
    }

    // --- CreateStream ---
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var handler = _sp.GetService<IStreamRequestHandler<IStreamRequest<TResponse>, TResponse>>()
            ?? throw new InvalidOperationException(
                $"Nenhum handler de stream registrado para '{request.GetType().Name}'.");

        var behaviors = _sp
            .GetServices<IStreamPipelineBehavior<IStreamRequest<TResponse>, TResponse>>()
            .Reverse();

        StreamHandlerDelegate<TResponse> pipeline = () => handler.Handle(request, cancellationToken);
        foreach (var behavior in behaviors)
        {
            var next = pipeline;
            var b    = behavior;
            pipeline = () => b.Handle(request, next, cancellationToken);
        }

        return pipeline();
    }

    // --- Publish ---
    public Task Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return PublishCore(notification, cancellationToken);
    }

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return notification is INotification n
            ? PublishCore(n, cancellationToken)
            : throw new ArgumentException(
                $"'{notification.GetType().Name}' não implementa INotification.",
                nameof(notification));
    }

    private Task PublishCore(INotification notification, CancellationToken ct)
    {
        if (_diagnosticsEnabled)
            ProxosDiagnostics.NotificationsPublished.Add(1,
                new("notification.type", notification.GetType().Name));

        var wrapper = _cache.GetOrCreateNotificationWrapper(notification.GetType());
        return wrapper.Handle(notification, _sp, _strategy, _logger, ct);
    }

    // --- Helpers ---

    private static CancellationTokenSource? BuildCancellationSource(
        Type requestType, CancellationToken externalCt)
    {
        var attr = requestType.GetCustomAttribute<RequestTimeoutAttribute>();
        if (attr is null) return null;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        cts.CancelAfter(attr.Milliseconds);
        return cts;
    }

    private static string BuildHandlerNotFoundMessage(Type requestType) =>
        $"""
        Nenhum handler encontrado para '{requestType.Name}'.

        Implemente IRequestHandler<{requestType.Name}, TResponse>:

            public class {requestType.Name}Handler : IRequestHandler<{requestType.Name}, TResponse>
            {{
                public async Task<TResponse> Handle({requestType.Name} request, CancellationToken ct)
                {{
                    // sua lógica aqui
                }}
            }}

        E registre:
            services.AddProxos(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
        """;
}
```

---

## 11. ServiceCollectionExtensions

```csharp
namespace Proxos.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProxos(
        this IServiceCollection services,
        Action<MediatorConfiguration> configure)
    {
        var config = new MediatorConfiguration();
        configure(config);

        // Configuração como singleton
        services.AddSingleton(config);

        // Cache de wrappers (singleton — vive por toda a vida do app)
        services.AddSingleton<MediatorCache>();

        // Warm-up no startup (pré-popula o cache e congela em FrozenDictionary)
        services.AddHostedService<WarmupService>();

        // Contexto do pipeline
        services.AddSingleton<IPipelineContextAccessor, PipelineContextAccessor>();

        // Escaneia assemblies e registra handlers/behaviors
        foreach (var assembly in config.AssembliesToScan)
            RegisterHandlersFromAssembly(services, assembly, config.HandlerLifetime);

        // Registra behaviors na ordem configurada
        // Menor Order = mais externo; RegistrationIndex desempata behaviors com mesmo Order
        foreach (var reg in config.BehaviorRegistrations
            .OrderBy(r => r.Order)
            .ThenBy(r => r.RegistrationIndex))
        {
            var serviceType = reg.IsOpenGeneric
                ? typeof(IPipelineBehavior<,>)
                : reg.BehaviorType;
            services.Add(new ServiceDescriptor(serviceType, reg.BehaviorType, reg.Lifetime));
        }

        // Registra IMediator / ISender / IPublisher apontando para Mediator
        services.Add(new ServiceDescriptor(typeof(IMediator),   typeof(Mediator), config.MediatorLifetime));
        services.Add(new ServiceDescriptor(typeof(ISender),     typeof(Mediator), config.MediatorLifetime));
        services.Add(new ServiceDescriptor(typeof(IPublisher),  typeof(Mediator), config.MediatorLifetime));

        return services;
    }

    private static void RegisterHandlersFromAssembly(
        IServiceCollection services,
        Assembly assembly,
        ServiceLifetime lifetime)
    {
        var concreteTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

        foreach (var type in concreteTypes)
        {
            RegisterImplementations(services, type, typeof(IRequestHandler<,>),              lifetime);
            RegisterImplementations(services, type, typeof(INotificationHandler<>),           lifetime);
            RegisterImplementations(services, type, typeof(IStreamRequestHandler<,>),         lifetime);
            RegisterImplementations(services, type, typeof(IRequestPreProcessor<>),           lifetime);
            RegisterImplementations(services, type, typeof(IRequestPostProcessor<,>),         lifetime);
            RegisterImplementations(services, type, typeof(IRequestExceptionHandler<,,>),     lifetime);
            RegisterImplementations(services, type, typeof(IStreamPipelineBehavior<,>),       lifetime);

            // Registra descritor de tipo para o WarmupService
            RegisterHandlerTypeDescriptors(services, type);
        }
    }

    private static void RegisterImplementations(
        IServiceCollection services,
        Type type,
        Type openInterface,
        ServiceLifetime lifetime)
    {
        foreach (var iface in type.GetInterfaces().Where(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == openInterface))
        {
            services.Add(new ServiceDescriptor(iface, type, lifetime));
        }
    }

    private static void RegisterHandlerTypeDescriptors(IServiceCollection services, Type type)
    {
        foreach (var iface in type.GetInterfaces().Where(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
        {
            var args = iface.GetGenericArguments();
            services.AddSingleton(new HandlerTypeDescriptor(
                RequestType:  args[0],
                ResponseType: args[1]));
        }
    }
}

// DTO auxiliar para o WarmupService
internal sealed record HandlerTypeDescriptor(Type RequestType, Type ResponseType);
```

---

## 12. Source Generator — Proxos.Generator

O gerador escaneia os symbols Roslyn em compile-time e produz dois arquivos:

### 12.1 HandlerRegistrationGenerator

**Entrada:** qualquer classe na compilação que implemente `IRequestHandler<,>`, etc.

**Saída:** `ProxosGeneratedRegistrations.g.cs`

```csharp
// <auto-generated/>
// Gerado por Proxos.Generator — não editar manualmente

using Microsoft.Extensions.DependencyInjection;
using Proxos;

namespace MyApp;  // namespace do projeto consumidor

public static class ProxosGeneratedRegistrations
{
    /// Registra todos os handlers encontrados em compile-time.
    /// Mais rápido que RegisterServicesFromAssembly porque não usa reflection no startup.
    public static IServiceCollection AddProxosGenerated(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        // Usa new ServiceDescriptor(...) — a extensão Add<TService, TImpl>() não existe no MS DI
        services.Add(new ServiceDescriptor(
            typeof(IRequestHandler<CreateUserCommand, UserDto>),
            typeof(CreateUserCommandHandler), lifetime));
        services.Add(new ServiceDescriptor(
            typeof(IRequestHandler<GetUserQuery, UserDto>),
            typeof(GetUserQueryHandler), lifetime));
        services.Add(new ServiceDescriptor(
            typeof(INotificationHandler<UserCreatedEvent>),
            typeof(SendWelcomeEmailHandler), lifetime));
        // ... todos os handlers encontrados na compilação ...
        return services;
    }
}
```

**Como usar — `AddProxosGenerated()` é chamado separado de `AddProxos()`:**

```csharp
// SEM source generator (assembly scanning em runtime):
services.AddProxos(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// COM source generator (zero reflection no startup):
services.AddProxos(cfg =>
{
    // apenas behaviors e configuração — sem RegisterServicesFromAssembly
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 1);
    cfg.PublishStrategy = PublishStrategy.Sequential;
});
services.AddProxosGenerated(); // método gerado pelo source generator, chamado aqui
```

### 12.2 DispatchTableGenerator

**Saída:** `ProxosDispatchTable.g.cs`

```csharp
// <auto-generated/>
// Tabela de dispatch sem reflection — zero Activator.CreateInstance no hot path

internal static class ProxosDispatchTable
{
    internal static readonly FrozenDictionary<Type, RequestHandlerBase> Handlers =
        new Dictionary<Type, RequestHandlerBase>
        {
            [typeof(CreateUserCommand)] = new RequestHandlerWrapper<CreateUserCommand, UserDto>(),
            [typeof(GetUserQuery)]      = new RequestHandlerWrapper<GetUserQuery, UserDto>(),
        }.ToFrozenDictionary();
}
```

O `MediatorCache` usa esta tabela quando disponível, eliminando o `Activator.CreateInstance` completamente.

### 12.3 Como implementar o generator (estrutura Roslyn)

```csharp
// Proxos.Generator/Registration/HandlerRegistrationGenerator.cs
[Generator]
public sealed class HandlerRegistrationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Encontrar todos os tipos que implementam IRequestHandler<,>
        var handlers = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => GetHandlerInfo(ctx))
            .Where(static info => info is not null)
            .Collect();

        // 2. Combinar com informações do assembly
        var combined = context.CompilationProvider.Combine(handlers);

        // 3. Gerar código
        context.RegisterSourceOutput(combined, static (spc, source) =>
            GenerateRegistrationCode(spc, source.Left, source.Right));
    }

    private static HandlerInfo? GetHandlerInfo(GeneratorSyntaxContext ctx) { /* ... */ }
    private static void GenerateRegistrationCode(...) { /* emite o .g.cs */ }
}
```

---

## 13. Roslyn Analyzer — PRX001

Detecta em **compile-time** quando um `IRequest<T>` existe mas nenhum handler está registrado
na mesma compilação.

```csharp
// Proxos.Generator/Analyzers/DiagnosticDescriptors.cs
internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor MissingHandler = new(
        id:                 "PRX001",
        title:              "Request sem handler",
        messageFormat:      "'{0}' implementa IRequest<{1}> mas nenhum IRequestHandler<{0}, {1}> foi encontrado",
        category:           "Proxos",
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:        "Toda IRequest deve ter um IRequestHandler correspondente registrado.");
}

// Proxos.Generator/Analyzers/MissingHandlerAnalyzer.cs
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingHandlerAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.MissingHandler];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;
        if (type.IsAbstract || type.TypeKind == TypeKind.Interface) return;

        // Verifica se implementa IRequest<TResponse>
        foreach (var iface in type.AllInterfaces)
        {
            if (!IsIRequest(iface)) continue;

            var responseType = iface.TypeArguments[0];

            // Verifica se existe IRequestHandler<type, responseType> na compilação
            if (!HandlerExists(ctx.Compilation, type, responseType))
            {
                var location = type.Locations.FirstOrDefault() ?? Location.None;
                ctx.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingHandler,
                    location,
                    type.Name,
                    responseType.Name));
            }
        }
    }

    private static bool IsIRequest(INamedTypeSymbol iface) { /* verifica o nome completo */ return false; }
    private static bool HandlerExists(Compilation c, INamedTypeSymbol req, ITypeSymbol res) { /* busca */ return false; }
}
```

**Resultado no IDE:**

```
⚠ PRX001  'CreateUserCommand' implementa IRequest<UserDto>
          mas nenhum IRequestHandler<CreateUserCommand, UserDto> foi encontrado
```

---

## 14. Proxos.Testing — FakeMediator

Testes unitários sem Moq. API fluente para configurar respostas e assertivas.

```csharp
namespace Proxos.Testing;

public sealed class FakeMediator : IMediator
{
    private readonly Dictionary<Type, Func<object, CancellationToken, Task<object?>>> _handlers = new();
    private readonly List<object>        _sentRequests     = [];
    private readonly List<INotification> _notifications    = [];

    // --- Configuração ---

    public FakeMediator OnSend<TRequest, TResponse>(TResponse response)
        where TRequest : IRequest<TResponse>
    {
        _handlers[typeof(TRequest)] = (_, _) => Task.FromResult<object?>(response);
        return this;
    }

    public FakeMediator OnSend<TRequest, TResponse>(Func<TRequest, TResponse> factory)
        where TRequest : IRequest<TResponse>
    {
        _handlers[typeof(TRequest)] = (req, _) => Task.FromResult<object?>(factory((TRequest)req));
        return this;
    }

    public FakeMediator OnSend<TRequest, TResponse>(Func<TRequest, CancellationToken, Task<TResponse>> factory)
        where TRequest : IRequest<TResponse>
    {
        _handlers[typeof(TRequest)] = async (req, ct) => await factory((TRequest)req, ct);
        return this;
    }

    public FakeMediator Throws<TRequest>(Exception exception)
        where TRequest : IRequest
    {
        _handlers[typeof(TRequest)] = (_, _) => Task.FromException<object?>(exception);
        return this;
    }

    // --- ISender ---

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        _sentRequests.Add(request);
        if (!_handlers.TryGetValue(request.GetType(), out var handler))
            throw new InvalidOperationException(
                $"FakeMediator: nenhuma resposta configurada para '{request.GetType().Name}'. " +
                $"Use .OnSend<{request.GetType().Name}, TResponse>(response).");

        return (TResponse)(await handler(request, ct))!;
    }

    public Task Send(IRequest request, CancellationToken ct = default)
        => Send<Unit>(request, ct);

    public Task<object?> Send(object request, CancellationToken ct = default)
        => throw new NotSupportedException("Use Send<TResponse> no FakeMediator.");

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request, CancellationToken ct = default)
        => throw new NotSupportedException("Use um FakeStreamHandler separado.");

    // --- IPublisher ---

    public Task Publish(INotification notification, CancellationToken ct = default)
    {
        _notifications.Add(notification);
        return Task.CompletedTask;
    }

    public Task Publish(object notification, CancellationToken ct = default)
        => notification is INotification n ? Publish(n, ct)
            : throw new ArgumentException("Não é INotification.");

    // --- Assertivas ---

    public IReadOnlyList<object>        SentRequests  => _sentRequests.AsReadOnly();
    public IReadOnlyList<INotification> Notifications => _notifications.AsReadOnly();

    public bool WasSent<TRequest>()
        => _sentRequests.OfType<TRequest>().Any();

    public bool WasSent<TRequest>(Func<TRequest, bool> predicate)
        => _sentRequests.OfType<TRequest>().Any(predicate);

    public bool WasPublished<TNotification>()
        => _notifications.OfType<TNotification>().Any();

    public TRequest GetSent<TRequest>()
        => _sentRequests.OfType<TRequest>().Single();
}

// Uso em teste:
// var fake = new FakeMediator()
//     .OnSend<GetUserQuery, UserDto>(new UserDto("João"));
//
// var sut = new UserController(fake);
// var result = await sut.GetUser(42);
//
// Assert.True(fake.WasSent<GetUserQuery>(q => q.UserId == 42));
```

---

## 15. Comportamentos para Casos de Borda

| Situação                                          | Comportamento                                                                             |
| ------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| `Send()` sem handler registrado                   | `InvalidOperationException` com mensagem detalhada (seção 10)                             |
| `Publish()` sem handlers                          | Silencioso — notificação ignorada                                                         |
| `Send(null)`                                      | `ArgumentNullException`                                                                   |
| `Publish(object)` que não é `INotification`       | `ArgumentException`                                                                       |
| Handler lança exceção                             | Propaga; `IRequestExceptionHandler` pode interceptar                                      |
| `[RequestTimeout]` estoura                        | `OperationCanceledException` (ou `TaskCanceledException`)                                 |
| `Parallel` publish com N falhas                   | `AggregateException` com todas as exceções                                                |
| `FireAndForget` com exceção                       | Exceção logada via `ILogger` (se disponível), nunca propagada                             |
| `IConditionalBehavior.ShouldHandle` → false       | Behavior pulado; próximo da cadeia é chamado                                              |
| Request tem `[IgnoreBehavior(typeof(TBehavior))]` | `TBehavior` removido do pipeline para este request                                        |
| Tipo registrado duas vezes                        | DI resolve o último (comportamento padrão MS DI); WarmupService usa o primeiro encontrado |
| WarmupService não registrado                      | Cache funciona lazy (ConcurrentDictionary) — sem penalidade, apenas sem FrozenDictionary  |

---

## 16. Ordem de Execução do Pipeline

```
Request entra em Send()
    │
    ├─ [RequestTimeout] → CancellationTokenSource criado e vinculado
    ├─ ProxosPipelineContext criado e publicado via AsyncLocal
    ├─ Activity OpenTelemetry iniciada
    │
    ▼
[IPipelineBehavior com menor Order] ← mais externo
    │
    ▼
[IPipelineBehavior com Order maior]
    │  ↑ IConditionalBehavior.ShouldHandle() == false → pulado
    │  ↑ [IgnoreBehavior(typeof(TBehavior))] no request → tipo pulado
    ▼
[IRequestPreProcessor #1, #2, ...]   ← executa antes do handler
    │
    ▼
[IRequestHandler]                    ← handler real
    │ exceção →  [IRequestExceptionHandler] (pode engolir e retornar resultado)
    ▼
[IRequestPostProcessor #1, #2, ...]  ← executa após o handler
    │
    ▼
Response retorna pela cadeia de behaviors (na ordem inversa)
    │
    ├─ Activity encerrada com success/failure
    ├─ Metrics registradas (count, duration)
    └─ PipelineContextAccessor limpo
```

**Regra de ordem dos behaviors:**

1. Behaviors com `order:` explícito no `AddBehavior`/`AddOpenBehavior` são ordenados pelo valor (menor = mais externo)
2. Behaviors sem `order:` recebem `int.MaxValue` e mantêm a ordem de registro
3. Para empates, a ordem de registro no DI é o desempate

**Ordem recomendada:** `Logging (1) → Validation (2) → Authorization (3) → Transaction (4)`

---

## 17. Compatibilidade com MediatR

**Decisão: Opção B — namespace próprio `Proxos`.**

Migração do projeto consumidor: substituir em massa:

```
using MediatR;               →  using Proxos;
using MediatR.Pipeline;      →  using Proxos;
IRequest<T>                  →  sem mudança (mesmo nome)
IRequestHandler<T, R>        →  sem mudança (mesmo nome)
INotification                →  sem mudança (mesmo nome)
services.AddMediatR(...)     →  services.AddProxos(...)
```

Script PowerShell de migração para incluir no repositório:

```powershell
# migrate-to-proxos.ps1
Get-ChildItem -Recurse -Filter "*.cs" | ForEach-Object {
    (Get-Content $_.FullName) `
        -replace 'using MediatR;',        'using Proxos;' `
        -replace 'using MediatR\.Pipeline;', 'using Proxos;' `
        -replace 'services\.AddMediatR\(', 'services.AddProxos(' |
    Set-Content $_.FullName
}
```

---

## 18. Checklist — Tudo Resolvido

### Decisões tomadas

- [x] **Target framework:** `net10.0` exclusivamente
- [x] **Namespace:** `Proxos` (Opção B — namespace próprio)
- [x] **Nome do pacote NuGet:** `Proxos` e `Proxos.Testing`
- [x] **`IRequestExceptionHandler`:** incluído na v1
- [x] **`IStreamRequest`:** incluído na v1
- [x] **`Proxos.Testing`:** incluído na v1
- [x] **Ordem de behaviors:** via parâmetro `order:` + `RegistrationIndex` como desempate
- [x] **Diagnósticos:** built-in, habilitados por padrão, desabilitáveis via `EnableDiagnostics = false`
- [x] **Source Generator:** planejado para v1 (registration + dispatch table)
- [x] **Analyzer PRX001:** planejado para v1 (warning de handler ausente)
- [x] **Cache:** `ConcurrentDictionary` lazy + `FrozenDictionary` pós-warm-up

### Correções aplicadas (bugs do plano anterior)

- [x] **`[IgnoreBehavior]`** corrigido: usa `Type` como parâmetro — `[IgnoreBehavior(typeof(LoggingBehavior<,>))]`
- [x] **`BehaviorRegistration`** agora inclui `RegistrationIndex` para desempate correto na ordenação
- [x] **`ThenBy((r, i) => i)`** substituído por `.ThenBy(r => r.RegistrationIndex)` (sintaxe válida)
- [x] **`ValueStopwatch`** substituído por `Stopwatch.GetTimestamp()` / `Stopwatch.GetElapsedTime()`
- [x] **`ThisAssembly.Version`** substituído por `typeof(ProxosDiagnostics).Assembly.GetName().Version`
- [x] **`services.Add<TService, TImpl>()`** substituído por `new ServiceDescriptor(...)` (extensão inexistente)
- [x] **`UseGeneratedRegistrations()`** removido — `AddProxosGenerated()` é chamado separado de `AddProxos()`
- [x] **`NotificationHandlerWrapper`** agora recebe `ILogger?` e loga exceções no `FireAndForget`
- [x] **Mediator** agora injeta `ILogger<Mediator>?` como dependência opcional
- [x] **Escopo por request** decidido: Mediator **não cria escopo próprio** — documentado no código
- [x] **`Microsoft.Extensions.Logging.Abstractions`** adicionado ao `Proxos.csproj`

### Antes de escrever a primeira linha de código

- [x] Plano detalhado revisado, corrigido e aprovado
- [ ] Criar a solution e estrutura de projetos
- [ ] Implementar na ordem: interfaces → Unit → Atributos → Context → Diagnostics → Configuration → Internal → Mediator → Extensions → Tests → Generator → Analyzer → Testing
