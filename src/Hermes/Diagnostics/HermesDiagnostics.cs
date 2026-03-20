using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HermesMediator.Diagnostics;

/// <summary>
/// Telemetria OpenTelemetry nativa do Hermes.
/// Exponha o <see cref="ActivitySource"/> e o <see cref="Meter"/> ao seu provider OTel.
/// </summary>
public static class HermesDiagnostics
{
    /// <summary>Nome do ActivitySource — use para filtrar traces: <c>"Hermes"</c>.</summary>
    public const string ActivitySourceName = "Hermes";

    /// <summary>Nome do Meter — use para filtrar métricas: <c>"Hermes"</c>.</summary>
    public const string MeterName = "Hermes";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    internal static readonly Meter Meter = new(MeterName, "1.0.0");

    // --- Contadores ---

    /// <summary>Total de requests enviados via <c>Send</c>.</summary>
    internal static readonly Counter<long> RequestsTotal =
        Meter.CreateCounter<long>("hermes.requests.total", "requests", "Total de requests enviados");

    /// <summary>Total de requests que falharam (exceção não tratada).</summary>
    internal static readonly Counter<long> RequestsFailed =
        Meter.CreateCounter<long>("hermes.requests.failed", "requests", "Total de requests com falha");

    /// <summary>Total de notificações publicadas.</summary>
    internal static readonly Counter<long> NotificationsTotal =
        Meter.CreateCounter<long>("hermes.notifications.total", "notifications", "Total de notificações publicadas");

    /// <summary>Total de requests que excederam o timeout.</summary>
    internal static readonly Counter<long> TimeoutsTotal =
        Meter.CreateCounter<long>("hermes.timeouts.total", "requests", "Total de requests que expiraram por timeout");

    // --- Histogramas ---

    /// <summary>Duração de execução dos requests em milissegundos.</summary>
    internal static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>("hermes.request.duration", "ms", "Duração dos requests em milissegundos");

    // --- Helpers para criar Activities ---

    internal static Activity? StartRequestActivity(string requestName)
    {
        var activity = ActivitySource.StartActivity(
            $"HermesMediator.Send {requestName}",
            ActivityKind.Internal);

        activity?.SetTag("hermes.request.type", requestName);
        return activity;
    }

    internal static Activity? StartPublishActivity(string notificationName)
    {
        var activity = ActivitySource.StartActivity(
            $"HermesMediator.Publish {notificationName}",
            ActivityKind.Internal);

        activity?.SetTag("hermes.notification.type", notificationName);
        return activity;
    }

    internal static Activity? StartStreamActivity(string requestName)
    {
        var activity = ActivitySource.StartActivity(
            $"HermesMediator.Stream {requestName}",
            ActivityKind.Internal);

        activity?.SetTag("hermes.stream.type", requestName);
        return activity;
    }
}
