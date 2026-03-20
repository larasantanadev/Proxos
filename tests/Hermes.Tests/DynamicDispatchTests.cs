using HermesMediator.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace HermesMediator.Tests;

/// <summary>
/// Testa dispatch dinâmico: Send(object), CreateStream(object), Publish(object).
/// Útil para cenários onde o tipo do request é descoberto em runtime.
/// </summary>
public class DynamicDispatchTests
{
    // -------------------------------------------------------------------------
    // Fixtures compartilhadas
    // -------------------------------------------------------------------------

    public record DynamicQuery(string Value) : IRequest<string>;

    public class DynamicQueryHandler : IRequestHandler<DynamicQuery, string>
    {
        public Task<string> Handle(DynamicQuery request, CancellationToken ct)
            => Task.FromResult($"result:{request.Value}");
    }

    public record DynamicCommand : IRequest;

    public class DynamicCommandHandler : IRequestHandler<DynamicCommand>
    {
        public static bool WasCalled;
        public Task<Unit> Handle(DynamicCommand request, CancellationToken ct)
        {
            WasCalled = true;
            return Unit.Task;
        }
    }

    public record DynamicStreamQuery(int Count) : IStreamRequest<int>;

    public class DynamicStreamQueryHandler : IStreamRequestHandler<DynamicStreamQuery, int>
    {
        public async IAsyncEnumerable<int> Handle(DynamicStreamQuery request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < request.Count; i++)
                yield return i;
        }
    }

    public record DynamicEvent : INotification;

    public class DynamicEventHandler : INotificationHandler<DynamicEvent>
    {
        public static bool WasCalled;
        public Task Handle(DynamicEvent notification, CancellationToken ct)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Send(object) — request com retorno
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Send_Object_ReturnsResult()
    {
        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DynamicDispatchTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        object request = new DynamicQuery("hello");
        var result = await mediator.Send(request);

        Assert.Equal("result:hello", result);
    }

    [Fact]
    public async Task Send_Object_VoidRequest_ExecutesHandler()
    {
        DynamicCommandHandler.WasCalled = false;

        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DynamicDispatchTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        object request = new DynamicCommand();
        var result = await mediator.Send(request);

        Assert.True(DynamicCommandHandler.WasCalled);
        Assert.IsType<Unit>(result);
    }

    [Fact]
    public async Task Send_Object_Null_ThrowsArgumentNullException()
    {
        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DynamicDispatchTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Send((object)null!));
    }

    // -------------------------------------------------------------------------
    // CreateStream(object) — dispatch dinâmico
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateStream_Object_YieldsItems()
    {
        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DynamicDispatchTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        object request = new DynamicStreamQuery(Count: 3);
        var items = new List<object?>();

        await foreach (var item in mediator.CreateStream(request))
            items.Add(item);

        Assert.Equal([0, 1, 2], items.Cast<int>());
    }

    [Fact]
    public async Task CreateStream_Object_Null_ThrowsArgumentNullException()
    {
        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DynamicDispatchTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        Assert.Throws<ArgumentNullException>(() => { mediator.CreateStream((object)null!); });
    }

    // -------------------------------------------------------------------------
    // Publish(object) — dispatch dinâmico
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Publish_Object_ExecutesHandlers()
    {
        DynamicEventHandler.WasCalled = false;

        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DynamicDispatchTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        object notification = new DynamicEvent();
        await mediator.Publish(notification);

        Assert.True(DynamicEventHandler.WasCalled);
    }

    [Fact]
    public async Task Publish_Object_NotINotification_ThrowsArgumentException()
    {
        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DynamicDispatchTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Publish(new object()));
    }

    [Fact]
    public async Task Publish_Object_Null_ThrowsArgumentNullException()
    {
        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DynamicDispatchTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Publish((object)null!));
    }
}
