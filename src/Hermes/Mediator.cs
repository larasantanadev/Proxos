using HermesMediator.Configuration;
using HermesMediator.Context;
using HermesMediator.Internal;

namespace HermesMediator;

/// <summary>
/// Implementação principal do Hermes.
/// Dispatch sem reflection em hot path — wrappers são resolvidos via FrozenDictionary
/// populado em startup.
/// </summary>
public sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WrapperRegistry _registry;
    private readonly PipelineContextAccessor _contextAccessor;
    private readonly PublishStrategy _defaultPublishStrategy;

    internal Mediator(
        IServiceProvider serviceProvider,
        WrapperRegistry registry,
        IPipelineContextAccessor contextAccessor,
        HermesConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _contextAccessor = (PipelineContextAccessor)contextAccessor;
        _defaultPublishStrategy = configuration.DefaultPublishStrategy;
    }

    // -------------------------------------------------------------------------
    // ISender — tipado
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = _registry.GetRequestWrapper(request.GetType());
        var result = await wrapper.Handle(request, _serviceProvider, _contextAccessor, cancellationToken);
        return (TResponse)result!;
    }

    /// <inheritdoc/>
    public async Task Send(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = _registry.GetRequestWrapper(request.GetType());
        await wrapper.Handle(request, _serviceProvider, _contextAccessor, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // ISender — dispatch dinâmico
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        if (!requestType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            throw new ArgumentException(
                $"'{requestType.Name}' não implementa IRequest<TResponse>. " +
                $"Apenas tipos que implementam IRequest<TResponse> podem ser enviados via Send(object).",
                nameof(request));

        var wrapper = _registry.GetRequestWrapper(requestType);
        return await wrapper.Handle(request, _serviceProvider, _contextAccessor, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = _registry.GetStreamWrapper(request.GetType());
        return wrapper.Handle(request, _serviceProvider, cancellationToken)
                      .Cast<TResponse>();
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = _registry.GetStreamWrapper(request.GetType());
        return wrapper.Handle(request, _serviceProvider, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // IPublisher
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return PublishCore(notification, _defaultPublishStrategy, cancellationToken);
    }

    /// <inheritdoc/>
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        return PublishCore(notification, _defaultPublishStrategy, cancellationToken);
    }

    /// <inheritdoc/>
    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification is not INotification typed)
            throw new ArgumentException(
                $"'{notification.GetType().Name}' não implementa INotification.",
                nameof(notification));

        return PublishCore(typed, _defaultPublishStrategy, cancellationToken);
    }

    private Task PublishCore(
        INotification notification,
        PublishStrategy strategy,
        CancellationToken cancellationToken)
    {
        var wrapper = _registry.GetNotificationWrapper(notification.GetType());
        if (wrapper is null)
            return Task.CompletedTask;

        return wrapper.Publish(notification, strategy, _serviceProvider, cancellationToken);
    }
}

/// <summary>Extensão interna para converter IAsyncEnumerable com cast de tipo.</summary>
file static class AsyncEnumerableCastExtensions
{
    internal static async IAsyncEnumerable<TResult> Cast<TResult>(
        this IAsyncEnumerable<object?> source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken))
            yield return (TResult)item!;
    }
}
