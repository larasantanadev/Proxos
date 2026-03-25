namespace Proxos;

/// <summary>
/// Publisher customizável de notificações. Implemente esta interface para substituir
/// completamente a estratégia de publicação padrão do Proxos.
/// Configure via <see cref="Proxos.Configuration.ProxosConfiguration.NotificationPublisher"/>.
/// </summary>
public interface INotificationPublisher
{
    /// <summary>
    /// Publica a notificação para todos os handlers fornecidos.
    /// </summary>
    /// <param name="handlerExecutors">Handlers prontos para execução.</param>
    /// <param name="notification">A notificação a ser publicada.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task Publish(
        IEnumerable<NotificationHandlerExecutor> handlerExecutors,
        INotification notification,
        CancellationToken cancellationToken);
}

/// <summary>
/// Representa um handler de notificação pronto para execução,
/// encapsulando instância e callback de invocação.
/// </summary>
/// <param name="HandlerInstance">Instância do handler.</param>
/// <param name="HandlerCallback">Callback para invocar o handler.</param>
public record NotificationHandlerExecutor(
    object HandlerInstance,
    Func<INotification, CancellationToken, Task> HandlerCallback);
