using Proxos;

namespace Proxos.Benchmarks.Requests;

// ── Request / Response ───────────────────────────────────────────────────────

public record ProxosPing(string Message) : IRequest<string>;

public class ProxosPingHandler : IRequestHandler<ProxosPing, string>
{
    public Task<string> Handle(ProxosPing request, CancellationToken ct)
        => Task.FromResult($"Pong: {request.Message}");
}

// ── Notification ─────────────────────────────────────────────────────────────

public record ProxosEvent(string Data) : INotification;

public class ProxosEventHandler : INotificationHandler<ProxosEvent>
{
    public Task Handle(ProxosEvent notification, CancellationToken ct)
        => Task.CompletedTask;
}
