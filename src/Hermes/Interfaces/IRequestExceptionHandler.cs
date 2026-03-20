namespace HermesMediator;

/// <summary>
/// Handler de exceção para um request específico.
/// Permite tratar, suprimir ou relançar exceções de forma centralizada.
/// </summary>
public interface IRequestExceptionHandler<in TRequest, TResponse, in TException>
    where TRequest : notnull
    where TException : Exception
{
    Task Handle(
        TRequest request,
        TException exception,
        RequestExceptionHandlerState<TResponse> state,
        CancellationToken cancellationToken);
}

/// <summary>
/// Estado mutável passado ao <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>.
/// Use <see cref="SetHandled"/> para suprimir a exceção e retornar um resultado alternativo.
/// </summary>
public sealed class RequestExceptionHandlerState<TResponse>
{
    private bool _handled;
    private TResponse? _response;

    /// <summary>Indica se a exceção foi tratada (suprimida).</summary>
    public bool Handled => _handled;

    /// <summary>Resposta alternativa definida quando <see cref="SetHandled"/> foi chamado.</summary>
    public TResponse? Response => _response;

    /// <summary>
    /// Marca a exceção como tratada e fornece uma resposta alternativa.
    /// O mediator retornará <paramref name="response"/> em vez de propagar a exceção.
    /// </summary>
    public void SetHandled(TResponse response)
    {
        _response = response;
        _handled = true;
    }
}
