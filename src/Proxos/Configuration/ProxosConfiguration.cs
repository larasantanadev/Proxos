using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Proxos.Configuration;

/// <summary>
/// Configuração do Proxos passada ao <c>AddProxos()</c>.
/// </summary>
public sealed class ProxosConfiguration
{
    private readonly List<Assembly> _assemblies = [];

    // Registros com ServiceLifetime explícito
    internal readonly List<ServiceDescriptor> BehaviorsToRegister = [];
    internal readonly List<ServiceDescriptor> StreamBehaviorsToRegister = [];
    internal readonly List<ServiceDescriptor> RequestPreProcessorsToRegister = [];
    internal readonly List<ServiceDescriptor> RequestPostProcessorsToRegister = [];

    /// <summary>Estratégia de publicação de notificações. Padrão: <see cref="PublishStrategy.ForeachAwait"/>.</summary>
    public PublishStrategy DefaultPublishStrategy { get; set; } = PublishStrategy.ForeachAwait;

    /// <summary>
    /// Publisher personalizado de notificações. Quando definido, substitui o
    /// <see cref="DefaultPublishStrategy"/>. Permite implementar qualquer estratégia de publicação.
    /// </summary>
    public INotificationPublisher? NotificationPublisher { get; set; }

    /// <summary>
    /// Filtro opcional aplicado a cada tipo durante o assembly scanning.
    /// Retorne <c>false</c> para excluir o tipo do registro automático.
    /// Padrão: todos os tipos são incluídos.
    /// </summary>
    public Func<Type, bool> TypeEvaluator { get; set; } = _ => true;

    // -------------------------------------------------------------------------
    // Assemblies
    // -------------------------------------------------------------------------

    /// <summary>Registra handlers, behaviors, pre/post processors e exception handlers do assembly.</summary>
    public ProxosConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assemblies.Add(assembly);
        return this;
    }

    /// <summary>Registra handlers de múltiplos assemblies.</summary>
    public ProxosConfiguration RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
            RegisterServicesFromAssembly(assembly);
        return this;
    }

    /// <summary>Registra handlers do assembly que contém o tipo <typeparamref name="T"/>.</summary>
    public ProxosConfiguration RegisterServicesFromAssemblyContaining<T>()
        => RegisterServicesFromAssembly(typeof(T).Assembly);

    /// <summary>Registra handlers do assembly que contém o tipo informado.</summary>
    public ProxosConfiguration RegisterServicesFromAssemblyContaining(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return RegisterServicesFromAssembly(type.Assembly);
    }

    // -------------------------------------------------------------------------
    // Behaviors — open-generic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adiciona um behavior open-generic ao pipeline global.
    /// Ex: <c>AddOpenBehavior(typeof(LoggingBehavior&lt;,&gt;))</c>
    /// </summary>
    /// <param name="openBehaviorType">Tipo open-generic do behavior.</param>
    /// <param name="lifetime">Lifetime de DI. Padrão: <see cref="ServiceLifetime.Scoped"/>.</param>
    public ProxosConfiguration AddOpenBehavior(
        Type openBehaviorType,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);
        BehaviorsToRegister.Add(
            new ServiceDescriptor(typeof(IPipelineBehavior<,>), openBehaviorType, lifetime));
        return this;
    }

    // -------------------------------------------------------------------------
    // Behaviors — closed (instância específica)
    // -------------------------------------------------------------------------

    /// <summary>Adiciona um behavior fechado ao pipeline global.</summary>
    /// <typeparam name="TService">Interface do behavior (ex: <c>IPipelineBehavior&lt;MyReq, MyRes&gt;</c>).</typeparam>
    /// <typeparam name="TImplementation">Tipo concreto do behavior.</typeparam>
    /// <param name="lifetime">Lifetime de DI. Padrão: <see cref="ServiceLifetime.Scoped"/>.</param>
    public ProxosConfiguration AddBehavior<TService, TImplementation>(
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TService : class
        where TImplementation : class, TService
    {
        BehaviorsToRegister.Add(
            new ServiceDescriptor(typeof(TService), typeof(TImplementation), lifetime));
        return this;
    }

    /// <summary>Adiciona um behavior fechado ao pipeline global (inferência de serviço).</summary>
    public ProxosConfiguration AddBehavior<TImplementation>(
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TImplementation : class
    {
        BehaviorsToRegister.Add(
            new ServiceDescriptor(typeof(TImplementation), typeof(TImplementation), lifetime));
        return this;
    }

    /// <summary>Adiciona um behavior fechado ao pipeline global.</summary>
    public ProxosConfiguration AddBehavior(
        Type serviceType,
        Type implementationType,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        BehaviorsToRegister.Add(
            new ServiceDescriptor(serviceType, implementationType, lifetime));
        return this;
    }

    // -------------------------------------------------------------------------
    // Stream behaviors
    // -------------------------------------------------------------------------

    /// <summary>Adiciona um stream behavior open-generic ao pipeline de streams.</summary>
    public ProxosConfiguration AddOpenStreamBehavior(
        Type openStreamBehaviorType,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(openStreamBehaviorType);
        StreamBehaviorsToRegister.Add(
            new ServiceDescriptor(typeof(IStreamPipelineBehavior<,>), openStreamBehaviorType, lifetime));
        return this;
    }

    /// <summary>Adiciona um stream behavior fechado ao pipeline de streams.</summary>
    public ProxosConfiguration AddStreamBehavior<TService, TImplementation>(
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TService : class
        where TImplementation : class, TService
    {
        StreamBehaviorsToRegister.Add(
            new ServiceDescriptor(typeof(TService), typeof(TImplementation), lifetime));
        return this;
    }

    /// <summary>Adiciona um stream behavior fechado ao pipeline de streams.</summary>
    public ProxosConfiguration AddStreamBehavior(
        Type serviceType,
        Type implementationType,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        StreamBehaviorsToRegister.Add(
            new ServiceDescriptor(serviceType, implementationType, lifetime));
        return this;
    }

    // -------------------------------------------------------------------------
    // Pre-processors
    // -------------------------------------------------------------------------

    /// <summary>Adiciona um pre-processor open-generic ao pipeline global.</summary>
    public ProxosConfiguration AddOpenRequestPreProcessor(
        Type openPreProcessorType,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(openPreProcessorType);
        RequestPreProcessorsToRegister.Add(
            new ServiceDescriptor(typeof(IRequestPreProcessor<>), openPreProcessorType, lifetime));
        return this;
    }

    /// <summary>Adiciona um pre-processor fechado ao pipeline.</summary>
    public ProxosConfiguration AddRequestPreProcessor<TService, TImplementation>(
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TService : class
        where TImplementation : class, TService
    {
        RequestPreProcessorsToRegister.Add(
            new ServiceDescriptor(typeof(TService), typeof(TImplementation), lifetime));
        return this;
    }

    /// <summary>Adiciona um pre-processor fechado ao pipeline (inferência automática de interface).</summary>
    public ProxosConfiguration AddRequestPreProcessor<TImplementation>(
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TImplementation : class
    {
        RequestPreProcessorsToRegister.Add(
            new ServiceDescriptor(typeof(TImplementation), typeof(TImplementation), lifetime));
        return this;
    }

    /// <summary>Adiciona um pre-processor fechado ao pipeline.</summary>
    public ProxosConfiguration AddRequestPreProcessor(
        Type serviceType,
        Type implementationType,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        RequestPreProcessorsToRegister.Add(
            new ServiceDescriptor(serviceType, implementationType, lifetime));
        return this;
    }

    // -------------------------------------------------------------------------
    // Post-processors
    // -------------------------------------------------------------------------

    /// <summary>Adiciona um post-processor open-generic ao pipeline global.</summary>
    public ProxosConfiguration AddOpenRequestPostProcessor(
        Type openPostProcessorType,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(openPostProcessorType);
        RequestPostProcessorsToRegister.Add(
            new ServiceDescriptor(typeof(IRequestPostProcessor<,>), openPostProcessorType, lifetime));
        return this;
    }

    /// <summary>Adiciona um post-processor fechado ao pipeline.</summary>
    public ProxosConfiguration AddRequestPostProcessor<TService, TImplementation>(
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TService : class
        where TImplementation : class, TService
    {
        RequestPostProcessorsToRegister.Add(
            new ServiceDescriptor(typeof(TService), typeof(TImplementation), lifetime));
        return this;
    }

    /// <summary>Adiciona um post-processor fechado ao pipeline (inferência automática de interface).</summary>
    public ProxosConfiguration AddRequestPostProcessor<TImplementation>(
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TImplementation : class
    {
        RequestPostProcessorsToRegister.Add(
            new ServiceDescriptor(typeof(TImplementation), typeof(TImplementation), lifetime));
        return this;
    }

    /// <summary>Adiciona um post-processor fechado ao pipeline.</summary>
    public ProxosConfiguration AddRequestPostProcessor(
        Type serviceType,
        Type implementationType,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        RequestPostProcessorsToRegister.Add(
            new ServiceDescriptor(serviceType, implementationType, lifetime));
        return this;
    }

    internal IReadOnlyList<Assembly> Assemblies => _assemblies;
}
