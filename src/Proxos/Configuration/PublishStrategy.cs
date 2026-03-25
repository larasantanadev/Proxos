namespace Proxos.Configuration;

/// <summary>Define como o mediator executa múltiplos handlers de uma notificação.</summary>
public enum PublishStrategy
{
    /// <summary>
    /// Executa os handlers um após o outro, em sequência (await foreach).
    /// Se um handler lançar exceção, os próximos não são executados.
    /// Padrão do Proxos.
    /// </summary>
    ForeachAwait = 0,

    /// <summary>
    /// Executa todos os handlers em paralelo (<c>Task.WhenAll</c>).
    /// Todas as exceções são agregadas em um <see cref="AggregateException"/>.
    /// </summary>
    WhenAll = 1,

    /// <summary>
    /// Executa todos os handlers em paralelo, mas ignora falhas individuais.
    /// Útil para notificações de melhor esforço (fire-and-forget).
    /// </summary>
    WhenAllContinueOnError = 2,
}
