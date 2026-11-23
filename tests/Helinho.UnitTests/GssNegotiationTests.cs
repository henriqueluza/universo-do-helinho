using Helinho.Infrastructure.DependencyInjection;
using Npgsql;

namespace Helinho.UnitTests;

/// <summary>
/// Issue #103: a abertura síncrona do Npgsql (a do anel de chaves do Data Protection) pode ficar sem tempo limite quando
/// repete a tentativa por causa da negociação GSS. A aplicação desliga essa negociação, salvo pedido explícito.
/// </summary>
public class GssNegotiationTests
{
    private const string Synthetic = "Host=banco.exemplo.test;Database=helinho;Username=helinho;Timeout=7";

    private static NpgsqlConnectionStringBuilder Parse(string? connectionString) => new(connectionString);

    [Fact]
    public void Sem_pedido_explicito_a_negociacao_GSS_fica_desligada_e_o_resto_da_connection_string_nao_muda()
    {
        var result = Parse(DomainPersistenceExtensions.WithoutGssNegotiation(Synthetic));

        Assert.Equal(GssEncryptionMode.Disable, result.GssEncryptionMode);
        Assert.Equal(("banco.exemplo.test", "helinho", "helinho", 7), (result.Host, result.Database, result.Username, result.Timeout));
    }

    [Theory]
    [InlineData("GSS Encryption Mode=Prefer", GssEncryptionMode.Prefer)]
    [InlineData("GssEncryptionMode=Require", GssEncryptionMode.Require)]
    public void Valor_explicito_na_connection_string_e_respeitado(string setting, GssEncryptionMode expected) =>
        Assert.Equal(expected, Parse(DomainPersistenceExtensions.WithoutGssNegotiation($"{Synthetic};{setting}")).GssEncryptionMode);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Connection_string_ausente_passa_sem_alteracao(string? connectionString) =>
        Assert.Equal(connectionString, DomainPersistenceExtensions.WithoutGssNegotiation(connectionString));
}
