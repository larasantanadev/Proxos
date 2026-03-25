namespace Proxos;

/// <summary>
/// Delegate que representa o próximo passo na cadeia do pipeline.
/// </summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Delegate para o próximo passo em um pipeline de stream.
/// </summary>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<TResponse>();

/// <summary>
/// Behavior padrão — envolve o handler como middleware.
/// Implementações são executadas em ordem crescente de <c>order</c> (registrado via DI).
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
