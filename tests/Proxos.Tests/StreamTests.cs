using Proxos.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Proxos.Tests;

public class StreamTests
{
    public record CountRequest(int Count) : IStreamRequest<int>;

    public class CountHandler : IStreamRequestHandler<CountRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(CountRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (int i = 1; i <= request.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return i;
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task CreateStream_ReturnsAllItems()
    {
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(StreamTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var results = new List<int>();
        await foreach (var item in mediator.CreateStream(new CountRequest(5)))
            results.Add(item);

        Assert.Equal([1, 2, 3, 4, 5], results);
    }

    [Fact]
    public async Task CreateStream_WithCancellation_StopsEarly()
    {
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(StreamTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var cts = new CancellationTokenSource();
        var results = new List<int>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in mediator.CreateStream(new CountRequest(100), cts.Token))
            {
                results.Add(item);
                if (results.Count >= 3)
                    await cts.CancelAsync();
            }
        });

        Assert.True(results.Count < 100);
    }

    [Fact]
    public async Task CreateStream_NullRequest_ThrowsArgumentNullException()
    {
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(StreamTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        Assert.Throws<ArgumentNullException>(() =>
            mediator.CreateStream((IStreamRequest<int>)null!));
    }
}
