using Helinho.Application.Administradores;

namespace Helinho.UnitTests;

/// <summary>Situações da lista de administradores (ADR-0003 §11.2). Contas sintéticas.</summary>
public class ConviteAdministradorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private static AdminAccount Conta(bool comSenha = false, DateTimeOffset? bloqueio = null) =>
        new(Guid.NewGuid(), "adm.sintetico", "adm.sintetico@example.com", Guid.NewGuid(), bloqueio, comSenha);

    [Theory]
    [InlineData(false, null, AdminSituacao.AguardandoEnvio)]
    [InlineData(false, 71, AdminSituacao.ConviteEnviado)]
    [InlineData(false, 72, AdminSituacao.ConviteExpirado)]
    [InlineData(true, null, AdminSituacao.NaoEntregue)]
    public void Conta_sem_senha_segue_o_convite_mais_recente(bool falhou, int? horasDesdeOEnvio, AdminSituacao esperada)
    {
        var invite = new InviteJob(falhou, horasDesdeOEnvio is { } horas ? Now.AddHours(-horas) : null);

        Assert.Equal(esperada, ConviteAdministrador.Situacao(Conta(), invite, Now));
    }

    [Fact]
    public void Sem_registro_na_fila_e_expirado_com_senha_e_ativo_e_o_bloqueio_vigente_vence_qualquer_convite()
    {
        var enviado = new InviteJob(false, Now.AddHours(-1));

        Assert.Equal(AdminSituacao.ConviteExpirado, ConviteAdministrador.Situacao(Conta(), null, Now));
        Assert.Equal(AdminSituacao.Ativo, ConviteAdministrador.Situacao(Conta(comSenha: true), enviado, Now));
        Assert.Equal(AdminSituacao.Inativo, ConviteAdministrador.Situacao(Conta(comSenha: true, bloqueio: Now.AddHours(1)), null, Now));
        Assert.Equal(AdminSituacao.Inativo, ConviteAdministrador.Situacao(Conta(bloqueio: Now.AddHours(1)), enviado, Now));
    }
}
