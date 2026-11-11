using Helinho.Domain;

namespace Helinho.UnitTests;

public class CpfTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    public void Aceita_cpf_valido_com_ou_sem_mascara(string cpf) => Assert.True(Cpf.IsValid(cpf));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("529.982.247-24")]
    [InlineData("111.111.111-11")]
    [InlineData("1234567890")]
    [InlineData("529.982.247-255")]
    public void Rejeita_cpf_invalido(string? cpf) => Assert.False(Cpf.IsValid(cpf));

    [Fact]
    public void Formata_no_padrao() => Assert.Equal("529.982.247-25", Cpf.Format("52998224725"));
}
