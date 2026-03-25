using System.Collections.Frozen;
using Microsoft.Extensions.Hosting;

namespace Proxos.Internal;

/// <summary>
/// Serviço de warmup executado no startup da aplicação.
/// Pré-aquece o dicionário de wrappers e o converte em <see cref="FrozenDictionary{TKey,TValue}"/>
/// para acesso sem lock e sem alocação em hot path.
/// </summary>
internal sealed class WarmupService : IHostedService
{
    private readonly WrapperRegistry _registry;

    public WarmupService(WrapperRegistry registry) => _registry = registry;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry.Freeze();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Registry mutável de wrappers — populado durante o AddProxos() e
/// congelado em FrozenDictionary pelo WarmupService (ou lazily no primeiro acesso,
/// para cenários sem IHost como aplicações console).
/// </summary>
internal sealed class WrapperRegistry
{
    private Dictionary<Type, RequestHandlerWrapperBase>? _requestWrappers = [];
    private Dictionary<Type, NotificationHandlerWrapperBase>? _notificationWrappers = [];
    private Dictionary<Type, StreamHandlerWrapperBase>? _streamWrappers = [];

    private FrozenDictionary<Type, RequestHandlerWrapperBase>? _frozenRequests;
    private FrozenDictionary<Type, NotificationHandlerWrapperBase>? _frozenNotifications;
    private FrozenDictionary<Type, StreamHandlerWrapperBase>? _frozenStreams;

    // Lock para garantir freeze thread-safe no lazy path (primeiro acesso sem WarmupService).
    private readonly object _freezeLock = new();

    internal void RegisterRequest<TRequest, TResponse>()
        where TRequest : notnull, IRequest<TResponse>
    {
        _requestWrappers![typeof(TRequest)] = new RequestHandlerWrapper<TRequest, TResponse>();
    }

    internal void RegisterNotification<TNotification>()
        where TNotification : INotification
    {
        _notificationWrappers![typeof(TNotification)] = new NotificationHandlerWrapper<TNotification>();
    }

    internal void RegisterStream<TRequest, TResponse>()
        where TRequest : notnull, IStreamRequest<TResponse>
    {
        _streamWrappers![typeof(TRequest)] = new StreamHandlerWrapper<TRequest, TResponse>();
    }

    internal void Freeze()
    {
        _frozenRequests = _requestWrappers!.ToFrozenDictionary();
        _frozenNotifications = _notificationWrappers!.ToFrozenDictionary();
        _frozenStreams = _streamWrappers!.ToFrozenDictionary();

        // Libera os dicionários mutáveis
        _requestWrappers = null;
        _notificationWrappers = null;
        _streamWrappers = null;
    }

    internal RequestHandlerWrapperBase GetRequestWrapper(Type requestType)
    {
        var dict = GetFrozenRequests();
        if (!dict.TryGetValue(requestType, out var wrapper))
            throw new InvalidOperationException(
                $"Nenhum handler registrado para '{requestType.Name}'. " +
                $"Verifique se o assembly foi incluído em AddProxos() ou se existe um IRequestHandler<{requestType.Name},...>.");
        return wrapper;
    }

    internal NotificationHandlerWrapperBase? GetNotificationWrapper(Type notificationType)
    {
        GetFrozenRequests(); // garante que o freeze ocorreu
        _frozenNotifications!.TryGetValue(notificationType, out var wrapper);
        return wrapper;
    }

    internal StreamHandlerWrapperBase GetStreamWrapper(Type requestType)
    {
        GetFrozenRequests(); // garante que o freeze ocorreu
        if (!_frozenStreams!.TryGetValue(requestType, out var wrapper))
            throw new InvalidOperationException(
                $"Nenhum handler de stream registrado para '{requestType.Name}'.");
        return wrapper;
    }

    /// <summary>
    /// Retorna o FrozenDictionary de requests, executando um lazy freeze thread-safe
    /// caso o <see cref="WarmupService"/> não tenha sido invocado (ex: app console sem IHost).
    /// </summary>
    private FrozenDictionary<Type, RequestHandlerWrapperBase> GetFrozenRequests()
    {
        if (_frozenRequests is not null)
            return _frozenRequests;

        lock (_freezeLock)
        {
            if (_frozenRequests is not null)
                return _frozenRequests;

            Freeze();
            return _frozenRequests!;
        }
    }
}
