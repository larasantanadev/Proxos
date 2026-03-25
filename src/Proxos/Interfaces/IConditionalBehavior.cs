namespace Proxos;

/// <summary>
/// Behavior que pode se auto-desabilitar em runtime.
/// Se <see cref="ShouldHandle"/> retornar <c>false</c>, o mediator chama <c>next()</c>
/// diretamente, pulando este behavior sem custo de alocação.
/// </summary>
public interface IConditionalBehavior<in TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Retorna <c>true</c> se este behavior deve processar o request;
    /// <c>false</c> para ser ignorado e chamar o próximo passo diretamente.
    /// </summary>
    bool ShouldHandle(TRequest request);
}
