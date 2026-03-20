using Microsoft.CodeAnalysis;

namespace HermesMediator.Generator.Analyzers;

internal static class DiagnosticDescriptors
{
    /// <summary>
    /// HRM001 — IRequest sem handler correspondente.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingHandler = new(
        id: "HRM001",
        title: "Handler ausente para IRequest",
        messageFormat: "'{0}' implementa '{1}' mas não existe 'IRequestHandler<{0}, {2}>' neste projeto. " +
                       "Crie o handler ou registre-o em outro assembly.",
        category: "Hermes",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Todo IRequest deve ter um IRequestHandler correspondente. " +
                     "Sem handler, o mediator lançará InvalidOperationException em runtime.",
        helpLinkUri: "https://github.com/seu-org/hermes#HRM001",
        customTags: [WellKnownDiagnosticTags.Unnecessary]);

    /// <summary>
    /// HRM002 — IStreamRequest sem handler correspondente.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingStreamHandler = new(
        id: "HRM002",
        title: "Handler ausente para IStreamRequest",
        messageFormat: "'{0}' implementa '{1}' mas não existe 'IStreamRequestHandler<{0}, {2}>' neste projeto. " +
                       "Crie o handler ou registre-o em outro assembly.",
        category: "Hermes",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Todo IStreamRequest deve ter um IStreamRequestHandler correspondente. " +
                     "Sem handler, o mediator lançará InvalidOperationException em runtime.",
        helpLinkUri: "https://github.com/seu-org/hermes#HRM002",
        customTags: [WellKnownDiagnosticTags.Unnecessary]);
}
