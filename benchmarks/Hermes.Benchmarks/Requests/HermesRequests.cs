using HermesMediator;

namespace Hermes.Benchmarks.Requests;

// ── Request / Response ───────────────────────────────────────────────────────

public record HermesPing(string Message) : IRequest<string>;

public class HermesPingHandler : IRequestHandler<HermesPing, string>
{
    public Task<string> Handle(HermesPing request, CancellationToken ct)
        => Task.FromResult($"Pong: {request.Message}");
}

// ── Notification ─────────────────────────────────────────────────────────────

public record HermesEvent(string Data) : INotification;

public class HermesEventHandler : INotificationHandler<HermesEvent>
{
    public Task Handle(HermesEvent notification, CancellationToken ct)
        => Task.CompletedTask;
}
