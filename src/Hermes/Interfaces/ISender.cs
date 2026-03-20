namespace HermesMediator;

/// <summary>Envia requests e retorna respostas tipadas.</summary>
public interface ISender
{
    /// <summary>Envia um request e aguarda a resposta tipada.</summary>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>Envia um request sem retorno explícito.</summary>
    Task Send(IRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatch dinâmico — útil quando o tipo do request é descoberto em runtime.
    /// O request deve implementar <see cref="IRequest{TResponse}"/> ou <see cref="IRequest"/>.
    /// </summary>
    Task<object?> Send(object request, CancellationToken cancellationToken = default);

    /// <summary>Cria um stream assíncrono a partir de um <see cref="IStreamRequest{TResponse}"/>.</summary>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream com dispatch dinâmico — útil quando o tipo é descoberto em runtime.
    /// O request deve implementar <see cref="IStreamRequest{TResponse}"/>.
    /// </summary>
    IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default);
}
