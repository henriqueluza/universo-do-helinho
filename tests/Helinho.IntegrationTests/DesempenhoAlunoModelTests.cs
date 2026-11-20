using Helinho.Web.Pages.Desempenho;

namespace Helinho.IntegrationTests;

/// <summary>Helpers de apresentação da ficha do aluno (Issue #153), sem banco. Nomes e valores sintéticos.</summary>
public class DesempenhoAlunoModelTests
{
    [Theory]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    [InlineData("ana", "A")]
    [InlineData("Ana Clara Souza", "AS")]
    [InlineData("  bruno   lima  ", "BL")]
    [InlineData("élio ávila", "ÉÁ")]
    [InlineData("élio Souza", "ÉS")] // acento combinado: a inicial é o grafema inteiro
    [InlineData("\U0001D49Cna Lima", "\U0001D49CL")] // par substituto UTF-16 não é partido ao meio
    public void Initials_usa_o_primeiro_grafema_do_primeiro_e_do_ultimo_nome(string name, string expected) =>
        Assert.Equal(expected, AlunoModel.Initials(name));

    [Theory]
    [InlineData(350, 600, 58)]
    [InlineData(0, 600, 0)]
    [InlineData(900, 600, 100)] // XP acima do limiar não estoura a barra
    [InlineData(-10, 600, 0)]
    [InlineData(10, 0, 0)] // sem próximo nível: barra vazia, sem divisão por zero
    [InlineData(10, -5, 0)]
    public void XpPercent_fica_entre_0_e_100(int xp, int nextLevelXp, int expected) =>
        Assert.Equal(expected, AlunoModel.XpPercent(xp, nextLevelXp));

    [Theory]
    [InlineData(2, 1, 50)]
    [InlineData(3, 1, 33)]
    [InlineData(3, 3, 100)]
    [InlineData(0, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(2, 5, 100)]
    public void CorrectPercent_fica_entre_0_e_100(int answered, int correct, int expected) =>
        Assert.Equal(expected, AlunoModel.CorrectPercent(answered, correct));

    [Theory]
    [InlineData("Cem perguntas", "/img/selo-cem-perguntas.webp", 109)]
    [InlineData("amigo da natureza", "/img/selo-amigo-da-natureza.webp", 101)] // título sem diferenciar maiúsculas
    [InlineData("Curioso do mês", "/img/selo-curioso-do-mes.webp", 101)]
    [InlineData("Leitor da Lia", "/img/selo-leitor-da-lia.webp", 101)]
    public void BadgeImage_casa_o_titulo_com_a_arte_entregue(string title, string src, int height) =>
        Assert.Equal((src, height), AlunoModel.BadgeImage(title));

    [Theory]
    [InlineData("Explorador do Bosque")]
    [InlineData("")]
    public void BadgeImage_sem_arte_devolve_null(string title) => Assert.Null(AlunoModel.BadgeImage(title));

    [Fact]
    public void UnlockedOn_mostra_a_data_no_fuso_de_brasilia()
    {
        Assert.Equal("09/11/2026", AlunoModel.UnlockedOn(new DateTimeOffset(2026, 11, 10, 2, 0, 0, TimeSpan.Zero)));
        Assert.Equal("10/11/2026", AlunoModel.UnlockedOn(new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero)));
    }
}
