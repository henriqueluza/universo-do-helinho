using Helinho.Application.Identidade;

namespace Helinho.UnitTests;

public class SessionTokensTests
{
    [Fact]
    public void Token_tem_256_bits_em_base64url_e_nao_se_repete()
    {
        var a = SessionTokens.Generate();
        var b = SessionTokens.Generate();

        Assert.NotEqual(a, b);
        Assert.Equal(43, a.Length); // 32 bytes em base64url, sem padding
        Assert.All(a, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
    }

    [Fact]
    public void Hash_e_deterministico_e_nao_revela_o_token()
    {
        var token = SessionTokens.Generate();

        Assert.Equal(SessionTokens.Hash(token), SessionTokens.Hash(token));
        Assert.NotEqual(token, SessionTokens.Hash(token));
        Assert.DoesNotContain(token, SessionTokens.Hash(token), StringComparison.Ordinal);
        Assert.Equal(64, SessionTokens.Hash(token).Length);
    }

    [Fact]
    public void Validade_padrao_e_de_8_horas()
    {
        Assert.Equal(TimeSpan.FromHours(8), new ApiAuthOptions().TokenLifetime);
    }
}
