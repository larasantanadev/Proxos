using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Proxos.Context;
using Proxos.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Proxos.Internal;

// ---------------------------------------------------------------------------
// Base abstrata para dispatch dinâmico (object → Task<object?>)
// ---------------------------------------------------------------------------

/// <summary>
/// Base para armazenamento no FrozenDictionary sem conhecer TResponse em compile-time.
/// </summary>
internal abstract class RequestHandlerWrapperBase
{
    internal abstract Task<object?> Handle(
        object request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}

// ---------------------------------------------------------------------------
// Base tipada — elimina o boxing de Task<object?> no caminho Send<TResponse>
// ---------------------------------------------------------------------------

/// <summary>
/// Base tipada por TResponse. Permite que <see cref="Mediator"/> obtenha
/// <c>Task&lt;TResponse&gt;</c> diretamente, sem criar um <c>Task&lt;object?&gt;</c>
/// intermediário e sem boxing de resultado.
/// </summary>
internal abstract class TypedRequestHandlerWrapper<TResponse> : RequestHandlerWrapperBase
{
    /// <summary>
    /// Caminho tipado — retorna <c>Task&lt;TResponse&gt;</c> sem boxing.
    /// Usado por <c>Mediator.Send&lt;TResponse&gt;</c>.
    /// </summary>
    internal abstract Task<TResponse> HandleTyped(
        object request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);

    /// <summary>
    /// Implementação padrão do dispatch dinâmico: encaminha para HandleTyped e boxa o resultado.
    /// Usado por <c>Send(object)</c>.
    /// </summary>
    internal override Task<object?> Handle(
        object request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var typedTask = HandleTyped(request, serviceProvider, cancellationToken);

        if (typedTask.IsCompletedSuccessfully)
            return Task.FromResult<object?>(typedTask.Result);

        return BoxAsync(typedTask);
    }

    private static async Task<object?> BoxAsync(Task<TResponse> task) => await task;
}

// ---------------------------------------------------------------------------
// Implementação concreta — pipeline completo
// ---------------------------------------------------------------------------

/// <summary>
/// Wrapper concreto e tipado que executa o pipeline completo:
/// pre-processors → behaviors → handler → post-processors →
/// exception actions (side-effects) → exception handlers (supressão).
/// </summary>
internal sealed class RequestHandlerWrapper<TRequest, TResponse> : TypedRequestHandlerWrapper<TResponse>
    where TRequest : notnull, IRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;
    private static readonly FrozenSet<Type> IgnoredBehaviorTypes = ComputeIgnoredTypes();
    private static readonly int? TimeoutMs = ComputeTimeout();

    // 0 = desconhecido, 1 = simples (sem behaviors/processors), 2 = completo.
    // Em genéricos, campos estáticos são por instanciação — perfeito para cache por TRequest.
    private static volatile int _pipelineState;

    // Ordem de behaviors cacheada por tipo — lida via reflection apenas uma vez por tipo.
    private static readonly ConcurrentDictionary<Type, int> _behaviorOrderCache = new();

    // Cache de MethodInfo por tipo de exceção — reflection apenas uma vez por tipo.
    private static readonly ConcurrentDictionary<Type, MethodInfo> _actionExecuteMethods = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> _handlerHandleMethods = new();

    private static FrozenSet<Type> ComputeIgnoredTypes()
    {
        var attrs = typeof(TRequest)
            .GetCustomAttributes(typeof(IgnoreBehaviorAttribute), inherit: true)
            .Cast<IgnoreBehaviorAttribute>()
            .Select(a => a.BehaviorType);
        return attrs.ToFrozenSet();
    }

    private static int? ComputeTimeout()
    {
        var attr = typeof(TRequest)
            .GetCustomAttributes(typeof(RequestTimeoutAttribute), inherit: true)
            .Cast<RequestTimeoutAttribute>()
            .FirstOrDefault();
        return attr?.Milliseconds;
    }

    private static int ProbePipeline(IServiceProvider sp)
    {
        bool hasBehaviors = sp.GetServices<IPipelineBehavior<TRequest, TResponse>>().Any();
        bool hasPreProc   = sp.GetServices<IRequestPreProcessor<TRequest>>().Any();
        bool hasPostProc  = sp.GetServices<IRequestPostProcessor<TRequest, TResponse>>().Any();
        int state = (!hasBehaviors && !hasPreProc && !hasPostProc) ? 1 : 2;
        _pipelineState = state;
        return state;
    }

    // ---------------------------------------------------------------------------
    // Ponto de entrada tipado (sem boxing)
    // ---------------------------------------------------------------------------

    internal override Task<TResponse> HandleTyped(
        object request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var typedRequest = (TRequest)request;

        int state = _pipelineState;
        if (state == 0) state = ProbePipeline(serviceProvider);

        bool otelActive = ProxosDiagnostics.ActivitySource.HasListeners()
                       || ProxosDiagnostics.RequestsTotal.Enabled;

        if (state == 1 && !TimeoutMs.HasValue && !otelActive)
            return SimplePathTyped(typedRequest, serviceProvider, cancellationToken);

        return FullPathTyped(typedRequest, serviceProvider, cancellationToken, otelActive);
    }

    // ---------------------------------------------------------------------------
    // Fast path — sem behaviors, sem processors, sem OTel, sem timeout
    // Retorna Task<TResponse> diretamente do handler, zero boxing, zero alocações extras.
    // ---------------------------------------------------------------------------

    private static Task<TResponse> SimplePathTyped(
        TRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handler = serviceProvider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

        Task<TResponse> handlerTask;
        try
        {
            handlerTask = handler.Handle(request, cancellationToken);
        }
        catch (Exception ex)
        {
            // Exceção síncrona lançada antes de retornar Task
            return SimplePathHandleExceptionTyped(ex, request, serviceProvider, cancellationToken);
        }

        // Caso síncrono: retorna a Task<TResponse> do handler DIRETAMENTE.
        // Nenhuma alocação extra — o caller recebe a task original.
        if (handlerTask.IsCompletedSuccessfully)
            return handlerTask;

        // Caso assíncrono: aguarda e trata exceções se necessário
        return SimplePathAwaitAsyncTyped(handlerTask, request, serviceProvider, cancellationToken);
    }

    private static async Task<TResponse> SimplePathAwaitAsyncTyped(
        Task<TResponse> handlerTask,
        TRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            return await handlerTask;
        }
        catch (Exception ex)
        {
            return await SimplePathHandleExceptionTyped(ex, request, serviceProvider, cancellationToken);
        }
    }

    private static async Task<TResponse> SimplePathHandleExceptionTyped(
        Exception ex,
        TRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        await ExecuteExceptionActions(ex, request, serviceProvider, cancellationToken);
        var (handled, result) = await TryHandleWithExceptionHandlers(ex, request, serviceProvider, cancellationToken);
        if (handled) return result!;
        ExceptionDispatchInfo.Capture(ex).Throw();
        return default!; // unreachable
    }

    // ---------------------------------------------------------------------------
    // Full path — pipeline completo com context, OTel opcional e timeout
    // ---------------------------------------------------------------------------

    private static async Task<TResponse> FullPathTyped(
        TRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken,
        bool otelActive)
    {
        using var timeoutCts = TimeoutMs.HasValue
            ? new CancellationTokenSource(TimeoutMs.Value)
            : null;

        using var linkedCts = timeoutCts is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
            : null;

        var effectiveCt = linkedCts?.Token ?? cancellationToken;

        var contextAccessor = (PipelineContextAccessor)serviceProvider.GetRequiredService<IPipelineContextAccessor>();
        var context = new ProxosPipelineContext
        {
            Request = request,
            CancellationToken = effectiveCt,
        };
        using var _ = PipelineContextAccessor.SetContext(context);

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

        try
        {
            var result = await ExecutePipeline(request, serviceProvider, effectiveCt);

            if (otelActive)
            {
                sw!.Stop();
                ProxosDiagnostics.RequestDuration.Record(sw.Elapsed.TotalMilliseconds, [tag]);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }

            return result;
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            if (otelActive)
            {
                ProxosDiagnostics.TimeoutsTotal.Add(1, [tag]);
                activity?.SetStatus(ActivityStatusCode.Error, "Timeout");
            }
            throw new TimeoutException(
                $"O request '{RequestName}' excedeu o timeout de {TimeoutMs} ms.");
        }
        catch (Exception ex)
        {
            if (otelActive)
            {
                ProxosDiagnostics.RequestsFailed.Add(1, [tag]);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    // ---------------------------------------------------------------------------
    // Pipeline — pre/post processors, behaviors, exception handling
    // ---------------------------------------------------------------------------

    private static async Task<TResponse> ExecutePipeline(
        TRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var preProcessors = serviceProvider.GetServices<IRequestPreProcessor<TRequest>>();
        foreach (var pre in preProcessors)
            await pre.Process(request, cancellationToken);

        var behaviors = serviceProvider
            .GetServices<IPipelineBehavior<TRequest, TResponse>>()
            .Where(b => !IsIgnored(b))
            .OrderBy(GetBehaviorOrder)
            .ToArray();

        var handler = serviceProvider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

        RequestHandlerDelegate<TResponse> pipeline = () =>
            handler.Handle(request, cancellationToken);

        for (int i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];

            if (behavior is IConditionalBehavior<TRequest, TResponse> conditional
                && !conditional.ShouldHandle(request))
                continue;

            var next = pipeline;
            pipeline = () => behavior.Handle(request, next, cancellationToken);
        }

        TResponse response;
        try
        {
            response = await pipeline();
        }
        catch (Exception ex)
        {
            await ExecuteExceptionActions(ex, request, serviceProvider, cancellationToken);
            var (handled, result) = await TryHandleWithExceptionHandlers(
                ex, request, serviceProvider, cancellationToken);

            if (handled)
            {
                response = result!;
            }
            else
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
                return default!; // unreachable
            }
        }

        var postProcessors = serviceProvider.GetServices<IRequestPostProcessor<TRequest, TResponse>>();
        foreach (var post in postProcessors)
            await post.Process(request, response, cancellationToken);

        return response;
    }

    // ---------------------------------------------------------------------------
    // Exception actions e handlers
    // ---------------------------------------------------------------------------

    private static async Task ExecuteExceptionActions(
        Exception exception,
        TRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var executedTypes = new HashSet<Type>();

        foreach (var exType in GetExceptionTypeHierarchy(exception.GetType()))
        {
            var actionInterface = typeof(IRequestExceptionAction<,>)
                .MakeGenericType(typeof(TRequest), exType);

            var actions = serviceProvider.GetServices(actionInterface);
            var method = GetActionExecuteMethod(exType);

            foreach (var action in actions)
            {
                if (action is null || !executedTypes.Add(action.GetType())) continue;
                await ((Task)method.Invoke(action, [request, exception, cancellationToken])!);
            }
        }
    }

    private static async Task<(bool Handled, TResponse? Response)> TryHandleWithExceptionHandlers(
        Exception exception,
        TRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var state = new RequestExceptionHandlerState<TResponse>();
        var executedTypes = new HashSet<Type>();

        foreach (var exType in GetExceptionTypeHierarchy(exception.GetType()))
        {
            var handlerInterface = typeof(IRequestExceptionHandler<,,>)
                .MakeGenericType(typeof(TRequest), typeof(TResponse), exType);

            var handlers = serviceProvider.GetServices(handlerInterface);
            var method = GetHandlerHandleMethod(exType);

            foreach (var handler in handlers)
            {
                if (handler is null || !executedTypes.Add(handler.GetType())) continue;
                await ((Task)method.Invoke(handler, [request, exception, state, cancellationToken])!);
                if (state.Handled) return (true, state.Response);
            }
        }

        return (false, default);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static IEnumerable<Type> GetExceptionTypeHierarchy(Type exceptionType)
    {
        var current = exceptionType;
        while (current != null && current != typeof(object))
        {
            yield return current;
            current = current.BaseType;
        }
    }

    private static MethodInfo GetActionExecuteMethod(Type exceptionType)
        => _actionExecuteMethods.GetOrAdd(exceptionType, static et =>
        {
            var iface = typeof(IRequestExceptionAction<,>).MakeGenericType(typeof(TRequest), et);
            return iface.GetMethod(nameof(IRequestExceptionAction<TRequest, Exception>.Execute))!;
        });

    private static MethodInfo GetHandlerHandleMethod(Type exceptionType)
        => _handlerHandleMethods.GetOrAdd(exceptionType, static et =>
        {
            var iface = typeof(IRequestExceptionHandler<,,>).MakeGenericType(typeof(TRequest), typeof(TResponse), et);
            return iface.GetMethod(nameof(IRequestExceptionHandler<TRequest, TResponse, Exception>.Handle))!;
        });

    /// <summary>
    /// Ordem de execução do behavior — lida do <see cref="BehaviorOrderAttribute"/> e cacheada por tipo.
    /// Menor valor = executa primeiro (mais externo).
    /// </summary>
    private static int GetBehaviorOrder(IPipelineBehavior<TRequest, TResponse> behavior)
        => _behaviorOrderCache.GetOrAdd(behavior.GetType(), static t =>
        {
            var attr = t.GetCustomAttribute<BehaviorOrderAttribute>(inherit: true);
            return attr?.Order ?? 0;
        });

    private static bool IsIgnored(IPipelineBehavior<TRequest, TResponse> behavior)
    {
        if (IgnoredBehaviorTypes.Count == 0) return false;

        var behaviorType = behavior.GetType();
        return IgnoredBehaviorTypes.Contains(behaviorType)
            || (behaviorType.IsGenericType
                && IgnoredBehaviorTypes.Contains(behaviorType.GetGenericTypeDefinition()));
    }
}
