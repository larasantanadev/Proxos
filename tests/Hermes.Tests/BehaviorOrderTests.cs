using HermesMediator.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace HermesMediator.Tests;

/// <summary>
/// Testa que [BehaviorOrder] é respeitado independentemente da ordem de registro no DI.
/// </summary>
public class BehaviorOrderTests
{
    public record OrderedQuery : IRequest<List<string>>;

    public class OrderedQueryHandler : IRequestHandler<OrderedQuery, List<string>>
    {
        public Task<List<string>> Handle(OrderedQuery request, CancellationToken ct)
            => Task.FromResult(new List<string> { "handler" });
    }

    // Executa PRIMEIRO (order -10) — even though registered last
    [BehaviorOrder(-10)]
    public class FirstBehavior : IPipelineBehavior<OrderedQuery, List<string>>
    {
        public async Task<List<string>> Handle(OrderedQuery request, RequestHandlerDelegate<List<string>> next, CancellationToken ct)
        {
            var result = await next();
            result.Insert(0, "first");
            return result;
        }
    }

    // Executa SEGUNDO (order 0, padrão)
    public class MiddleBehavior : IPipelineBehavior<OrderedQuery, List<string>>
    {
        public async Task<List<string>> Handle(OrderedQuery request, RequestHandlerDelegate<List<string>> next, CancellationToken ct)
        {
            var result = await next();
            result.Insert(0, "middle");
            return result;
        }
    }

    // Executa TERCEIRO (order 50) — mais próximo do handler
    [BehaviorOrder(50)]
    public class LastBehavior : IPipelineBehavior<OrderedQuery, List<string>>
    {
        public async Task<List<string>> Handle(OrderedQuery request, RequestHandlerDelegate<List<string>> next, CancellationToken ct)
        {
            var result = await next();
            result.Insert(0, "last");
            return result;
        }
    }

    [Fact]
    public async Task BehaviorOrder_RespectsAttributeOrder_IndependentOfRegistration()
    {
        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(BehaviorOrderTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new OrderedQuery());

        // Pipeline executa: First (mais externo) → Middle → Last → Handler → Last retorna → Middle retorna → First retorna
        // Então a lista final, da perspectiva da inserção no resultado, fica:
        // handler → last insere no início → middle insere no início → first insere no início
        // Resultado: ["first", "middle", "last", "handler"]
        Assert.Equal(["first", "middle", "last", "handler"], result);
    }

    [Fact]
    public async Task BehaviorOrder_WithoutAttribute_DefaultsToZero()
    {
        // MiddleBehavior não tem atributo → order 0 = mesmo nível que padrão
        // FirstBehavior tem order -10 → executa antes (mais externo)
        // LastBehavior tem order 50 → executa depois (mais interno)
        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(BehaviorOrderTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new OrderedQuery());

        // First deve sempre estar antes de Middle que deve estar antes de Last
        var firstIdx  = result.IndexOf("first");
        var middleIdx = result.IndexOf("middle");
        var lastIdx   = result.IndexOf("last");

        Assert.True(firstIdx < middleIdx, "first deve aparecer antes de middle");
        Assert.True(middleIdx < lastIdx,  "middle deve aparecer antes de last");
    }
}
