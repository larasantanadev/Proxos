using System.Reflection;
using HermesMediator;
using HermesMediator.Configuration;
using HermesMediator.Context;
using HermesMediator.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extensões de registro do HermesMediator no container de DI.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra o HermesMediator no container de DI.
    /// </summary>
    /// <param name="services">O <see cref="IServiceCollection"/> do app.</param>
    /// <param name="configure">Configuração do HermesMediator (assemblies, behaviors, estratégia de publish).</param>
    /// <returns>O mesmo <see cref="IServiceCollection"/> para encadeamento.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddHermesMediator(cfg => cfg
    ///     .RegisterServicesFromAssembly(typeof(Program).Assembly)
    ///     .AddOpenBehavior(typeof(LoggingBehavior&lt;,&gt;)));
    /// </code>
    /// </example>
    public static IServiceCollection AddHermesMediator(
        this IServiceCollection services,
        Action<HermesConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var config = new HermesConfiguration();
        configure(config);

        // Singleton: configuração e registry
        services.AddSingleton(config);

        var registry = new WrapperRegistry();
        services.AddSingleton(registry);

        // Scoped: mediator e context accessor
        services.TryAddScoped<IPipelineContextAccessor, PipelineContextAccessor>();
        services.TryAddScoped<IMediator>(sp => new Mediator(
            sp,
            sp.GetRequiredService<WrapperRegistry>(),
            sp.GetRequiredService<IPipelineContextAccessor>(),
            sp.GetRequiredService<HermesConfiguration>()));
        services.TryAddScoped<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.TryAddScoped<IPublisher>(sp => sp.GetRequiredService<IMediator>());

        // Warmup: congela o FrozenDictionary no startup
        services.AddHostedService<WarmupService>();

        // Registra handlers e behaviors dos assemblies informados
        foreach (var assembly in config.Assemblies)
            RegisterFromAssembly(services, assembly, registry, config);

        // Behaviors open-generic globais
        foreach (var behaviorType in config.BehaviorTypes)
            services.AddScoped(typeof(IPipelineBehavior<,>), behaviorType);

        // Stream behaviors open-generic globais
        foreach (var streamBehaviorType in config.StreamBehaviorTypes)
            services.AddScoped(typeof(IStreamPipelineBehavior<,>), streamBehaviorType);

        return services;
    }

    private static void RegisterFromAssembly(
        IServiceCollection services,
        Assembly assembly,
        WrapperRegistry registry,
        HermesConfiguration config)
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

    /// <inheritdoc cref="AddHermesMediator"/>
    [Obsolete("Use AddHermesMediator() instead. AddHermes() will be removed in a future major version.")]
    public static IServiceCollection AddHermes(
        this IServiceCollection services,
        Action<HermesConfiguration> configure)
        => AddHermesMediator(services, configure);
}
