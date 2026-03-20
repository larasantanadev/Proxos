namespace HermesMediator.Context;

/// <summary>
/// Contexto compartilhado entre todos os behaviors e o handler de um mesmo request.
/// Permite passar dados arbitrários ao longo do pipeline sem poluir as assinaturas.
/// </summary>
public sealed class HermesPipelineContext
{
    private readonly Dictionary<string, object?> _items = new(StringComparer.Ordinal);

    /// <summary>O request em execução.</summary>
    public object Request { get; internal set; } = null!;

    /// <summary>CancellationToken efetivo (pode incluir timeout do [RequestTimeout]).</summary>
    public CancellationToken CancellationToken { get; internal set; }

    /// <summary>Momento em que o pipeline foi iniciado (UTC).</summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>Armazena um valor no contexto do pipeline.</summary>
    public void Set<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        _items[key] = value;
    }

    /// <summary>Recupera um valor do contexto do pipeline.</summary>
    public T? Get<T>(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _items.TryGetValue(key, out var value) ? (T?)value : default;
    }

    /// <summary>Tenta recuperar um valor do contexto do pipeline.</summary>
    public bool TryGet<T>(string key, out T? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (_items.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Remove um valor do contexto do pipeline.</summary>
    public bool Remove(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _items.Remove(key);
    }

    /// <summary>Verifica se uma chave existe no contexto.</summary>
    public bool ContainsKey(string key) => _items.ContainsKey(key);
}
