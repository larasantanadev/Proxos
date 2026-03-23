namespace Proxos;

/// <summary>Interface marcadora base para todos os requests.</summary>
public interface IBaseRequest { }

/// <summary>Request sem retorno — retorna <see cref="Unit"/> internamente.</summary>
public interface IRequest : IRequest<Unit> { }

/// <summary>Request com retorno tipado.</summary>
public interface IRequest<out TResponse> : IBaseRequest { }

/// <summary>Request que retorna stream assíncrono de dados.</summary>
public interface IStreamRequest<out TResponse> : IBaseRequest { }
