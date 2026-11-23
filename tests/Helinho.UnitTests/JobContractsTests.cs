using Helinho.Application.Abstractions;

namespace Helinho.UnitTests;

/// <summary>Contratos da fila (ADR-0003 §5.1): o que entra na fila não comporta texto livre.</summary>
public class JobContractsTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    [Fact]
    public void Payload_e_versionado_e_leva_so_ids_inteiros_booleanos_e_codigos()
    {
        var json = new JobPayload().WithId("aiJobId", Id).WithNumber("count", 3).WithFlag("auto", true).WithCode("reason", "auto-approval").ToJson();

        Assert.Equal("""{"v":1,"aiJobId":"11111111-1111-1111-1111-111111111111","count":3,"auto":true,"reason":"auto-approval"}""", json);
        Assert.Equal("""{"v":2}""", new JobPayload(2).ToJson());
        Assert.Equal("http_429", JobCode.Require("http_429", "code"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new JobPayload(0));
    }

    [Theory]
    [InlineData("responsavel@example.org")]
    [InlineData("Maria Sintética")]
    [InlineData("texto livre digitado")]
    [InlineData("Resend")]
    [InlineData("ação")]
    [InlineData("")]
    [InlineData("resend\n")]
    [InlineData("codigo-com-mais-de-trinta-caracteres")]
    [InlineData("https://example.org/conta/definir-senha?token=abc")]
    public void Texto_livre_nao_entra_no_payload(string text) =>
        Assert.Throws<ArgumentException>(() => new JobPayload().WithCode("reason", text));

    [Theory]
    [InlineData("v")]
    [InlineData("")]
    [InlineData("com espaço")]
    [InlineData("Maiuscula")]
    [InlineData("e@mail")]
    public void Nome_de_parametro_do_payload_e_restrito(string name) =>
        Assert.Throws<ArgumentException>(() => new JobPayload().WithFlag(name, true));

    [Fact]
    public void Pedido_valida_tipo_validade_e_chave_de_idempotencia()
    {
        var request = new JobRequest("email.admin-invite", Id, new JobPayload(), OneHour, $"email.admin-invite:{Id}:14712345");

        Assert.Equal("email.admin-invite", request.Type);
        Assert.Throws<ArgumentException>(() => new JobRequest("Convite por e-mail", Id, new JobPayload(), OneHour));
        Assert.Throws<ArgumentException>(() => new JobRequest("email." + new string('a', 60), Id, new JobPayload(), OneHour));
        Assert.Throws<ArgumentOutOfRangeException>(() => new JobRequest("email.admin-invite", Id, new JobPayload(), TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new JobRequest("email.admin-invite", Id, new JobPayload(), OneHour, "convite:responsavel@example.org"));
        Assert.Throws<ArgumentException>(() => new JobRequest("email.admin-invite", Id, new JobPayload(), OneHour, new string('k', 201)));
        Assert.Throws<ArgumentException>(() => new JobRequest("email.admin-invite\n", Id, new JobPayload(), OneHour));
        Assert.Throws<ArgumentException>(() => new JobRequest("email.admin-invite", Id, new JobPayload(), OneHour, "chave\n"));
    }
}
