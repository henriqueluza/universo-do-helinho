using Helinho.Infrastructure.Email;

namespace Helinho.UnitTests;

/// <summary>Links dos e-mails (#205, ADR-0003 §10.3): só da base configurada, em https, montados com Uri.</summary>
public class PublicBaseUrlTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly PublicBaseUrl Base = PublicBaseUrl.Parse("https://painel.example.org", allowHttpLoopback: false);

    [Theory]
    [InlineData("http://painel.example.org")]
    [InlineData("https://painel.example.org/app")]
    [InlineData("https://painel.example.org/?x=1")]
    [InlineData("https://painel.example.org/#topo")]
    [InlineData("https://usuario@painel.example.org")]
    [InlineData("https://painel.example.org@evil.example")]
    [InlineData("//painel.example.org")]
    [InlineData("painel.example.org")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData(null)]
    public void Base_que_nao_e_https_absoluta_sem_usuario_nem_caminho_e_recusada_sem_ecoar_o_valor(string? value)
    {
        var error = Assert.Throws<ArgumentException>(() => PublicBaseUrl.Parse(value, allowHttpLoopback: false));

        Assert.DoesNotContain("example", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Http_so_vale_para_localhost_e_so_em_desenvolvimento()
    {
        Assert.Equal("http://localhost:5080", PublicBaseUrl.Parse("http://localhost:5080", allowHttpLoopback: true).Origin);
        Assert.Throws<ArgumentException>(() => PublicBaseUrl.Parse("http://localhost:5080", allowHttpLoopback: false));
        Assert.Throws<ArgumentException>(() => PublicBaseUrl.Parse("http://painel.example.org", allowHttpLoopback: true));
    }

    [Theory]
    [InlineData("//evil.example/conta")]
    [InlineData("/@evil.example")]
    [InlineData("@evil.example")]
    [InlineData("/conta//definir-senha")]
    [InlineData("/conta/../admin")]
    [InlineData("\\\\evil.example")]
    [InlineData("/conta?x=1")]
    [InlineData("/conta\n")]
    [InlineData("https://evil.example/conta")]
    [InlineData("")]
    public void Caminho_que_poderia_trocar_o_host_e_recusado(string path)
    {
        Assert.Throws<ArgumentException>(() => Base.Link(path));
    }

    [Fact]
    public void Link_de_definir_senha_fica_na_base_e_escapa_cada_parte()
    {
        var link = Base.SetPasswordLink(UserId, "a b&c=d/+#@evil.example");

        Assert.Equal("https://painel.example.org", Base.Origin);
        Assert.Equal(
            "https://painel.example.org/conta/definir-senha?usuario=11111111-2222-3333-4444-555555555555&token=a%20b%26c%3Dd%2F%2B%23%40evil.example",
            link.AbsoluteUri);
        Assert.Equal("https://painel.example.org/img/fundhas.png", Base.Link("/img/fundhas.png").AbsoluteUri);
        Assert.Throws<ArgumentException>(() => Base.SetPasswordLink(UserId, " "));
    }
}
