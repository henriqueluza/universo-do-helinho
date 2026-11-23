using Helinho.Application.Identidade;

namespace Helinho.UnitTests;

public class DefinirSenhaHandlerTests
{
    private const string Token = "CfDJ8+token/sintetico==";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly FakeGateway _gateway = new();
    private readonly DefinirSenhaHandler _handler;

    public DefinirSenhaHandlerTests() => _handler = new DefinirSenhaHandler(_gateway);

    private static string TokenDaUrl(string link) => link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..];

    [Fact]
    public void Link_leva_so_o_id_e_o_token_em_base64_para_url()
    {
        var link = LinkDeAcesso.Montar(UserId, Token);

        Assert.StartsWith($"/conta/definir-senha?usuario={UserId:D}&token=", link, StringComparison.Ordinal);
        Assert.All(TokenDaUrl(link), c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
        Assert.Equal(Token, LinkDeAcesso.LerToken(TokenDaUrl(link)));
        Assert.Equal(TimeSpan.FromHours(72), LinkDeAcesso.ValidadeConvite);
        Assert.Equal(TimeSpan.FromHours(1), LinkDeAcesso.ValidadeRedefinicao);
    }

    [Fact]
    public async Task Link_bem_formado_chega_a_porta_com_o_token_decodificado()
    {
        var token = TokenDaUrl(LinkDeAcesso.Montar(UserId, Token));

        Assert.True(await _handler.LinkValidoAsync(UserId.ToString(), token, Ct));
        var result = await _handler.HandleAsync(UserId.ToString(), token, "Nova-Sintetica-7", Ct);

        Assert.Equal(DefinirSenhaStatus.Definida, result.Status);
        Assert.Equal([(UserId, Token), (UserId, Token)], _gateway.Calls);
    }

    [Theory]
    [InlineData(null, "dG9rZW4")]
    [InlineData("", "dG9rZW4")]
    [InlineData("nao-e-uuid", "dG9rZW4")]
    [InlineData("00000000000000000000000000000001", "dG9rZW4")] // UUID fora do formato do link
    [InlineData("3f2b8c1e-5d47-4a9b-8e21-7c6d5b4a3f10", null)]
    [InlineData("3f2b8c1e-5d47-4a9b-8e21-7c6d5b4a3f10", "")]
    [InlineData("3f2b8c1e-5d47-4a9b-8e21-7c6d5b4a3f10", "tem+barra/e=igual")]
    public async Task Id_ou_token_malformado_e_link_invalido_sem_consultar_a_conta(string? usuario, string? token)
    {
        Assert.False(await _handler.LinkValidoAsync(usuario, token, Ct));
        Assert.Equal(DefinirSenhaStatus.LinkInvalido, (await _handler.HandleAsync(usuario, token, "Nova-Sintetica-7", Ct)).Status);
        Assert.False(await _handler.LinkValidoAsync(UserId.ToString(), new string('A', 2049), Ct));
        Assert.Empty(_gateway.Calls);
    }

    private sealed class FakeGateway : IPasswordLinkGateway
    {
        public List<(Guid UserId, string Token)> Calls { get; } = [];

        public Task<bool> IsValidAsync(Guid userId, string token, CancellationToken cancellationToken)
        {
            Calls.Add((userId, token));
            return Task.FromResult(true);
        }

        public Task<DefinirSenhaResult> SetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken)
        {
            Calls.Add((userId, token));
            return Task.FromResult(DefinirSenhaResult.Definida);
        }
    }
}
