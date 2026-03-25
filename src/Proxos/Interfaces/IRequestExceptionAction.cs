namespace Proxos;

/// <summary>
/// Ação de side-effect para exceções de um request.
/// Diferente de <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>,
/// esta ação <b>nunca suprime</b> a exceção — apenas executa efeitos colaterais
/// como logging, métricas ou auditoria.
/// A exceção é sempre repropagada após todas as ações executarem.
/// </summary>
/// <typeparam name="TRequest">Tipo do request.</typeparam>
/// <typeparam name="TException">Tipo da exceção (ou base). Suporta hierarquia: registrar para
/// <c>Exception</c> executa para qualquer exceção.</typeparam>
/// <example>
/// <code>
/// // Executa para qualquer InvalidOperationException (e derivadas) de MyCommand
/// public class MyCommandErrorLogger : IRequestExceptionAction&lt;MyCommand, InvalidOperationException&gt;
/// {
///     public Task Execute(MyCommand request, InvalidOperationException exception, CancellationToken ct)
///     {
///         logger.LogError(exception, "Falha ao processar {Request}", request);
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </example>
public interface IRequestExceptionAction<in TRequest, in TException>
    where TRequest : notnull
    where TException : Exception
{
    /// <summary>
    /// Executado quando o pipeline lança uma exceção do tipo <typeparamref name="TException"/>
    /// (ou derivada). A exceção será repropagada automaticamente após este método retornar.
    /// </summary>
    Task Execute(TRequest request, TException exception, CancellationToken cancellationToken);
}
