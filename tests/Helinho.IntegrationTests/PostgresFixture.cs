using Testcontainers.PostgreSql;

namespace Helinho.IntegrationTests;

/// <summary>PostgreSQL real em container, compartilhado por coleção. Frentes reutilizam via [Collection(PostgresCollection.Name)].</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // max_connections acima do padrão (100): a suíte roda classes em paralelo contra o mesmo container.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithCommand("-c", "max_connections=300")
        .Build();

    /// <summary>
    /// Sem pool: cada teste usa um banco próprio (connection string única), e o Npgsql manteria um pool
    /// ocioso por banco até o fim da execução, esgotando as conexões do servidor (erro 53300).
    /// </summary>
    public string ConnectionString =>
        new Npgsql.NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Pooling = false }.ConnectionString;

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
