using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HermesMediator.Context;
using HermesMediator.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HermesMediator.Internal;

/// <summary>
/// Wrapper abstrato — permite que o <see cref="Mediator"/> armazene handlers
/// em dicionário sem conhecer o tipo genérico em tempo de compilação.
/// </summary>
internal abstract class RequestHandlerWrapperBase
{
    internal abstract Task<object?> Handle(
        object request,
        IServiceProvider serviceProvider,
        PipelineContextAccessor contextAccessor,
        CancellationToken cancellationToken);
}

/// <summary>
/// Wrapper concreto e tipado que executa o pipeline completo:
/// pre-processors → behaviors → handler → post-processors →
/// exception actions (side-effects) → exception handlers (supressão).
/// </summary>
internal sealed class RequestHandlerWrapper<TRequest, TResponse> : RequestHandlerWrapperBase
    where TRequest : notnull, IRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;
    private static readonly FrozenSet<Type> IgnoredBehaviorTypes = ComputeIgnoredTypes();
    private static readonly int? TimeoutMs = ComputeTimeout();

    // Cache de MethodInfo por tipo de exceção — um cache por instanciação genérica.
    // Evita reflection repetida no hot path de exceções.
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

    internal override async Task<object?> Handle(
        object request,
        IServiceProvider serviceProvider,
        PipelineContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        var typedRequest = (TRequest)request;

        using var timeoutCts = TimeoutMs.HasValue
            ? new CancellationTokenSource(TimeoutMs.Value)
            : null;

        using var linkedCts = timeoutCts is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
            : null;

        var effectiveCt = linkedCts?.Token ?? cancellationToken;

        var context = new HermesPipelineContext
        {
            Request = typedRequest,
            CancellationToken = effectiveCt,
        };

        using var _ = PipelineContextAccessor.SetContext(context);

        var sw = Stopwatch.StartNew();
        var tag = new KeyValuePair<string, object?>("request.type", RequestName);

        HermesDiagnostics.RequestsTotal.Add(1, [tag]);

        using var activity = HermesDiagnostics.StartRequestActivity(RequestName);

        try
        {
            var result = await ExecutePipeline(typedRequest, serviceProvider, effectiveCt);
            sw.Stop();
            HermesDiagnostics.RequestDuration.Record(sw.Elapsed.TotalMilliseconds, [tag]);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            HermesDiagnostics.TimeoutsTotal.Add(1, [tag]);
            activity?.SetStatus(ActivityStatusCode.Error, "Timeout");
            throw new TimeoutException(
                $"O request '{RequestName}' excedeu o timeout de {TimeoutMs} ms.");
        }
        catch (Exception ex)
        {
            HermesDiagnostics.RequestsFailed.Add(1, [tag]);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

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
            // 1. Executa ações de side-effect (IRequestExceptionAction) — sempre repropaga.
            await ExecuteExceptionActions(ex, request, serviceProvider, cancellationToken);

            // 2. Tenta suprimir via handlers (IRequestExceptionHandler).
            var (handled, result) = await TryHandleWithExceptionHandlers(
                ex, request, serviceProvider, cancellationToken);

            if (handled)
            {
                response = result!;
            }
            else
            {
                // Preserva o stack trace original ao reproparar.
                ExceptionDispatchInfo.Capture(ex).Throw();
                return default!; // unreachable
            }
        }

        var postProcessors = serviceProvider.GetServices<IRequestPostProcessor<TRequest, TResponse>>();
        foreach (var post in postProcessors)
            await post.Process(request, response, cancellationToken);

        return response;
    }

    /// <summary>
    /// Executa todos os <see cref="IRequestExceptionAction{TRequest,TException}"/> registrados,
    /// percorrendo a hierarquia de tipos da exceção (do mais específico ao mais genérico).
    /// Cada implementação concreta é executada no máximo uma vez (deduplicação por tipo).
    /// Nunca suprime a exceção — apenas executa efeitos colaterais.
    /// </summary>
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

    /// <summary>
    /// Executa os <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/> registrados,
    /// percorrendo a hierarquia de tipos da exceção. Para no primeiro que chamar
    /// <see cref="RequestExceptionHandlerState{TResponse}.SetHandled"/>.
    /// Cada implementação concreta é executada no máximo uma vez (deduplicação por tipo).
    /// </summary>
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

    /// <summary>
    /// Itera a hierarquia de tipos de uma exceção do mais específico ao mais genérico.
    /// Ex: InvalidOperationException → Exception → (para em object).
    /// </summary>
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
    {
        return _actionExecuteMethods.GetOrAdd(exceptionType, et =>
        {
            var ifaceType = typeof(IRequestExceptionAction<,>)
                .MakeGenericType(typeof(TRequest), et);
            return ifaceType.GetMethod(nameof(IRequestExceptionAction<TRequest, Exception>.Execute))!;
        });
    }

    private static MethodInfo GetHandlerHandleMethod(Type exceptionType)
    {
        return _handlerHandleMethods.GetOrAdd(exceptionType, et =>
        {
            var ifaceType = typeof(IRequestExceptionHandler<,,>)
                .MakeGenericType(typeof(TRequest), typeof(TResponse), et);
            return ifaceType.GetMethod(nameof(IRequestExceptionHandler<TRequest, TResponse, Exception>.Handle))!;
        });
    }

    /// <summary>
    /// Retorna a ordem de execução do behavior, lida do <see cref="BehaviorOrderAttribute"/>.
    /// Behaviors sem o atributo recebem ordem 0. Menor valor = executa primeiro (mais externo).
    /// </summary>
    private static int GetBehaviorOrder(IPipelineBehavior<TRequest, TResponse> behavior)
    {
        var attr = behavior.GetType()
            .GetCustomAttributes(typeof(BehaviorOrderAttribute), inherit: true)
            .Cast<BehaviorOrderAttribute>()
            .FirstOrDefault();
        return attr?.Order ?? 0;
    }

    private static bool IsIgnored(IPipelineBehavior<TRequest, TResponse> behavior)
    {
        if (IgnoredBehaviorTypes.Count == 0)
            return false;

        var behaviorType = behavior.GetType();
        return IgnoredBehaviorTypes.Contains(behaviorType)
            || (behaviorType.IsGenericType
                && IgnoredBehaviorTypes.Contains(behaviorType.GetGenericTypeDefinition()));
    }
}
