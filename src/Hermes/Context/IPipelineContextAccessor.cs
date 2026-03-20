namespace HermesMediator.Context;

/// <summary>
/// Permite acessar o <see cref="HermesPipelineContext"/> do pipeline atual
/// a partir de qualquer serviço injetado (handler, behavior, serviço de domínio).
/// Escopo: por request (scoped via AsyncLocal).
/// </summary>
public interface IPipelineContextAccessor
{
    /// <summary>
    /// Contexto do pipeline atual. <c>null</c> se chamado fora de um pipeline do Hermes.
    /// </summary>
    HermesPipelineContext? Context { get; }
}
