using Proxos.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Proxos.Tests;

public class ConditionalBehaviorTests
{
    public record ConditionalRequest(bool ShouldValidate, int Value) : IRequest<int>;

    public class ConditionalHandler : IRequestHandler<ConditionalRequest, int>
    {
        public Task<int> Handle(ConditionalRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);
    }

    public class ValidationBehavior : IConditionalBehavior<ConditionalRequest, int>
    {
        public static int ExecutionCount { get; set; }

        public bool ShouldHandle(ConditionalRequest request) => request.ShouldValidate;

        public async Task<int> Handle(ConditionalRequest request, RequestHandlerDelegate<int> next, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            if (request.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(request.Value), "Valor não pode ser negativo.");
            return await next();
        }
    }

    [Fact]
    public async Task ConditionalBehavior_WhenShouldHandleTrue_Executes()
    {
        ValidationBehavior.ExecutionCount = 0;

        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ConditionalBehaviorTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new ConditionalRequest(ShouldValidate: true, Value: 42));

        Assert.Equal(42, result);
        Assert.Equal(1, ValidationBehavior.ExecutionCount);
    }

    [Fact]
    public async Task ConditionalBehavior_WhenShouldHandleFalse_IsSkipped()
    {
        ValidationBehavior.ExecutionCount = 0;

        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ConditionalBehaviorTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // ShouldValidate = false → behavior é pulado mesmo com valor negativo
        var result = await mediator.Send(new ConditionalRequest(ShouldValidate: false, Value: -1));

        Assert.Equal(-1, result);
        Assert.Equal(0, ValidationBehavior.ExecutionCount);
    }

    [Fact]
    public async Task ConditionalBehavior_WhenShouldHandleTrue_CanThrow()
    {
        var sp = await ServiceProviderBuilder.BuildWithProxosAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ConditionalBehaviorTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            mediator.Send(new ConditionalRequest(ShouldValidate: true, Value: -1)));
    }
}
