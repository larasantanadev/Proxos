using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Proxos;

/// <summary>
/// API de infraestrutura usada pelo código gerado pelo Proxos.Generator.
/// Não chame diretamente — use <c>AddProxos()</c> ou <c>AddProxosGenerated()</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ProxosRegistrar
{
    /// <summary>
    /// Obtém o <c>WrapperRegistry</c> singleton registrado pelo <c>AddProxos()</c>.
    /// Chamado pelo código gerado — não use diretamente.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static object? GetRegistry(IServiceCollection services)
    {
        return services
            .FirstOrDefault(d => d.ServiceType == typeof(Internal.WrapperRegistry))
            ?.ImplementationInstance;
    }

    /// <summary>Registra um wrapper de request no registry. Chamado pelo código gerado.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterRequest<TRequest, TResponse>(object registry)
        where TRequest : notnull, IRequest<TResponse>
    {
        ((Internal.WrapperRegistry)registry).RegisterRequest<TRequest, TResponse>();
    }

    /// <summary>Registra um wrapper de notificação no registry. Chamado pelo código gerado.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterNotification<TNotification>(object registry)
        where TNotification : INotification
    {
        ((Internal.WrapperRegistry)registry).RegisterNotification<TNotification>();
    }

    /// <summary>Registra um wrapper de stream no registry. Chamado pelo código gerado.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterStream<TRequest, TResponse>(object registry)
        where TRequest : notnull, IStreamRequest<TResponse>
    {
        ((Internal.WrapperRegistry)registry).RegisterStream<TRequest, TResponse>();
    }
}
