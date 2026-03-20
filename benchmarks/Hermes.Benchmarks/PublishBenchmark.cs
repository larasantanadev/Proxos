using BenchmarkDotNet.Attributes;
using Hermes.Benchmarks.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hermes.Benchmarks;

/// <summary>
/// Compara o dispatch de Publish() entre HermesMediator e MediatR 12.x.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class PublishBenchmark
{
    private IServiceProvider _hermesProvider = null!;
    private IServiceProvider _mediatRProvider = null!;

    private static readonly HermesEvent HermesNotification = new("data");
    private static readonly MediatREvent MediatRNotification = new("data");

    [GlobalSetup]
    public async Task Setup()
    {
        var hermesServices = new ServiceCollection();
        hermesServices.AddLogging();
        hermesServices.AddHermesMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PublishBenchmark).Assembly));

        _hermesProvider = hermesServices.BuildServiceProvider();

        foreach (var svc in _hermesProvider.GetServices<IHostedService>())
            await svc.StartAsync(CancellationToken.None);

        var mediatRServices = new ServiceCollection();
        mediatRServices.AddLogging();
        mediatRServices.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PublishBenchmark).Assembly));

        _mediatRProvider = mediatRServices.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_hermesProvider as IDisposable)?.Dispose();
        (_mediatRProvider as IDisposable)?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "MediatR")]
    public async Task MediatR_Publish()
    {
        using var scope = _mediatRProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>();
        await mediator.Publish(MediatRNotification);
    }

    [Benchmark(Description = "HermesMediator")]
    public async Task Hermes_Publish()
    {
        using var scope = _hermesProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<global::HermesMediator.IMediator>();
        await mediator.Publish(HermesNotification);
    }
}
