namespace Proxos;

/// <summary>Behavior para pipelines de stream (IAsyncEnumerable).</summary>
public interface IStreamPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
