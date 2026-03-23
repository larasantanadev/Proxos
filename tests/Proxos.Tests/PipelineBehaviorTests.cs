using Proxos.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Proxos.Tests;

public class PipelineBehaviorTests
{
    public record TracedRequest(string Value) : IRequest<string>;

    public class TracedHandler : IRequestHandler<TracedRequest, string>
    {
        public Task<string> Handle(TracedRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);
    }

    public class FirstBehavior : IPipelineBehavior<TracedRequest, string>
    {
        public static List<string> Log { get; } = [];

        public async Task<string> Handle(TracedRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            Log.Add("First:Before");
            var result = await next();
            Log.Add("First:After");
            return result;
        }
    }

    public class SecondBehavior : IPipelineBehavior<TracedRequest, string>
    {
        public static List<string> Log { get; } = [];

        public async Task<string> Handle(TracedRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            Log.Add("Second:Before");
            var result = await next();
            Log.Add("Second:After");
            return result;
        }
    }

    [Fact]
    public async Task Behaviors_ExecuteInRegistrationOrder()
    {
        FirstBehavior.Log.Clear();
        SecondBehavior.Log.Clear();

        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PipelineBehaviorTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(new TracedRequest("test"));

        // FirstBehavior executa antes e depois (wrapping externo)
        Assert.Equal(["First:Before", "First:After"], FirstBehavior.Log);
        // SecondBehavior executa por dentro do First
        Assert.Equal(["Second:Before", "Second:After"], SecondBehavior.Log);
    }

    [Fact]
    public async Task Behaviors_CanModifyResponse()
    {
        var sp = await ServiceProviderBuilder.BuildAsync(services =>
        {
            services.AddProxos(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineBehaviorTests).Assembly));
            // Adiciona behavior que transforma a resposta
            services.AddScoped<IPipelineBehavior<TracedRequest, string>, UpperCaseBehavior>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TracedRequest("hello"));

        Assert.Equal("HELLO", result);
    }

    private class UpperCaseBehavior : IPipelineBehavior<TracedRequest, string>
    {
        public async Task<string> Handle(TracedRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            var result = await next();
            return result.ToUpperInvariant();
        }
    }
}
