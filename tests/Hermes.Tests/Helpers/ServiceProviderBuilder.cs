using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HermesMediator.Tests.Helpers;

internal static class ServiceProviderBuilder
{
    /// <summary>
    /// Cria um ServiceProvider com Hermes configurado, sem precisar de IHost completo.
    /// O WarmupService é chamado manualmente para simular o startup.
    /// </summary>
    internal static async Task<IServiceProvider> BuildAsync(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();

        // Executa todos os IHostedService (WarmupService) manualmente
        var hostedServices = provider.GetServices<IHostedService>();
        foreach (var svc in hostedServices)
            await svc.StartAsync(CancellationToken.None);

        return provider;
    }

    internal static async Task<IServiceProvider> BuildWithHermesAsync(
        Action<global::HermesMediator.Configuration.HermesConfiguration> hermesConfig,
        Action<IServiceCollection>? extraServices = null)
    {
        return await BuildAsync(services =>
        {
            services.AddHermesMediator(hermesConfig);
            extraServices?.Invoke(services);
        });
    }
}
