using Xunit;

namespace IPS.Middleware.AspireTests;

// One shared stack with a single API instance for the scenarios that do not need more. Each test resets the simulators and uses
// client references of its own, so what the service stored in the shared database never collides.
public sealed class SingleInstanceStack : IAsyncLifetime
{
    internal Stack? Stack { get; private set; }

    internal Stack Started => Stack ?? throw new InvalidOperationException("The stack did not start.");

    public async Task InitializeAsync()
    {
        // Without Docker the tests are skipped, so there is nothing to start.
        if (DockerFactAttribute.Available)
        {
            Stack = await Stack.StartAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (Stack is not null)
        {
            await Stack.DisposeAsync();
        }
    }
}

[CollectionDefinition("Single instance stack")]
public sealed class SingleInstanceStackCollection : ICollectionFixture<SingleInstanceStack>;
