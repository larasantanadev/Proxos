using Proxos.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Proxos.Tests;

/// <summary>
/// Testa edge cases do Source Generator e do assembly scanning:
/// handlers em classes aninhadas e múltiplos assemblies.
/// </summary>
public class NestedHandlerTests
{
    // ── Handlers em classes aninhadas (public nested types) ──────────────────

    public record NestedRequest(int Value) : IRequest<int>;
    public record NestedNotification(string Message) : INotification;

    /// <summary>Handler público aninhado dentro de outra classe.</summary>
    public class OuterContainer
    {
        public class NestedRequestHandler : IRequestHandler<NestedRequest, int>
        {
            public Task<int> Handle(NestedRequest request, CancellationToken ct)
                => Task.FromResult(request.Value * 2);
        }

        public class NestedNotificationHandler : INotificationHandler<NestedNotification>
        {
            public static string? LastMessage { get; set; }

            public Task Handle(NestedNotification notification, CancellationToken ct)
            {
                LastMessage = notification.Message;
                return Task.CompletedTask;
            }
        }
    }

    [Fact]
    public async Task Send_HandlerInNestedClass_IsDiscoveredAndExecuted()
    {
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(NestedHandlerTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new NestedRequest(21));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Publish_HandlerInNestedClass_IsDiscoveredAndExecuted()
    {
        OuterContainer.NestedNotificationHandler.LastMessage = null;

        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(NestedHandlerTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Publish(new NestedNotification("hello-nested"));

        Assert.Equal("hello-nested", OuterContainer.NestedNotificationHandler.LastMessage);
    }

    // ── Múltiplos assemblies registrados ────────────────────────────────────

    public record AssemblyARequest(string Tag) : IRequest<string>;

    public class AssemblyAHandler : IRequestHandler<AssemblyARequest, string>
    {
        public Task<string> Handle(AssemblyARequest request, CancellationToken ct)
            => Task.FromResult($"from-A:{request.Tag}");
    }

    [Fact]
    public async Task RegisterServicesFromAssemblies_MultipleAssemblies_AllHandlersResolved()
    {
        // Registra o mesmo assembly duas vezes — não deve duplicar handlers
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(NestedHandlerTests).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(NestedHandlerTests).Assembly);
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new AssemblyARequest("x"));

        Assert.Equal("from-A:x", result);
    }

    // ── TypeEvaluator — filtro de namespace ──────────────────────────────────

    public record FilteredRequest(string Data) : IRequest<string>;
    public class FilteredHandler : IRequestHandler<FilteredRequest, string>
    {
        public Task<string> Handle(FilteredRequest request, CancellationToken ct)
            => Task.FromResult($"filtered:{request.Data}");
    }

    [Fact]
    public async Task TypeEvaluator_ExcludesNamespace_HandlerNotRegistered()
    {
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(NestedHandlerTests).Assembly);
            // Exclui todos os tipos de NestedHandlerTests — só FilteredHandler seria excluído junto
            cfg.TypeEvaluator = t => t.DeclaringType != typeof(NestedHandlerTests);
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // FilteredHandler está em NestedHandlerTests, então deve ser excluído
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new FilteredRequest("test")));
    }
}
