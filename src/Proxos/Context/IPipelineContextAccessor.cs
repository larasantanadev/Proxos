namespace Proxos.Context;

/// <summary>
/// Permite acessar o <see cref="ProxosPipelineContext"/> do pipeline atual
/// a partir de qualquer serviço injetado (handler, behavior, serviço de domínio).
/// Escopo: por request (scoped via AsyncLocal).
/// </summary>
public interface IPipelineContextAccessor
{
    /// <summary>
    /// Contexto do pipeline atual. <c>null</c> se chamado fora de um pipeline do Proxos.
    /// </summary>
    ProxosPipelineContext? Context { get; }
}
