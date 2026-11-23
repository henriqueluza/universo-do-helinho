using Helinho.Application.Identidade;

namespace Helinho.UnitTests;

public class DefinirSenhaHandlerTests
{
    private const string Token = "CfDJ8+token/sintetico==";
    private const string Senha = "Nova-Sintetica-7";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly FakeGateway _gateway = new();
    private readonly DefinirSenhaHandler _handler;

    public DefinirSenhaHandlerTests() => _handler = new DefinirSenhaHandler(_gateway);

    [Fact]
    public async Task Link_leva_so_o_id_e_o_token_em_base64_para_url_e_chega_decodificado_a_porta()
    {
        var token = LinkDeAcesso.TokenParaUrl(Token);

        Assert.Equal($"/conta/definir-senha?usuario={UserId:D}&token={token}", LinkDeAcesso.Montar(UserId, Token));
        Assert.All(token, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
        Assert.True(await _handler.LinkValidoAsync(UserId.ToString(), token, Ct));
        Assert.Equal(DefinirSenhaStatus.Definida, (await _handler.HandleAsync(UserId.ToString(), token, Senha, Ct)).Status);
        Assert.Equal([(UserId, Token), (UserId, Token)], _gateway.Calls);
    }

    [Theory]
    [InlineData(null, "dG9rZW4")]
    [InlineData("nao-e-uuid", "dG9rZW4")]
    [InlineData("00000000000000000000000000000001", "dG9rZW4")] // UUID fora do formato do link
    [InlineData("3f2b8c1e-5d47-4a9b-8e21-7c6d5b4a3f10", null)]
    [InlineData("3f2b8c1e-5d47-4a9b-8e21-7c6d5b4a3f10", "tem+barra/e=igual")]
    public async Task Id_ou_token_malformado_e_link_invalido_sem_consultar_a_conta(string? usuario, string? token)
    {
        Assert.False(await _handler.LinkValidoAsync(usuario, token, Ct));
        Assert.Equal(DefinirSenhaStatus.LinkInvalido, (await _handler.HandleAsync(usuario, token, Senha, Ct)).Status);
        Assert.False(await _handler.LinkValidoAsync(UserId.ToString(), new string('A', 2049), Ct));
        Assert.Empty(_gateway.Calls);
    }

    private sealed class FakeGateway : IPasswordLinkGateway
    {
        public List<(Guid UserId, string Token)> Calls { get; } = [];

        public Task<bool> IsValidAsync(Guid userId, string token, CancellationToken cancellationToken) => Registrar(userId, token, true);

        public Task<DefinirSenhaResult> SetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken) =>
            Registrar(userId, token, DefinirSenhaResult.Of(DefinirSenhaStatus.Definida));

        private Task<T> Registrar<T>(Guid userId, string token, T resposta)
        {
            Calls.Add((userId, token));
            return Task.FromResult(resposta);
        }
    }
}
