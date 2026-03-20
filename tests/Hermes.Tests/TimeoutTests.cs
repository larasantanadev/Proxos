using HermesMediator.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace HermesMediator.Tests;

public class TimeoutTests
{
    [RequestTimeout(50)] // 50ms
    public record SlowRequest : IRequest<string>;

    public class SlowHandler : IRequestHandler<SlowRequest, string>
    {
        public async Task<string> Handle(SlowRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(5000, cancellationToken); // Vai expirar
            return "never";
        }
    }

    public record FastRequest : IRequest<string>;

    public class FastHandler : IRequestHandler<FastRequest, string>
    {
        public Task<string> Handle(FastRequest request, CancellationToken cancellationToken)
            => Task.FromResult("fast");
    }

    [Fact]
    public async Task RequestTimeout_WhenExceeded_ThrowsTimeoutException()
    {
        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TimeoutTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            mediator.Send(new SlowRequest()));
    }

    [Fact]
    public async Task RequestTimeout_WhenNotExceeded_ReturnsNormally()
    {
        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TimeoutTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new FastRequest());

        Assert.Equal("fast", result);
    }

    [Fact]
    public void RequestTimeoutAttribute_WithZeroMs_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestTimeoutAttribute(0));
    }

    [Fact]
    public void RequestTimeoutAttribute_WithNegativeMs_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestTimeoutAttribute(-100));
    }
}
