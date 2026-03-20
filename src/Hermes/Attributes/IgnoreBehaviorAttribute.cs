namespace HermesMediator;

/// <summary>
/// Instrui o mediator a pular um behavior específico para este request.
/// Pode ser aplicado múltiplas vezes para ignorar vários behaviors.
/// </summary>
/// <example>
/// <code>
/// [IgnoreBehavior(typeof(LoggingBehavior&lt;,&gt;))]
/// [IgnoreBehavior(typeof(ValidationBehavior&lt;,&gt;))]
/// public record InternalCommand(string Data) : IRequest;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = true)]
public sealed class IgnoreBehaviorAttribute : Attribute
{
    /// <summary>Tipo do behavior a ser ignorado.</summary>
    public Type BehaviorType { get; }

    /// <param name="behaviorType">
    /// Tipo concreto ou aberto do behavior (ex: <c>typeof(LoggingBehavior&lt;,&gt;)</c>).
    /// </param>
    public IgnoreBehaviorAttribute(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);
        BehaviorType = behaviorType;
    }
}
