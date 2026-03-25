using Proxos.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Proxos.Tests;

public class SendTests
{
    // --- Requests e Handlers de teste ---
    public record PingRequest(string Message) : IRequest<string>;
    public record VoidRequest(string Data) : IRequest;

    public class PingHandler : IRequestHandler<PingRequest, string>
    {
        public Task<string> Handle(PingRequest request, CancellationToken cancellationToken)
            => Task.FromResult($"Pong: {request.Message}");
    }

    public class VoidHandler : IRequestHandler<VoidRequest>
    {
        public static bool WasCalled { get; private set; }
        public Task<Unit> Handle(VoidRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Unit.Task;
        }
    }

    [Fact]
    public async Task Send_WithTypedResponse_ReturnsExpectedResult()
    {
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SendTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new PingRequest("Hello"));

        Assert.Equal("Pong: Hello", result);
    }

    [Fact]
    public async Task Send_VoidRequest_ExecutesHandler()
    {
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SendTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(new VoidRequest("test"));

        Assert.True(VoidHandler.WasCalled);
    }

    [Fact]
    public async Task Send_NullRequest_ThrowsArgumentNullException()
    {
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SendTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            mediator.Send((IRequest<string>)null!));
    }

    [Fact]
    public async Task Send_UnregisteredRequest_ThrowsInvalidOperationException()
    {
        // Registry sem nenhum handler
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg => { });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new PingRequest("x")));
    }

    [Fact]
    public async Task Send_WithCancellation_ThrowsOperationCanceledException()
    {
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SendTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            mediator.Send(new PingRequest("x"), cts.Token));
    }
}
