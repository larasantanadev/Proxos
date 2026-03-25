using System.Runtime.CompilerServices;

namespace Proxos.Testing;

/// <summary>
/// Implementação fake do <see cref="IMediator"/> para testes unitários.
/// API fluente: setup de retornos, captura de chamadas, verificação de invocações.
/// </summary>
public sealed class FakeMediator : IMediator
{
    private readonly Dictionary<Type, Func<object, CancellationToken, Task<object?>>> _sendSetups = [];
    private readonly Dictionary<Type, Func<object, CancellationToken, IAsyncEnumerable<object?>>> _streamSetups = [];
    private readonly List<object> _sentRequests = [];
    private readonly List<object> _publishedNotifications = [];

    // -------------------------------------------------------------------------
    // Setup API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configura o retorno para um tipo de request específico.
    /// </summary>
    public FakeMediator Setup<TRequest, TResponse>(Func<TRequest, TResponse> handler)
        where TRequest : IRequest<TResponse>
    {
        _sendSetups[typeof(TRequest)] = (req, _) =>
            Task.FromResult<object?>(handler((TRequest)req));
        return this;
    }

    /// <summary>
    /// Configura o retorno assíncrono para um tipo de request específico.
    /// </summary>
    public FakeMediator Setup<TRequest, TResponse>(Func<TRequest, CancellationToken, Task<TResponse>> handler)
        where TRequest : IRequest<TResponse>
    {
        _sendSetups[typeof(TRequest)] = async (req, ct) =>
            (object?)await handler((TRequest)req, ct);
        return this;
    }

    /// <summary>
    /// Configura uma resposta fixa para um tipo de request.
    /// </summary>
    public FakeMediator Returns<TRequest, TResponse>(TResponse response)
        where TRequest : IRequest<TResponse>
    {
        _sendSetups[typeof(TRequest)] = (_, _) => Task.FromResult<object?>(response);
        return this;
    }

    /// <summary>
    /// Configura uma exceção para ser lançada quando um request específico for enviado.
    /// </summary>
    public FakeMediator Throws<TRequest>(Exception exception)
        where TRequest : notnull
    {
        _sendSetups[typeof(TRequest)] = (_, _) => Task.FromException<object?>(exception);
        return this;
    }

    /// <summary>
    /// Configura o stream de retorno para um tipo de stream request.
    /// </summary>
    public FakeMediator SetupStream<TRequest, TResponse>(Func<TRequest, IAsyncEnumerable<TResponse>> handler)
        where TRequest : IStreamRequest<TResponse>
    {
        _streamSetups[typeof(TRequest)] = (req, _) =>
            handler((TRequest)req).AsObjects();
        return this;
    }

    /// <summary>
    /// Configura um stream fixo para um tipo de stream request.
    /// </summary>
    public FakeMediator ReturnsStream<TRequest, TResponse>(IEnumerable<TResponse> items)
        where TRequest : IStreamRequest<TResponse>
    {
        _streamSetups[typeof(TRequest)] = (_, _) =>
            items.ToAsyncEnumerable().AsObjects();
        return this;
    }

    // -------------------------------------------------------------------------
    // IMediator / ISender / IPublisher
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _sentRequests.Add(request);

        if (_sendSetups.TryGetValue(request.GetType(), out var setup))
            return (TResponse)(await setup(request, cancellationToken))!;

        throw new InvalidOperationException(
            $"FakeMediator: nenhum setup configurado para '{request.GetType().Name}'. " +
            $"Use .Setup<{request.GetType().Name}, TResponse>(...) ou .Returns<{request.GetType().Name}, TResponse>(...).");
    }

    /// <inheritdoc/>
    public async Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        ArgumentNullException.ThrowIfNull(request);
        _sentRequests.Add(request);

        if (_sendSetups.TryGetValue(request.GetType(), out var setup))
            await setup(request, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task Send(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _sentRequests.Add(request);

        if (_sendSetups.TryGetValue(request.GetType(), out var setup))
        {
            await setup(request, cancellationToken);
            return;
        }

        // Se não há setup, retorna Unit (comportamento padrão para void requests)
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _sentRequests.Add(request);

        if (_streamSetups.TryGetValue(request.GetType(), out var setup))
            return setup(request, cancellationToken).Cast<TResponse>(cancellationToken);

        throw new InvalidOperationException(
            $"FakeMediator: nenhum stream setup configurado para '{request.GetType().Name}'.");
    }

    /// <inheritdoc/>
    public async Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _sentRequests.Add(request);

        if (_sendSetups.TryGetValue(request.GetType(), out var setup))
            return await setup(request, cancellationToken);

        throw new InvalidOperationException(
            $"FakeMediator: nenhum setup configurado para '{request.GetType().Name}'.");
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _sentRequests.Add(request);

        if (_streamSetups.TryGetValue(request.GetType(), out var setup))
            return setup(request, cancellationToken);

        throw new InvalidOperationException(
            $"FakeMediator: nenhum stream setup configurado para '{request.GetType().Name}'.");
    }

    /// <inheritdoc/>
    public Task Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        _publishedNotifications.Add(notification);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        _publishedNotifications.Add(notification);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification is not INotification typed)
            throw new ArgumentException(
                $"'{notification.GetType().Name}' não implementa INotification.", nameof(notification));
        _publishedNotifications.Add(typed);
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Verify API
    // -------------------------------------------------------------------------

    /// <summary>Retorna todos os requests enviados via <c>Send</c> ou <c>CreateStream</c>.</summary>
    public IReadOnlyList<object> SentRequests => _sentRequests;

    /// <summary>Retorna todas as notificações publicadas via <c>Publish</c>.</summary>
    public IReadOnlyList<object> PublishedNotifications => _publishedNotifications;

    /// <summary>Retorna os requests enviados do tipo <typeparamref name="TRequest"/>.</summary>
    public IReadOnlyList<TRequest> SentOf<TRequest>() =>
        _sentRequests.OfType<TRequest>().ToList();

    /// <summary>Retorna as notificações publicadas do tipo <typeparamref name="TNotification"/>.</summary>
    public IReadOnlyList<TNotification> PublishedOf<TNotification>() =>
        _publishedNotifications.OfType<TNotification>().ToList();

    /// <summary>Verifica se um request do tipo <typeparamref name="TRequest"/> foi enviado.</summary>
    public bool WasSent<TRequest>() => _sentRequests.OfType<TRequest>().Any();

    /// <summary>Verifica se uma notificação do tipo <typeparamref name="TNotification"/> foi publicada.</summary>
    public bool WasPublished<TNotification>() => _publishedNotifications.OfType<TNotification>().Any();

    /// <summary>
    /// Verifica se um request do tipo <typeparamref name="TRequest"/> foi enviado que satisfaz o predicado.
    /// </summary>
    public bool WasSent<TRequest>(Func<TRequest, bool> predicate) =>
        _sentRequests.OfType<TRequest>().Any(predicate);

    /// <summary>
    /// Verifica se uma notificação do tipo <typeparamref name="TNotification"/> foi publicada que satisfaz o predicado.
    /// </summary>
    public bool WasPublished<TNotification>(Func<TNotification, bool> predicate) =>
        _publishedNotifications.OfType<TNotification>().Any(predicate);

    /// <summary>Conta quantas vezes um request do tipo <typeparamref name="TRequest"/> foi enviado.</summary>
    public int CountSent<TRequest>() => _sentRequests.OfType<TRequest>().Count();

    /// <summary>Limpa todo o histórico de chamadas (mas mantém os setups).</summary>
    public FakeMediator Reset()
    {
        _sentRequests.Clear();
        _publishedNotifications.Clear();
        return this;
    }
}

file static class AsyncEnumerableHelpers
{
    internal static async IAsyncEnumerable<object?> AsObjects<T>(
        this IAsyncEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken))
            yield return item;
    }

    internal static async IAsyncEnumerable<T> Cast<T>(
        this IAsyncEnumerable<object?> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken))
            yield return (T)item!;
    }

    internal static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }
}
