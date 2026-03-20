namespace HermesMediator;

/// <summary>Handler para notificações. Pode haver N handlers por notificação.</summary>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}
