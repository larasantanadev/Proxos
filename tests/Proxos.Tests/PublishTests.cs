using Proxos.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Proxos.Tests;

public class PublishTests
{
    public record OrderCreated(Guid OrderId) : INotification;
    public record OrphanNotification : INotification;

    public class AuditHandler : INotificationHandler<OrderCreated>
    {
        public static List<Guid> Received { get; } = [];
        public Task Handle(OrderCreated notification, CancellationToken cancellationToken)
        {
            Received.Add(notification.OrderId);
            return Task.CompletedTask;
        }
    }

    public class EmailHandler : INotificationHandler<OrderCreated>
    {
        public static List<Guid> Received { get; } = [];
        public Task Handle(OrderCreated notification, CancellationToken cancellationToken)
        {
            Received.Add(notification.OrderId);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Publish_MultipleHandlers_AllReceiveNotification()
    {
        AuditHandler.Received.Clear();
        EmailHandler.Received.Clear();

        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PublishTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var id = Guid.NewGuid();
        await mediator.Publish(new OrderCreated(id));

        Assert.Contains(id, AuditHandler.Received);
        Assert.Contains(id, EmailHandler.Received);
    }

    [Fact]
    public async Task Publish_NoHandlers_CompletesWithoutError()
    {
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PublishTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // OrphanNotification não tem handlers — não deve lançar exceção
        await mediator.Publish(new OrphanNotification());
    }

    [Fact]
    public async Task Publish_WhenAllStrategy_ExecutesInParallel()
    {
        AuditHandler.Received.Clear();
        EmailHandler.Received.Clear();

        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg => cfg
            .RegisterServicesFromAssembly(typeof(PublishTests).Assembly)
            .DefaultPublishStrategy = Configuration.PublishStrategy.WhenAll);

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var id = Guid.NewGuid();
        await mediator.Publish(new OrderCreated(id));

        Assert.Contains(id, AuditHandler.Received);
        Assert.Contains(id, EmailHandler.Received);
    }

    [Fact]
    public async Task Publish_NullNotification_ThrowsArgumentNullException()
    {
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PublishTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            mediator.Publish((INotification)null!));
    }
}
