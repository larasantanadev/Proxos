using HermesMediator.Testing;

namespace HermesMediator.Tests;

public class FakeMediatorTests
{
    public record GetUserQuery(Guid UserId) : IRequest<string>;
    public record DeleteUserCommand(Guid UserId) : IRequest;
    public record UserCreated(Guid UserId) : INotification;
    public record NumberStream(int Max) : IStreamRequest<int>;

    [Fact]
    public async Task FakeMediator_Setup_ReturnsConfiguredValue()
    {
        var fake = new FakeMediator();
        fake.Returns<GetUserQuery, string>("Alice");

        var result = await fake.Send(new GetUserQuery(Guid.NewGuid()));

        Assert.Equal("Alice", result);
    }

    [Fact]
    public async Task FakeMediator_Setup_WithHandler_CallsFunction()
    {
        var fake = new FakeMediator();
        fake.Setup<GetUserQuery, string>(q => $"User:{q.UserId}");

        var id = Guid.NewGuid();
        var result = await fake.Send(new GetUserQuery(id));

        Assert.Equal($"User:{id}", result);
    }

    [Fact]
    public async Task FakeMediator_Throws_PropagatesException()
    {
        var fake = new FakeMediator();
        fake.Throws<GetUserQuery>(new NotFoundException("User not found"));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fake.Send(new GetUserQuery(Guid.NewGuid())));
    }

    [Fact]
    public async Task FakeMediator_WasSent_DetectsRequest()
    {
        var fake = new FakeMediator();
        fake.Returns<GetUserQuery, string>("x");

        Assert.False(fake.WasSent<GetUserQuery>());

        await fake.Send(new GetUserQuery(Guid.NewGuid()));

        Assert.True(fake.WasSent<GetUserQuery>());
    }

    [Fact]
    public async Task FakeMediator_WasSent_WithPredicate_FiltersCorrectly()
    {
        var fake = new FakeMediator();
        var targetId = Guid.NewGuid();
        fake.Returns<GetUserQuery, string>("x");

        await fake.Send(new GetUserQuery(targetId));
        await fake.Send(new GetUserQuery(Guid.NewGuid()));

        Assert.True(fake.WasSent<GetUserQuery>(q => q.UserId == targetId));
        Assert.Equal(2, fake.CountSent<GetUserQuery>());
    }

    [Fact]
    public async Task FakeMediator_WasPublished_DetectsNotification()
    {
        var fake = new FakeMediator();

        Assert.False(fake.WasPublished<UserCreated>());

        await fake.Publish(new UserCreated(Guid.NewGuid()));

        Assert.True(fake.WasPublished<UserCreated>());
    }

    [Fact]
    public async Task FakeMediator_VoidRequest_CompletesWithoutSetup()
    {
        var fake = new FakeMediator();

        // Void requests sem setup devem completar sem exceção
        await fake.Send(new DeleteUserCommand(Guid.NewGuid()));

        Assert.True(fake.WasSent<DeleteUserCommand>());
    }

    [Fact]
    public async Task FakeMediator_ReturnsStream_YieldsItems()
    {
        var fake = new FakeMediator();
        fake.ReturnsStream<NumberStream, int>([1, 2, 3]);

        var results = new List<int>();
        await foreach (var item in fake.CreateStream(new NumberStream(3)))
            results.Add(item);

        Assert.Equal([1, 2, 3], results);
    }

    [Fact]
    public async Task FakeMediator_Reset_ClearsHistory()
    {
        var fake = new FakeMediator();
        fake.Returns<GetUserQuery, string>("x");
        await fake.Send(new GetUserQuery(Guid.NewGuid()));

        fake.Reset();

        Assert.False(fake.WasSent<GetUserQuery>());
        Assert.Empty(fake.SentRequests);
    }

    [Fact]
    public async Task FakeMediator_NoSetup_ThrowsInvalidOperationException()
    {
        var fake = new FakeMediator();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fake.Send(new GetUserQuery(Guid.NewGuid())));
    }

    private sealed class NotFoundException(string message) : Exception(message);
}
