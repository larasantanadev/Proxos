namespace HermesMediator;

/// <summary>
/// Representa a ausência de retorno tipado — equivalente ao <c>void</c> em contextos genéricos.
/// Use em handlers que não precisam retornar nenhum valor.
/// </summary>
[System.Diagnostics.DebuggerDisplay("()")]
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>, IComparable
{
    /// <summary>Instância singleton de <see cref="Unit"/>.</summary>
    public static readonly Unit Value = new();

    /// <summary>Task já completa que retorna <see cref="Value"/>.</summary>
    public static readonly Task<Unit> Task = System.Threading.Tasks.Task.FromResult(Value);

    public bool Equals(Unit other) => true;
    public override bool Equals(object? obj) => obj is Unit;
    public override int GetHashCode() => 0;
    public int CompareTo(Unit other) => 0;
    public int CompareTo(object? obj) => 0;

    public static bool operator ==(Unit left, Unit right) => true;
    public static bool operator !=(Unit left, Unit right) => false;
    public static bool operator <(Unit left, Unit right) => false;
    public static bool operator <=(Unit left, Unit right) => true;
    public static bool operator >(Unit left, Unit right) => false;
    public static bool operator >=(Unit left, Unit right) => true;

    public override string ToString() => "()";
}
