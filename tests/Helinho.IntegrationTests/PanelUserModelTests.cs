using Helinho.Web.Panel;

namespace Helinho.IntegrationTests;

/// <summary>Iniciais do avatar no bloco do usuário da casca (Issue #140). Nomes sintéticos.</summary>
public class PanelUserModelTests
{
    [Theory]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    [InlineData("ana", "A")]
    [InlineData("admin.norte", "A")]
    [InlineData("Ana Clara Souza", "AS")]
    [InlineData("  bruno   lima  ", "BL")]
    [InlineData("1ª Turma Norte", "1N")]
    [InlineData("élio ávila", "ÉÁ")]
    public void Initials_usa_a_primeira_letra_do_primeiro_e_do_ultimo_nome(string name, string expected)
    {
        var model = new PanelUserModel(name, "Master", "Todas as unidades", []);

        Assert.Equal(expected, model.Initials);
    }
}
