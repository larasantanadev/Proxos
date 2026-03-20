namespace HermesMediator;

/// <summary>
/// Define a ordem de execução de um <see cref="IPipelineBehavior{TRequest,TResponse}"/> no pipeline.
/// Behaviors são executados em ordem crescente de <see cref="Order"/>.
/// Behaviors sem este atributo têm <c>order = 0</c>.
/// </summary>
/// <example>
/// <code>
/// [BehaviorOrder(-100)]  // executa primeiro
/// public class ValidationBehavior&lt;TReq, TRes&gt; : IPipelineBehavior&lt;TReq, TRes&gt; { ... }
///
/// [BehaviorOrder(100)]   // executa por último (antes do handler)
/// public class LoggingBehavior&lt;TReq, TRes&gt; : IPipelineBehavior&lt;TReq, TRes&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class BehaviorOrderAttribute : Attribute
{
    /// <summary>
    /// Ordem de execução. Valores menores executam primeiro.
    /// Padrão (sem atributo): 0.
    /// </summary>
    public int Order { get; }

    public BehaviorOrderAttribute(int order) => Order = order;
}
