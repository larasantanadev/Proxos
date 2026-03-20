using System.Reflection;

namespace HermesMediator.Configuration;

/// <summary>
/// Configuração do Hermes passada ao <c>AddHermes()</c>.
/// </summary>
public sealed class HermesConfiguration
{
    private readonly List<Assembly> _assemblies = [];
    private readonly List<Type> _behaviorTypes = [];
    private readonly List<Type> _streamBehaviorTypes = [];

    /// <summary>Estratégia de publicação de notificações. Padrão: <see cref="PublishStrategy.ForeachAwait"/>.</summary>
    public PublishStrategy DefaultPublishStrategy { get; set; } = PublishStrategy.ForeachAwait;

    /// <summary>
    /// Filtro opcional aplicado a cada tipo durante o assembly scanning.
    /// Retorne <c>false</c> para excluir o tipo do registro automático.
    /// Padrão: todos os tipos são incluídos.
    /// </summary>
    /// <example>
    /// <code>
    /// // Exclui tipos de um namespace específico:
    /// cfg.TypeEvaluator = t => !t.Namespace?.StartsWith("MyApp.Legacy") ?? true;
    /// </code>
    /// </example>
    public Func<Type, bool> TypeEvaluator { get; set; } = _ => true;

    /// <summary>
    /// Registra handlers, behaviors, pre/post processors e exception handlers
    /// encontrados nos assemblies informados.
    /// </summary>
    public HermesConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assemblies.Add(assembly);
        return this;
    }

    /// <summary>Registra handlers de múltiplos assemblies.</summary>
    public HermesConfiguration RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
            RegisterServicesFromAssembly(assembly);
        return this;
    }

    /// <summary>
    /// Registra handlers do assembly que contém o tipo <typeparamref name="T"/>.
    /// Equivalente a <c>RegisterServicesFromAssembly(typeof(T).Assembly)</c>.
    /// </summary>
    public HermesConfiguration RegisterServicesFromAssemblyContaining<T>()
        => RegisterServicesFromAssembly(typeof(T).Assembly);

    /// <summary>
    /// Registra handlers do assembly que contém o tipo informado.
    /// Equivalente a <c>RegisterServicesFromAssembly(type.Assembly)</c>.
    /// </summary>
    public HermesConfiguration RegisterServicesFromAssemblyContaining(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return RegisterServicesFromAssembly(type.Assembly);
    }

    /// <summary>
    /// Adiciona um behavior open-generic ao pipeline global.
    /// Ex: <c>AddOpenBehavior(typeof(LoggingBehavior&lt;,&gt;))</c>
    /// </summary>
    public HermesConfiguration AddOpenBehavior(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);
        _behaviorTypes.Add(behaviorType);
        return this;
    }

    /// <summary>
    /// Adiciona um behavior open-generic exclusivamente ao pipeline de streams.
    /// Ex: <c>AddOpenStreamBehavior(typeof(StreamLoggingBehavior&lt;,&gt;))</c>
    /// </summary>
    public HermesConfiguration AddOpenStreamBehavior(Type streamBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(streamBehaviorType);
        _streamBehaviorTypes.Add(streamBehaviorType);
        return this;
    }

    internal IReadOnlyList<Assembly> Assemblies => _assemblies;
    internal IReadOnlyList<Type> BehaviorTypes => _behaviorTypes;
    internal IReadOnlyList<Type> StreamBehaviorTypes => _streamBehaviorTypes;
}
