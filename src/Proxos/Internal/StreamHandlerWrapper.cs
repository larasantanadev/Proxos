using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Proxos.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Proxos.Internal;

/// <summary>Wrapper abstrato para streams.</summary>
internal abstract class StreamHandlerWrapperBase
{
    internal abstract IAsyncEnumerable<object?> Handle(
        object request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}

/// <summary>
/// Wrapper tipado para streams com suporte a IStreamPipelineBehavior,
/// [BehaviorOrder], [IgnoreBehavior] e OpenTelemetry condicional.
/// </summary>
internal sealed class StreamHandlerWrapper<TRequest, TResponse> : StreamHandlerWrapperBase
    where TRequest : notnull, IStreamRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;
    private static readonly FrozenSet<Type> IgnoredBehaviorTypes = ComputeIgnoredTypes();

    // Ordem de behaviors cacheada por tipo — reflection apenas uma vez.
    private static readonly ConcurrentDictionary<Type, int> _behaviorOrderCache = new();

    private static FrozenSet<Type> ComputeIgnoredTypes()
    {
        var attrs = typeof(TRequest)
            .GetCustomAttributes(typeof(IgnoreBehaviorAttribute), inherit: true)
            .Cast<IgnoreBehaviorAttribute>()
            .Select(a => a.BehaviorType);
        return attrs.ToFrozenSet();
    }

    internal override IAsyncEnumerable<object?> Handle(
        object request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        return HandleTyped((TRequest)request, serviceProvider, cancellationToken)
            .SelectAsObject(cancellationToken);
    }

    /// <summary>
    /// Pipeline tipado para streams.
    ///
    /// IMPORTANTE: implementado como async iterator para garantir que a OTel Activity
    /// seja descartada APÓS o término da enumeração, não quando o método retorna.
    /// (um método síncrono com 'using var activity' descartaria a activity imediatamente
    /// ao retornar o IAsyncEnumerable, antes de qualquer item ser produzido.)
    /// </summary>
    private static async IAsyncEnumerable<TResponse> HandleTyped(
        TRequest request,
        IServiceProvider serviceProvider,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        bool otelActive = ProxosDiagnostics.ActivitySource.HasListeners();
        using var activity = otelActive
            ? ProxosDiagnostics.StartStreamActivity(RequestName)
            : null;

        var behaviors = serviceProvider
            .GetServices<IStreamPipelineBehavior<TRequest, TResponse>>()
            .Where(b => !IsIgnored(b))
            .OrderBy(GetBehaviorOrder)
            .ToArray();

        var handler = serviceProvider.GetRequiredService<IStreamRequestHandler<TRequest, TResponse>>();

        StreamHandlerDelegate<TResponse> pipeline = () =>
            handler.Handle(request, cancellationToken);

        for (int i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var next = pipeline;
            pipeline = () => behavior.Handle(request, next, cancellationToken);
        }

        // yield return não pode estar em try-catch — apenas em try-finally.
        // Usamos flag 'completed' para distinguir sucesso de falha no finally.
        bool completed = false;
        try
        {
            await foreach (var item in pipeline().WithCancellation(cancellationToken))
                yield return item;
            completed = true;
        }
        finally
        {
            if (otelActive)
                activity?.SetStatus(completed ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
                                    completed ? null : "Stream error");
        }
    }

    /// <summary>
    /// Ordem de execução do behavior — cacheada por tipo de behavior.
    /// Menor valor = executa primeiro (mais externo).
    /// </summary>
    private static int GetBehaviorOrder(IStreamPipelineBehavior<TRequest, TResponse> behavior)
        => _behaviorOrderCache.GetOrAdd(behavior.GetType(), static t =>
        {
            var attr = t.GetCustomAttribute<BehaviorOrderAttribute>(inherit: true);
            return attr?.Order ?? 0;
        });

    private static bool IsIgnored(IStreamPipelineBehavior<TRequest, TResponse> behavior)
    {
        if (IgnoredBehaviorTypes.Count == 0) return false;

        var behaviorType = behavior.GetType();
        return IgnoredBehaviorTypes.Contains(behaviorType)
            || (behaviorType.IsGenericType
                && IgnoredBehaviorTypes.Contains(behaviorType.GetGenericTypeDefinition()));
    }
}

/// <summary>Extensões internas para IAsyncEnumerable.</summary>
internal static class AsyncEnumerableExtensions
{
    internal static async IAsyncEnumerable<object?> SelectAsObject<T>(
        this IAsyncEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken))
            yield return item;
    }
}
