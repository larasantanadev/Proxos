namespace Proxos;

/// <summary>
/// Executa após o handler, dentro do núcleo do pipeline.
/// Útil para logging de saída, auditoria, transformações de resposta.
/// </summary>
public interface IRequestPostProcessor<in TRequest, in TResponse>
    where TRequest : notnull
{
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}
