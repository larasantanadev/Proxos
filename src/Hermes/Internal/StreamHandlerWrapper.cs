using System.Reflection;
using System.Runtime.CompilerServices;
using HermesMediator.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HermesMediator.Internal;

/// <summary>Wrapper abstrato para streams.</summary>
internal abstract class StreamHandlerWrapperBase
{
    internal abstract IAsyncEnumerable<object?> Handle(
        object request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}

/// <summary>Wrapper tipado para streams com suporte a IStreamPipelineBehavior.</summary>
internal sealed class StreamHandlerWrapper<TRequest, TResponse> : StreamHandlerWrapperBase
    where TRequest : notnull, IStreamRequest<TResponse>
{
    internal override IAsyncEnumerable<object?> Handle(
        object request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        return HandleTyped((TRequest)request, serviceProvider, cancellationToken)
            .SelectAsObject(cancellationToken);
    }

    private static IAsyncEnumerable<TResponse> HandleTyped(
        TRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        using var activity = HermesDiagnostics.StartStreamActivity(typeof(TRequest).Name);

        var behaviors = serviceProvider
            .GetServices<IStreamPipelineBehavior<TRequest, TResponse>>()
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

        return pipeline();
    }

    private static int GetBehaviorOrder(IStreamPipelineBehavior<TRequest, TResponse> behavior)
    {
        var attr = behavior.GetType()
            .GetCustomAttributes(typeof(BehaviorOrderAttribute), inherit: true)
            .Cast<BehaviorOrderAttribute>()
            .FirstOrDefault();
        return attr?.Order ?? 0;
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
