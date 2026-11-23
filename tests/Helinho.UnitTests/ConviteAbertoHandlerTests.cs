using Helinho.Application.Abstractions;
using Helinho.Application.Administradores;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using FakeGateway = Helinho.UnitTests.CriarAdministradorHandlerTests.FakeGateway;
using FakeQueue = Helinho.UnitTests.CriarAdministradorHandlerTests.FakeQueue;

namespace Helinho.UnitTests;

/// <summary>Reenviar e cancelar convite (ADR-0003 §11.2, §10.2 e §11.6). Contas sintéticas.</summary>
public class ConviteAbertoHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly FakeGateway _gateway = new(new Unit("Unidade Norte"));
    private readonly FakeQueue _queue = new();
    private readonly FakeInvites _invites = new();
    private readonly ConviteAbertoHandler _handler;

    public ConviteAbertoHandlerTests()
    {
        _queue.Gateway = _gateway;
        _handler = new ConviteAbertoHandler(_gateway, _invites, _queue, new FixedClock());
    }

    private Guid Conta(bool comSenha = false, DateTimeOffset? bloqueio = null)
    {
        var account = new AdminAccount(Guid.NewGuid(), "adm.norte", "adm.norte@example.com", Guid.NewGuid(), bloqueio, comSenha);
        _gateway.Admins.Add(account);
        return account.Id;
    }

    private void AssertNadaMudou()
    {
        Assert.Empty(_queue.Requests);
        Assert.Empty(_queue.Cancelled);
        Assert.False(_gateway.Committed);
    }

    [Fact]
    public async Task Reenviar_troca_o_carimbo_cancela_os_pendentes_e_enfileira_o_novo_convite_na_mesma_transacao()
    {
        var id = Conta();
        _invites.Count = ConviteAbertoHandler.LimitePorHora - 1;

        Assert.Equal(ConviteStatus.Done, await _handler.ReenviarAsync(id, Ct));

        Assert.Equal([id], _gateway.Rotated);
        Assert.Equal([id], _queue.Cancelled);
        var (job, committedBefore) = Assert.Single(_queue.Requests);
        Assert.False(committedBefore);
        Assert.Equal(("email.admin-invite", id, """{"v":1}"""), (job.Type, job.SubjectId, job.Payload.ToJson()));
        Assert.Equal($"email.admin-invite:{id:D}:{Now.ToUnixTimeSeconds() / 120}", job.IdempotencyKey);
        Assert.Equal((id, Now.AddHours(-1)), _invites.Asked); // janela de uma hora
        Assert.True(_gateway.Committed);
    }

    [Fact]
    public async Task Reenviar_no_limite_de_tres_por_hora_nao_muda_nada()
    {
        var id = Conta();
        _invites.Count = ConviteAbertoHandler.LimitePorHora;

        Assert.Equal(ConviteStatus.RateLimited, await _handler.ReenviarAsync(id, Ct));

        Assert.Empty(_gateway.Rotated);
        AssertNadaMudou();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Sem_convite_em_aberto_nada_e_reenviado_nem_cancelado(bool comSenha, bool bloqueada, bool inexistente)
    {
        var id = inexistente ? Guid.NewGuid() : Conta(comSenha, bloqueada ? Now.AddHours(1) : null);

        Assert.Equal(ConviteStatus.NotInvited, await _handler.ReenviarAsync(id, Ct));
        Assert.Equal(ConviteStatus.NotInvited, await _handler.CancelarAsync(id, Ct));
        Assert.Null(await _handler.AbertoAsync(id, Ct));

        Assert.Empty(_gateway.Rotated);
        Assert.Equal(inexistente ? 0 : 1, _gateway.Admins.Count);
        AssertNadaMudou();
    }

    [Fact]
    public async Task Conta_que_mudou_depois_de_lida_nao_recebe_convite_nem_e_apagada()
    {
        var id = Conta();
        _gateway.AccountChanged = true;

        Assert.Equal(ConviteStatus.NotInvited, await _handler.ReenviarAsync(id, Ct));
        Assert.Equal(ConviteStatus.NotInvited, await _handler.CancelarAsync(id, Ct));

        Assert.Single(_gateway.Admins);
        AssertNadaMudou();
    }

    [Fact]
    public async Task Cancelar_apaga_a_conta_e_cancela_os_convites_pendentes()
    {
        var id = Conta();

        Assert.Equal(ConviteStatus.Done, await _handler.CancelarAsync(id, Ct));

        Assert.Empty(_gateway.Admins);
        Assert.Equal([id], _queue.Cancelled);
        Assert.Empty(_queue.Requests);
        Assert.True(_gateway.Committed);
    }

    [Fact]
    public async Task Convite_pego_pelo_trabalhador_no_meio_da_operacao_nao_vira_sucesso()
    {
        var id = Conta();
        _gateway.CommitError = new DbUpdateConcurrencyException();

        Assert.Equal(ConviteStatus.SendingNow, await _handler.ReenviarAsync(id, Ct));
        Assert.Equal(ConviteStatus.SendingNow, await _handler.CancelarAsync(Conta(), Ct));

        Assert.False(_gateway.Committed);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeInvites : IAdminInviteJobs
    {
        public int Count { get; set; }

        public (Guid AccountId, DateTimeOffset Since)? Asked { get; private set; }

        public Task<IReadOnlyDictionary<Guid, InviteJob>> LatestAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, InviteJob>>(new Dictionary<Guid, InviteJob>());

        public Task<int> CountSinceAsync(Guid accountId, DateTimeOffset since, CancellationToken cancellationToken)
        {
            Asked = (accountId, since);
            return Task.FromResult(Count);
        }
    }
}
