using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Proxos.Tests.Helpers;

internal static class ServiceProviderBuilder
{
    /// <summary>
    /// Cria um ServiceProvider com Proxos configurado, sem precisar de IHost completo.
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

    internal static async Task<IServiceProvider> BuildWithProxosAsync(
        Action<global::Proxos.Configuration.ProxosConfiguration> proxosConfig,
        Action<IServiceCollection>? extraServices = null)
    {
        return await BuildAsync(services =>
        {
            services.AddProxos(proxosConfig);
            extraServices?.Invoke(services);
        });
    }
}
