namespace HermesMediator;

/// <summary>
/// Executa antes do handler, dentro do núcleo do pipeline.
/// Útil para validação, logging de entrada, enriquecimento de contexto.
/// </summary>
public interface IRequestPreProcessor<in TRequest>
    where TRequest : notnull
{
    Task Process(TRequest request, CancellationToken cancellationToken);
}
