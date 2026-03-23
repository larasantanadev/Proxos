using Proxos.Configuration;
using Proxos.Internal;

namespace Proxos;

/// <summary>
/// Implementação principal do Proxos.
/// Dispatch sem reflection no hot path — wrappers resolvidos via FrozenDictionary populado no startup.
/// </summary>
public sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WrapperRegistry _registry;
    private readonly PublishStrategy _defaultPublishStrategy;
    private readonly INotificationPublisher? _notificationPublisher;

    internal Mediator(
        IServiceProvider serviceProvider,
        WrapperRegistry registry,
        ProxosConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _defaultPublishStrategy = configuration.DefaultPublishStrategy;
        _notificationPublisher = configuration.NotificationPublisher;
    }

    // -------------------------------------------------------------------------
    // ISender — tipado com resposta
    // Usa HandleTyped: retorna Task<TResponse> sem Task<object?> intermediário.
    // Zero allocações extras no fast path.
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var wrapper = _registry.GetRequestWrapper(request.GetType());
        return ((TypedRequestHandlerWrapper<TResponse>)wrapper)
            .HandleTyped(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc/>
    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        ArgumentNullException.ThrowIfNull(request);
        var wrapper = _registry.GetRequestWrapper(request.GetType());
        return ((TypedRequestHandlerWrapper<Unit>)wrapper)
            .HandleTyped(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc/>
    public Task Send(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var wrapper = _registry.GetRequestWrapper(request.GetType());
        return ((TypedRequestHandlerWrapper<Unit>)wrapper)
            .HandleTyped(request, _serviceProvider, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // ISender — dispatch dinâmico (object)
    // Usa Handle que boxa o resultado — só para cenários onde TResponse é desconhecido.
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        if (!requestType.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            throw new ArgumentException(
                $"'{requestType.Name}' não implementa IRequest<TResponse>. " +
                $"Apenas tipos que implementam IRequest<TResponse> podem ser enviados via Send(object).",
                nameof(request));

        var wrapper = _registry.GetRequestWrapper(requestType);
        return wrapper.Handle(request, _serviceProvider, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // ISender — streams
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var wrapper = _registry.GetStreamWrapper(request.GetType());
        return wrapper.Handle(request, _serviceProvider, cancellationToken)
                      .Cast<TResponse>(cancellationToken);
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
        if (wrapper is null) return Task.CompletedTask;
        return wrapper.Publish(notification, strategy, _notificationPublisher, _serviceProvider, cancellationToken);
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
