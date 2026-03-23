using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Proxos.Generator.Analyzers;

/// <summary>
/// Analyzer Roslyn PRX001.
/// Detecta em compile-time quando um <c>IRequest&lt;TResponse&gt;</c> ou
/// <c>IStreamRequest&lt;TResponse&gt;</c> não tem handler correspondente no projeto
/// nem em qualquer assembly referenciado.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingHandlerAnalyzer : DiagnosticAnalyzer
{
    private const string IRequestMetadata              = "Proxos.IRequest`1";
    private const string IRequestHandlerMetadata       = "Proxos.IRequestHandler`2";
    private const string IStreamRequestMetadata        = "Proxos.IStreamRequest`1";
    private const string IStreamRequestHandlerMetadata = "Proxos.IStreamRequestHandler`2";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.MissingHandler, DiagnosticDescriptors.MissingStreamHandler];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationCtx =>
        {
            var iRequestSymbol = compilationCtx.Compilation
                .GetTypeByMetadataName(IRequestMetadata);
            var iRequestHandlerSymbol = compilationCtx.Compilation
                .GetTypeByMetadataName(IRequestHandlerMetadata);
            var iStreamRequestSymbol = compilationCtx.Compilation
                .GetTypeByMetadataName(IStreamRequestMetadata);
            var iStreamRequestHandlerSymbol = compilationCtx.Compilation
                .GetTypeByMetadataName(IStreamRequestHandlerMetadata);

            if (iRequestSymbol is null || iRequestHandlerSymbol is null)
                return;

            // Pré-coleta handlers de assemblies referenciados (fix cross-assembly)
            var handledFromRefs       = new HashSet<string>(StringComparer.Ordinal);
            var handledStreamFromRefs = new HashSet<string>(StringComparer.Ordinal);

            foreach (var reference in compilationCtx.Compilation.References)
            {
                if (compilationCtx.Compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asm)
                {
                    CollectHandlersFromNamespace(
                        asm.GlobalNamespace,
                        iRequestHandlerSymbol,
                        iStreamRequestHandlerSymbol,
                        handledFromRefs,
                        handledStreamFromRefs);
                }
            }

            // Coleta thread-safe da compilação atual
            var requestTypes        = new ConcurrentBag<(INamedTypeSymbol Type, ITypeSymbol ResponseType, Location Location)>();
            var handledRequestTypes = new ConcurrentBag<string>();

            var streamRequestTypes        = new ConcurrentBag<(INamedTypeSymbol Type, ITypeSymbol ResponseType, Location Location)>();
            var handledStreamRequestTypes = new ConcurrentBag<string>();

            compilationCtx.RegisterSymbolAction(symbolCtx =>
            {
                if (symbolCtx.Symbol is not INamedTypeSymbol namedType)
                    return;

                if (namedType.IsAbstract || namedType.TypeKind == TypeKind.Interface || namedType.IsGenericType)
                    return;

                foreach (var iface in namedType.AllInterfaces)
                {
                    symbolCtx.CancellationToken.ThrowIfCancellationRequested();

                    // IRequest<TResponse> sem handler
                    if (iRequestSymbol is not null
                        && SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, iRequestSymbol)
                        && iface.TypeArguments.Length == 1)
                    {
                        var location = namedType.Locations.Length > 0
                            ? namedType.Locations[0]
                            : Location.None;
                        requestTypes.Add((namedType, iface.TypeArguments[0], location));
                    }

                    // IRequestHandler<TRequest, TResponse>
                    if (iRequestHandlerSymbol is not null
                        && SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, iRequestHandlerSymbol)
                        && iface.TypeArguments.Length == 2)
                    {
                        handledRequestTypes.Add(iface.TypeArguments[0].ToDisplayString());
                    }

                    // IStreamRequest<TResponse> sem handler
                    if (iStreamRequestSymbol is not null
                        && SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, iStreamRequestSymbol)
                        && iface.TypeArguments.Length == 1)
                    {
                        var location = namedType.Locations.Length > 0
                            ? namedType.Locations[0]
                            : Location.None;
                        streamRequestTypes.Add((namedType, iface.TypeArguments[0], location));
                    }

                    // IStreamRequestHandler<TRequest, TResponse>
                    if (iStreamRequestHandlerSymbol is not null
                        && SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, iStreamRequestHandlerSymbol)
                        && iface.TypeArguments.Length == 2)
                    {
                        handledStreamRequestTypes.Add(iface.TypeArguments[0].ToDisplayString());
                    }
                }
            }, SymbolKind.NamedType);

            compilationCtx.RegisterCompilationEndAction(endCtx =>
            {
                // Une handlers do projeto atual com handlers de assemblies referenciados
                var handled = new HashSet<string>(handledRequestTypes, StringComparer.Ordinal);
                handled.UnionWith(handledFromRefs);

                var handledStream = new HashSet<string>(handledStreamRequestTypes, StringComparer.Ordinal);
                handledStream.UnionWith(handledStreamFromRefs);

                foreach (var (requestType, responseType, location) in requestTypes)
                {
                    endCtx.CancellationToken.ThrowIfCancellationRequested();

                    if (!handled.Contains(requestType.ToDisplayString()))
                    {
                        endCtx.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.MissingHandler,
                            location,
                            requestType.Name,
                            $"IRequest<{responseType.Name}>",
                            responseType.Name));
                    }
                }

                foreach (var (streamType, responseType, location) in streamRequestTypes)
                {
                    endCtx.CancellationToken.ThrowIfCancellationRequested();

                    if (!handledStream.Contains(streamType.ToDisplayString()))
                    {
                        endCtx.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.MissingStreamHandler,
                            location,
                            streamType.Name,
                            $"IStreamRequest<{responseType.Name}>",
                            responseType.Name));
                    }
                }
            });
        });
    }

    /// <summary>
    /// Varre recursivamente um namespace (de assembly referenciado) coletando todos os
    /// handlers de request e stream — resolve o falso positivo cross-assembly.
    /// </summary>
    private static void CollectHandlersFromNamespace(
        INamespaceSymbol ns,
        INamedTypeSymbol? handlerSymbol,
        INamedTypeSymbol? streamHandlerSymbol,
        HashSet<string> handled,
        HashSet<string> handledStream)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol childNs)
            {
                CollectHandlersFromNamespace(childNs, handlerSymbol, streamHandlerSymbol, handled, handledStream);
                continue;
            }

            if (member is not INamedTypeSymbol type)
                continue;

            if (type.IsAbstract || type.TypeKind == TypeKind.Interface || type.IsGenericType)
                continue;

            foreach (var iface in type.AllInterfaces)
            {
                if (handlerSymbol is not null
                    && SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, handlerSymbol)
                    && iface.TypeArguments.Length == 2)
                {
                    handled.Add(iface.TypeArguments[0].ToDisplayString());
                }

                if (streamHandlerSymbol is not null
                    && SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, streamHandlerSymbol)
                    && iface.TypeArguments.Length == 2)
                {
                    handledStream.Add(iface.TypeArguments[0].ToDisplayString());
                }
            }
        }
    }
}
