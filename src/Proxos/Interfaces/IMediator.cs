namespace Proxos;

/// <summary>
/// Interface principal do Proxos — combina <see cref="ISender"/> e <see cref="IPublisher"/>.
/// Injete <see cref="IMediator"/> quando precisar de send e publish no mesmo serviço,
/// ou prefira <see cref="ISender"/>/<see cref="IPublisher"/> para dependências mais estreitas.
/// </summary>
public interface IMediator : ISender, IPublisher { }
