using Proxos.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Proxos.Tests;

public class PrePostProcessorTests
{
    public record ProcessedRequest(string Input) : IRequest<string>;

    public class ProcessedHandler : IRequestHandler<ProcessedRequest, string>
    {
        public Task<string> Handle(ProcessedRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Input);
    }

    public class LoggingPreProcessor : IRequestPreProcessor<ProcessedRequest>
    {
        public static List<string> Log { get; } = [];
        public Task Process(ProcessedRequest request, CancellationToken cancellationToken)
        {
            Log.Add($"Pre:{request.Input}");
            return Task.CompletedTask;
        }
    }

    public class LoggingPostProcessor : IRequestPostProcessor<ProcessedRequest, string>
    {
        public static List<string> Log { get; } = [];
        public Task Process(ProcessedRequest request, string response, CancellationToken cancellationToken)
        {
            Log.Add($"Post:{response}");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PreProcessor_ExecutesBeforeHandler()
    {
        LoggingPreProcessor.Log.Clear();

        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PrePostProcessorTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(new ProcessedRequest("hello"));

        Assert.Single(LoggingPreProcessor.Log);
        Assert.Equal("Pre:hello", LoggingPreProcessor.Log[0]);
    }

    [Fact]
    public async Task PostProcessor_ReceivesRequestAndResponse()
    {
        LoggingPostProcessor.Log.Clear();

        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PrePostProcessorTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(new ProcessedRequest("world"));

        Assert.Single(LoggingPostProcessor.Log);
        Assert.Equal("Post:world", LoggingPostProcessor.Log[0]);
    }
}
