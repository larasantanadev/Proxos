namespace Proxos;

/// <summary>Publica notificações para todos os handlers registrados.</summary>
public interface IPublisher
{
    /// <summary>
    /// Publica uma notificação. A estratégia de execução (sequencial/paralela)
    /// é definida em <see cref="Configuration.ProxosConfiguration"/>.
    /// </summary>
    Task Publish(INotification notification, CancellationToken cancellationToken = default);

    /// <summary>Publica uma notificação fortemente tipada.</summary>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Dispatch dinâmico — útil quando o tipo da notificação é descoberto em runtime.
    /// O objeto deve implementar <see cref="INotification"/>; caso contrário,
    /// uma <see cref="ArgumentException"/> é lançada.
    /// </summary>
    Task Publish(object notification, CancellationToken cancellationToken = default);
}
