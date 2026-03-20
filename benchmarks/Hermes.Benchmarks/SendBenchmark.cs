using BenchmarkDotNet.Attributes;
using Hermes.Benchmarks.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hermes.Benchmarks;

/// <summary>
/// Compara o dispatch de Send() entre HermesMediator e MediatR 12.x.
/// Execute com: dotnet run -c Release
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class SendBenchmark
{
    private IServiceProvider _hermesProvider = null!;
    private IServiceProvider _mediatRProvider = null!;

    private static readonly HermesPing HermesRequest = new("hello");
    private static readonly MediatRPing MediatRRequest = new("hello");

    [GlobalSetup]
    public async Task Setup()
    {
        // ── HermesMediator ───────────────────────────────────────────────────
        var hermesServices = new ServiceCollection();
        hermesServices.AddLogging();
        hermesServices.AddHermesMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SendBenchmark).Assembly));

        _hermesProvider = hermesServices.BuildServiceProvider();

        // Congela o FrozenDictionary (warmup do WarmupService)
        foreach (var svc in _hermesProvider.GetServices<IHostedService>())
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
        (_hermesProvider as IDisposable)?.Dispose();
        (_mediatRProvider as IDisposable)?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "MediatR")]
    public async Task<string> MediatR_Send()
    {
        using var scope = _mediatRProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>();
        return await mediator.Send(MediatRRequest);
    }

    [Benchmark(Description = "HermesMediator")]
    public async Task<string> Hermes_Send()
    {
        using var scope = _hermesProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<global::HermesMediator.IMediator>();
        return await mediator.Send(HermesRequest);
    }
}
