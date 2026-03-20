namespace HermesMediator.Context;

/// <summary>
/// Implementação de <see cref="IPipelineContextAccessor"/> usando <see cref="AsyncLocal{T}"/>
/// para isolar o contexto por cadeia assíncrona (por request), sem lock.
/// </summary>
internal sealed class PipelineContextAccessor : IPipelineContextAccessor
{
    private static readonly AsyncLocal<HermesPipelineContext?> _current = new();

    public HermesPipelineContext? Context
    {
        get => _current.Value;
        internal set => _current.Value = value;
    }

    /// <summary>Define o contexto para o pipeline atual e retorna um disposable que o limpa.</summary>
    internal static IDisposable SetContext(HermesPipelineContext context)
    {
        _current.Value = context;
        return new ContextScope();
    }

    private sealed class ContextScope : IDisposable
    {
        public void Dispose() => _current.Value = null;
    }
}
