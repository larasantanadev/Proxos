using BenchmarkDotNet.Attributes;
using Proxos.Benchmarks.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Proxos.Benchmarks;

/// <summary>
/// Compara o dispatch de Send() entre Proxos e MediatR 12.x.
/// Execute com: dotnet run -c Release
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class SendBenchmark
{
    private IServiceProvider _proxosProvider = null!;
    private IServiceProvider _mediatRProvider = null!;

    private static readonly ProxosPing ProxosRequest = new("hello");
    private static readonly MediatRPing MediatRRequest = new("hello");

    [GlobalSetup]
    public async Task Setup()
    {
        // ── Proxos ───────────────────────────────────────────────────
        var proxosServices = new ServiceCollection();
        proxosServices.AddLogging();
        proxosServices.AddProxos(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SendBenchmark).Assembly));

        _proxosProvider = proxosServices.BuildServiceProvider();

        // Congela o FrozenDictionary (warmup do WarmupService)
        foreach (var svc in _proxosProvider.GetServices<IHostedService>())
            await svc.StartAsync(CancellationToken.None);

        // ── MediatR 12.x ─────────────────────────────────────────────────────
        var mediatRServices = new ServiceCollection();
        mediatRServices.AddLogging();
        mediatRServices.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SendBenchmark).Assembly));

        _mediatRProvider = mediatRServices.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_proxosProvider as IDisposable)?.Dispose();
        (_mediatRProvider as IDisposable)?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "MediatR")]
    public async Task<string> MediatR_Send()
    {
        using var scope = _mediatRProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>();
        return (string)(await mediator.Send((object)MediatRRequest))!;
    }

    [Benchmark(Description = "Proxos")]
    public async Task<string> Proxos_Send()
    {
        using var scope = _proxosProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<global::Proxos.IMediator>();
        return await mediator.Send(ProxosRequest);
    }
}
