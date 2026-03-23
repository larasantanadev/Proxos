using Microsoft.Extensions.DependencyInjection;

namespace Proxos.Tests;

/// <summary>
/// Testa que o Proxos funciona sem IHost (ex: app console).
/// O WrapperRegistry deve se auto-congelar no primeiro acesso (lazy freeze).
/// </summary>
public class LazyFreezeTests
{
    public record ConsoleQuery(string Value) : IRequest<string>;

    public class ConsoleQueryHandler : IRequestHandler<ConsoleQuery, string>
    {
        public Task<string> Handle(ConsoleQuery request, CancellationToken ct)
            => Task.FromResult($"console:{request.Value}");
    }

    [Fact]
    public async Task WithoutIHost_LazyFreeze_MediatorWorksCorrectly()
    {
        // Simula app console: sem IHost, sem WarmupService.StartAsync()
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProxos(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(LazyFreezeTests).Assembly));

        // NÃO chama StartAsync — simula app console que só usa ServiceProvider diretamente
        var provider = services.BuildServiceProvider();

        // Deve funcionar sem IHost — lazy freeze no primeiro acesso
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new ConsoleQuery("test"));

        Assert.Equal("console:test", result);
    }

    [Fact]
    public async Task WithoutIHost_MultipleCallsAreThreadSafe()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProxos(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(LazyFreezeTests).Assembly));

        var provider = services.BuildServiceProvider();

        // Múltiplas chamadas concorrentes sem warmup prévio
        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            using var scope = provider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            return await mediator.Send(new ConsoleQuery(i.ToString()));
        });

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.StartsWith("console:", r));
    }
}
