using BenchmarkDotNet.Attributes;
using Proxos.Benchmarks.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Proxos.Benchmarks;

/// <summary>
/// Compara o dispatch de Publish() entre Proxos e MediatR 12.x.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class PublishBenchmark
{
    private IServiceProvider _proxosProvider = null!;
    private IServiceProvider _mediatRProvider = null!;

    private static readonly ProxosEvent ProxosNotification = new("data");
    private static readonly MediatREvent MediatRNotification = new("data");

    [GlobalSetup]
    public async Task Setup()
    {
        var proxosServices = new ServiceCollection();
        proxosServices.AddLogging();
        proxosServices.AddProxos(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PublishBenchmark).Assembly));

        _proxosProvider = proxosServices.BuildServiceProvider();

        foreach (var svc in _proxosProvider.GetServices<IHostedService>())
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
        (_proxosProvider as IDisposable)?.Dispose();
        (_mediatRProvider as IDisposable)?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "MediatR")]
    public async Task MediatR_Publish()
    {
        using var scope = _mediatRProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>();
        await mediator.Publish(MediatRNotification);
    }

    [Benchmark(Description = "Proxos")]
    public async Task Proxos_Publish()
    {
        using var scope = _proxosProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<global::Proxos.IMediator>();
        await mediator.Publish(ProxosNotification);
    }
}
