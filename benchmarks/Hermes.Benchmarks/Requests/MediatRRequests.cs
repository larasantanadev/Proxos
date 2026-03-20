using MediatR;

namespace Hermes.Benchmarks.Requests;

// ── Request / Response ───────────────────────────────────────────────────────

public record MediatRPing(string Message) : IRequest<string>;

public class MediatRPingHandler : IRequestHandler<MediatRPing, string>
{
    public Task<string> Handle(MediatRPing request, CancellationToken ct)
        => Task.FromResult($"Pong: {request.Message}");
}

// ── Notification ─────────────────────────────────────────────────────────────

public record MediatREvent(string Data) : INotification;

public class MediatREventHandler : INotificationHandler<MediatREvent>
{
    public Task Handle(MediatREvent notification, CancellationToken ct)
        => Task.CompletedTask;
}
