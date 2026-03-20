using HermesMediator.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace HermesMediator.Tests;

public class IgnoreBehaviorTests
{
    public record NormalRequest(string Value) : IRequest<string>;

    [IgnoreBehavior(typeof(AuditBehavior))]
    public record IgnoredAuditRequest(string Value) : IRequest<string>;

    public class NormalHandler : IRequestHandler<NormalRequest, string>
    {
        public Task<string> Handle(NormalRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);
    }

    public class IgnoredAuditHandler : IRequestHandler<IgnoredAuditRequest, string>
    {
        public Task<string> Handle(IgnoredAuditRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);
    }

    public class AuditBehavior : IPipelineBehavior<NormalRequest, string>,
                                 IPipelineBehavior<IgnoredAuditRequest, string>
    {
        public static int ExecutionCount { get; set; }

        Task<string> IPipelineBehavior<NormalRequest, string>.Handle(
            NormalRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return next();
        }

        Task<string> IPipelineBehavior<IgnoredAuditRequest, string>.Handle(
            IgnoredAuditRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return next();
        }
    }

    [Fact]
    public async Task IgnoreBehavior_WhenApplied_SkipsBehaviorForThatRequest()
    {
        AuditBehavior.ExecutionCount = 0;

        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(IgnoreBehaviorTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // NormalRequest: AuditBehavior DEVE executar
        await mediator.Send(new NormalRequest("test"));
        var countAfterNormal = AuditBehavior.ExecutionCount;

        // IgnoredAuditRequest: AuditBehavior NÃO deve executar
        await mediator.Send(new IgnoredAuditRequest("test"));
        var countAfterIgnored = AuditBehavior.ExecutionCount;

        Assert.Equal(countAfterNormal, countAfterIgnored); // Não incrementou
    }
}
