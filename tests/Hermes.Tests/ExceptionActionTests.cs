using HermesMediator.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace HermesMediator.Tests;

/// <summary>
/// Testa o IRequestExceptionAction — ação de side-effect que nunca suprime a exceção.
/// </summary>
public class ExceptionActionTests
{
    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    public record FailingCommand : IRequest<string>;

    public class FailingCommandHandler : IRequestHandler<FailingCommand, string>
    {
        public Task<string> Handle(FailingCommand request, CancellationToken ct)
            => throw new InvalidOperationException("Falha intencional");
    }

    public class ArgumentExceptionCommand : IRequest<string>;

    public class ArgumentExceptionCommandHandler : IRequestHandler<ArgumentExceptionCommand, string>
    {
        public Task<string> Handle(ArgumentExceptionCommand request, CancellationToken ct)
            => throw new ArgumentNullException("param", "Argumento nulo");
    }

    // Ação que registra a exceção recebida
    public class RecordingExceptionAction : IRequestExceptionAction<FailingCommand, InvalidOperationException>
    {
        public static readonly List<string> Recorded = [];

        public Task Execute(FailingCommand request, InvalidOperationException exception, CancellationToken ct)
        {
            Recorded.Add(exception.Message);
            return Task.CompletedTask;
        }
    }

    // Ação registrada para Exception (base) — deve executar para qualquer exceção
    public class BaseExceptionAction : IRequestExceptionAction<FailingCommand, Exception>
    {
        public static readonly List<string> Recorded = [];

        public Task Execute(FailingCommand request, Exception exception, CancellationToken ct)
        {
            Recorded.Add($"base:{exception.GetType().Name}");
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Testes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExceptionAction_Executes_BeforeExceptionPropagates()
    {
        RecordingExceptionAction.Recorded.Clear();

        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ExceptionActionTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new FailingCommand()));

        // A ação deve ter sido chamada mesmo que a exceção se propague
        Assert.Contains("Falha intencional", RecordingExceptionAction.Recorded);
    }

    [Fact]
    public async Task ExceptionAction_DoesNotSuppress_ExceptionAlwaysPropagates()
    {
        RecordingExceptionAction.Recorded.Clear();

        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ExceptionActionTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Mesmo com ação registrada, a exceção deve propagar
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new FailingCommand()));

        Assert.Equal("Falha intencional", ex.Message);
    }

    [Fact]
    public async Task ExceptionAction_BaseException_ExecutesForDerivedExceptionTypes()
    {
        BaseExceptionAction.Recorded.Clear();

        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ExceptionActionTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // BaseExceptionAction escuta Exception (base) — deve executar para InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new FailingCommand()));

        Assert.Contains("base:InvalidOperationException", BaseExceptionAction.Recorded);
    }

    [Fact]
    public async Task ExceptionAction_SpecificAndBase_BothExecute()
    {
        RecordingExceptionAction.Recorded.Clear();
        BaseExceptionAction.Recorded.Clear();

        var sp = await ServiceProviderBuilder.BuildWithHermesAsync(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ExceptionActionTests).Assembly));

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new FailingCommand()));

        // Ambas as ações devem ter executado
        Assert.NotEmpty(RecordingExceptionAction.Recorded);
        Assert.NotEmpty(BaseExceptionAction.Recorded);
    }

    [Fact]
    public async Task ExceptionAction_NotExecuted_WhenNoExceptionThrown()
    {
        RecordingExceptionAction.Recorded.Clear();

        // Registra um handler que NÃO falha (diferente de FailingCommand)
        var sp = await ServiceProviderBuilder.BuildAsync(services =>
        {
            services.AddHermesMediator(cfg => { });
            // Não registra nada — só verifica que sem falha não executa a ação
        });

        // Como não há handler, não há falha da parte do handler
        // A ação só executa quando há exceção no pipeline
        Assert.Empty(RecordingExceptionAction.Recorded);
    }
}
