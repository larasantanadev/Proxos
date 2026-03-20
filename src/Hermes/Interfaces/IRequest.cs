namespace HermesMediator;

/// <summary>Request sem retorno — retorna <see cref="Unit"/> internamente.</summary>
public interface IRequest : IRequest<Unit> { }

/// <summary>Request com retorno tipado.</summary>
public interface IRequest<out TResponse> { }

/// <summary>Request que retorna stream assíncrono de dados.</summary>
public interface IStreamRequest<out TResponse> { }
