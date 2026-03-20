namespace HermesMediator;

/// <summary>
/// Define um timeout máximo para a execução do request.
/// O mediator cancelará automaticamente o request se o tempo for excedido.
/// </summary>
/// <example>
/// <code>
/// [RequestTimeout(5000)] // 5 segundos
/// public record DeleteUserCommand(Guid UserId) : IRequest;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class RequestTimeoutAttribute : Attribute
{
    /// <summary>Timeout em milissegundos.</summary>
    public int Milliseconds { get; }

    /// <param name="milliseconds">Timeout máximo em milissegundos. Deve ser maior que zero.</param>
    public RequestTimeoutAttribute(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(milliseconds, 0);
        Milliseconds = milliseconds;
    }
}
