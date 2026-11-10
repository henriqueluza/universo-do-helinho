using System.Net;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class HealthTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_responde_200()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
