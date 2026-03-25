using System.Diagnostics;
using Proxos.Configuration;
using Proxos.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Proxos.Internal;

/// <summary>Wrapper abstrato para publicação de notificações.</summary>
internal abstract class NotificationHandlerWrapperBase
{
    internal abstract Task Publish(
        object notification,
        PublishStrategy strategy,
        INotificationPublisher? publisher,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}

/// <summary>Wrapper tipado para publicação de notificações.</summary>
internal sealed class NotificationHandlerWrapper<TNotification> : NotificationHandlerWrapperBase
    where TNotification : INotification
{
    private static readonly string NotificationName = typeof(TNotification).Name;

    internal override async Task Publish(
        object notification,
        PublishStrategy strategy,
        INotificationPublisher? publisher,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var typedNotification = (TNotification)notification;
        var handlers = serviceProvider
            .GetServices<INotificationHandler<TNotification>>()
            .ToArray();

        if (handlers.Length == 0)
            return;

        bool otelActive = ProxosDiagnostics.ActivitySource.HasListeners()
                       || ProxosDiagnostics.NotificationsTotal.Enabled;

        var tag = otelActive
            ? new KeyValuePair<string, object?>("notification.type", NotificationName)
            : default;

        if (otelActive)
            ProxosDiagnostics.NotificationsTotal.Add(1, [tag]);

        using var activity = otelActive
            ? ProxosDiagnostics.StartPublishActivity(NotificationName)
            : null;

        try
        {
            if (publisher is not null)
            {
                var executors = handlers.Select(h =>
                    new NotificationHandlerExecutor(h,
                        (n, ct) => h.Handle((TNotification)n, ct)));
                await publisher.Publish(executors, typedNotification, cancellationToken);
            }
            else
            {
                await (strategy switch
                {
                    PublishStrategy.WhenAll => PublishWhenAll(typedNotification, handlers, cancellationToken),
                    PublishStrategy.WhenAllContinueOnError => PublishWhenAllContinueOnError(typedNotification, handlers, cancellationToken),
                    _ => PublishForeachAwait(typedNotification, handlers, cancellationToken),
                });
            }
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private static async Task PublishForeachAwait(
        TNotification notification,
        INotificationHandler<TNotification>[] handlers,
        CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
            await handler.Handle(notification, cancellationToken);
    }

    private static Task PublishWhenAll(
        TNotification notification,
        INotificationHandler<TNotification>[] handlers,
        CancellationToken cancellationToken)
    {
        return Task.WhenAll(handlers.Select(h => h.Handle(notification, cancellationToken)));
    }

    private static async Task PublishWhenAllContinueOnError(
        TNotification notification,
        INotificationHandler<TNotification>[] handlers,
        CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(
            handlers.Select(h => h.Handle(notification, cancellationToken)
                .ContinueWith(t => t.Exception, TaskContinuationOptions.None)));

        var exceptions = results
            .OfType<AggregateException>()
            .SelectMany(e => e.InnerExceptions)
            .ToList();

        if (exceptions.Count > 0)
            throw new AggregateException("Um ou mais handlers falharam.", exceptions);
    }
}
