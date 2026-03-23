using System.Reflection;
using Proxos;
using Proxos.Configuration;
using Proxos.Context;
using Proxos.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extensões de registro do Proxos no container de DI.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra o Proxos no container de DI.
    /// </summary>
    /// <param name="services">O <see cref="IServiceCollection"/> do app.</param>
    /// <param name="configure">Configuração do Proxos (assemblies, behaviors, estratégia de publish).</param>
    /// <returns>O mesmo <see cref="IServiceCollection"/> para encadeamento.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddProxos(cfg => cfg
    ///     .RegisterServicesFromAssembly(typeof(Program).Assembly)
    ///     .AddOpenBehavior(typeof(LoggingBehavior&lt;,&gt;)));
    /// </code>
    /// </example>
    public static IServiceCollection AddProxos(
        this IServiceCollection services,
        Action<ProxosConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var config = new ProxosConfiguration();
        configure(config);

        // Singleton: configuração e registry
        services.AddSingleton(config);

        var registry = new WrapperRegistry();
        services.AddSingleton(registry);

        // Singleton: context accessor — AsyncLocal já isola por contexto assíncrono.
        // Usar singleton evita alocar uma nova instância por scope sem perda de isolamento.
        services.TryAddSingleton<IPipelineContextAccessor, PipelineContextAccessor>();
        services.TryAddScoped<IMediator>(sp => new Mediator(
            sp,
            sp.GetRequiredService<WrapperRegistry>(),
            sp.GetRequiredService<ProxosConfiguration>()));
        services.TryAddScoped<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.TryAddScoped<IPublisher>(sp => sp.GetRequiredService<IMediator>());

        // Warmup: congela o FrozenDictionary no startup
        services.AddHostedService<WarmupService>();

        // Registra handlers e behaviors dos assemblies informados
        foreach (var assembly in config.Assemblies)
            RegisterFromAssembly(services, assembly, registry, config);

        // Behaviors globais (open e closed, lifetime configurável)
        foreach (var descriptor in config.BehaviorsToRegister)
            services.Add(descriptor);

        // Stream behaviors globais
        foreach (var descriptor in config.StreamBehaviorsToRegister)
            services.Add(descriptor);

        // Pre-processors globais
        foreach (var descriptor in config.RequestPreProcessorsToRegister)
            services.Add(descriptor);

        // Post-processors globais
        foreach (var descriptor in config.RequestPostProcessorsToRegister)
            services.Add(descriptor);

        return services;
    }

    private static void RegisterFromAssembly(
        IServiceCollection services,
        Assembly assembly,
        WrapperRegistry registry,
        ProxosConfiguration config)
    {
        var exportedTypes = assembly.GetExportedTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                        && config.TypeEvaluator(t))
            .ToArray();

        foreach (var type in exportedTypes)
        {
            RegisterRequestHandlers(services, registry, type);
            RegisterNotificationHandlers(services, registry, type);
            RegisterStreamHandlers(services, registry, type);
            RegisterPreProcessors(services, type);
            RegisterPostProcessors(services, type);
            RegisterExceptionHandlers(services, type);
            RegisterExceptionActions(services, type);
            RegisterBehaviors(services, type);
        }
    }

    private static void RegisterRequestHandlers(
        IServiceCollection services,
        WrapperRegistry registry,
        Type type)
    {
        var handlerInterfaces = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));

        foreach (var iface in handlerInterfaces)
        {
            services.AddScoped(iface, type);

            var typeArgs = iface.GetGenericArguments();
            var requestType = typeArgs[0];
            var responseType = typeArgs[1];

            // Registra no registry via reflection (único lugar — só no startup)
            var method = typeof(WrapperRegistry)
                .GetMethod(nameof(WrapperRegistry.RegisterRequest),
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(requestType, responseType);

            method.Invoke(registry, null);

            // Se a resposta é Unit, registra também o alias IRequestHandler<TRequest>
            if (responseType == typeof(Unit))
            {
                var simpleInterface = typeof(IRequestHandler<>).MakeGenericType(requestType);
                if (type.GetInterfaces().Contains(simpleInterface))
                    services.TryAddScoped(simpleInterface, type);
            }
        }
    }

    private static void RegisterNotificationHandlers(
        IServiceCollection services,
        WrapperRegistry registry,
        Type type)
    {
        var handlerInterfaces = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>));

        foreach (var iface in handlerInterfaces)
        {
            services.AddScoped(iface, type);

            var notificationType = iface.GetGenericArguments()[0];

            var method = typeof(WrapperRegistry)
                .GetMethod(nameof(WrapperRegistry.RegisterNotification),
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(notificationType);

            method.Invoke(registry, null);
        }
    }

    private static void RegisterStreamHandlers(
        IServiceCollection services,
        WrapperRegistry registry,
        Type type)
    {
        var handlerInterfaces = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamRequestHandler<,>));

        foreach (var iface in handlerInterfaces)
        {
            services.AddScoped(iface, type);

            var typeArgs = iface.GetGenericArguments();
            var method = typeof(WrapperRegistry)
                .GetMethod(nameof(WrapperRegistry.RegisterStream),
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(typeArgs[0], typeArgs[1]);

            method.Invoke(registry, null);
        }
    }

    private static void RegisterPreProcessors(IServiceCollection services, Type type)
    {
        var ifaces = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestPreProcessor<>));

        foreach (var iface in ifaces)
            services.AddScoped(iface, type);
    }

    private static void RegisterPostProcessors(IServiceCollection services, Type type)
    {
        var ifaces = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestPostProcessor<,>));

        foreach (var iface in ifaces)
            services.AddScoped(iface, type);
    }

    private static void RegisterExceptionHandlers(IServiceCollection services, Type type)
    {
        var ifaces = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestExceptionHandler<,,>));

        foreach (var iface in ifaces)
            services.AddScoped(iface, type);
    }

    private static void RegisterExceptionActions(IServiceCollection services, Type type)
    {
        var ifaces = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestExceptionAction<,>));

        foreach (var iface in ifaces)
            services.AddScoped(iface, type);
    }

    private static void RegisterBehaviors(IServiceCollection services, Type type)
    {
        var ifaces = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

        foreach (var iface in ifaces)
            services.AddScoped(iface, type);
    }

    /// <inheritdoc cref="AddProxos"/>
    [Obsolete("Use AddProxos() instead. AddProxosMediator() will be removed in a future major version.")]
    public static IServiceCollection AddProxosMediator(
        this IServiceCollection services,
        Action<ProxosConfiguration> configure)
        => AddProxos(services, configure);
}
