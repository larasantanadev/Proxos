namespace HermesMediator;

/// <summary>Handler principal para requests com retorno tipado.</summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Handler para requests sem retorno explícito (retorna <see cref="Unit"/>).</summary>
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest<Unit>
{ }
