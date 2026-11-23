using Helinho.Application.Abstractions;
using Helinho.Application.Administradores;
using Helinho.Domain.Entities;

namespace Helinho.UnitTests;

public class CriarAdministradorHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly Unit _unit = new("Unidade Norte");
    private readonly FakeGateway _gateway;
    private readonly FakeQueue _queue = new();
    private readonly CriarAdministradorHandler _handler;

    public CriarAdministradorHandlerTests()
    {
        _gateway = _queue.Gateway = new FakeGateway(_unit);
        _handler = new CriarAdministradorHandler(_gateway, _queue, new FixedClock());
    }

    private NewAdminAccount Account(string user = "adm.norte") => new(user, user + "@example.test", _unit.Id);

    private static AdminAccount Existing(string user, Guid? unitId, DateTimeOffset? lockoutEnd = null) =>
        new(Guid.NewGuid(), user, user + "@example.test", unitId, lockoutEnd, HasPassword: true);

    [Fact]
    public async Task Cria_administrador_sem_senha_e_enfileira_o_convite_antes_do_commit()
    {
        var account = Account();

        var result = await _handler.HandleAsync(account, Ct);

        Assert.Equal(CriarAdministradorStatus.Created, result.Status);
        Assert.False(Assert.Single(_gateway.Admins).HasPassword);
        Assert.True(_gateway.Committed);
        var (job, committedBefore) = Assert.Single(_queue.Requests);
        Assert.False(committedBefore); // §5.5: o convite entra na transação da conta
        Assert.Equal(("email.admin-invite", account.Id, TimeSpan.FromHours(24)), (job.Type, job.SubjectId, job.ExpiresIn));
        Assert.Equal("""{"v":1}""", job.Payload.ToJson()); // só referências: sem e-mail, nome, token ou link
        Assert.Equal($"email.admin-invite:{account.Id:D}:{Now.ToUnixTimeSeconds() / 120}", job.IdempotencyKey);
    }

    [Fact]
    public async Task Email_ja_usado_por_conta_do_painel_e_recusado_sem_criar_nem_enfileirar()
    {
        _gateway.PanelEmails.Add("ADM.NORTE@example.test");

        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal((CriarAdministradorStatus.Rejected, "DuplicateEmail"), (result.Status, Assert.Single(result.ErrorCodes)));
        Assert.Empty(_gateway.Admins);
        Assert.Empty(_queue.Requests);
        Assert.False(_gateway.Committed);
    }

    [Fact]
    public async Task Recusa_segundo_administrador_ativo_na_mesma_unidade()
    {
        _gateway.Admins.Add(Existing("existente", _unit.Id));

        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal(CriarAdministradorStatus.UnitHasActiveAdmin, result.Status);
        Assert.Single(_gateway.Admins);
        Assert.Empty(_queue.Requests);
        Assert.False(_gateway.Committed);
    }

    [Fact]
    public async Task Administrador_com_bloqueio_vigente_nao_conta_como_ativo()
    {
        _gateway.Admins.Add(Existing("bloqueado", _unit.Id, Now.AddHours(1)));

        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal(CriarAdministradorStatus.Created, result.Status);
    }

    [Fact]
    public async Task Bloqueio_vencido_conta_como_ativo()
    {
        _gateway.Admins.Add(Existing("vencido", _unit.Id, Now.AddMinutes(-1)));

        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal(CriarAdministradorStatus.UnitHasActiveAdmin, result.Status);
    }

    [Fact]
    public async Task Admin_ativo_de_outra_unidade_nao_bloqueia()
    {
        _gateway.Admins.Add(Existing("outro", Guid.NewGuid()));

        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal(CriarAdministradorStatus.Created, result.Status);
    }

    [Fact]
    public async Task Unidade_inexistente_e_recusada_sem_abrir_lock()
    {
        var result = await _handler.HandleAsync(Account() with { UnitId = Guid.NewGuid() }, Ct);

        Assert.Equal(CriarAdministradorStatus.InvalidUnit, result.Status);
        Assert.False(_gateway.LockOpened);
    }

    [Fact]
    public async Task Erro_do_identity_desfaz_e_devolve_os_codigos()
    {
        _gateway.CreateErrors = ["DuplicateUserName"];

        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal(CriarAdministradorStatus.Rejected, result.Status);
        Assert.Equal(["DuplicateUserName"], result.ErrorCodes);
        Assert.Empty(_queue.Requests);
        Assert.False(_gateway.Committed);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    internal sealed class FakeQueue : IJobQueue
    {
        public List<Guid> Cancelled { get; } = [];

        public FakeGateway? Gateway { get; set; }

        public List<(JobRequest Job, bool CommittedBefore)> Requests { get; } = [];

        public Task<Guid?> EnqueueAsync(JobRequest request, CancellationToken cancellationToken)
        {
            Requests.Add((request, Gateway!.Committed));
            return Task.FromResult<Guid?>(Guid.NewGuid());
        }

        public Task<int> CancelPendingAsync(string type, Guid subjectId, CancellationToken cancellationToken)
        {
            Cancelled.Add(subjectId);
            return Task.FromResult(1);
        }
    }

    internal sealed class FakeGateway(Unit unit) : IAdminAccountGateway
    {
        public List<Guid> Rotated { get; } = [];

        /// <summary>Simula a conta que sumiu ou ganhou senha depois de lida.</summary>
        public bool AccountChanged { get; set; }

        public Exception? CommitError { get; set; }

        public List<AdminAccount> Admins { get; } = [];

        public HashSet<string> PanelEmails { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> CreateErrors { get; set; } = [];

        public bool LockOpened { get; private set; }

        public bool Committed { get; private set; }

        public Task<IReadOnlyList<Unit>> ListUnitsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Unit>>([unit]);

        public Task<IReadOnlyList<AdminAccount>> ListAdminsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdminAccount>>(Admins.ToList());

        public Task<IAdminUnitLock> BeginUnitLockAsync(Guid unitId, CancellationToken cancellationToken)
        {
            LockOpened = true;
            return Task.FromResult<IAdminUnitLock>(new Lock(this));
        }

        public Task<AdminAccount?> FindAdminAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult(Admins.Find(a => a.Id == accountId));

        public Task<bool> RotateSecurityStampAsync(Guid accountId, CancellationToken cancellationToken)
        {
            Rotated.Add(accountId);
            return Task.FromResult(!AccountChanged);
        }

        public Task<bool> DeleteInvitedAdminAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult(!AccountChanged && Admins.RemoveAll(a => a.Id == accountId) == 1);

        public Task<bool> PanelEmailInUseAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(PanelEmails.Contains(email));

        public Task<IReadOnlyList<string>> CreateAdminAsync(NewAdminAccount account, CancellationToken cancellationToken)
        {
            if (CreateErrors.Count == 0)
            {
                Admins.Add(new AdminAccount(account.Id, account.UserName, account.Email, account.UnitId, null, HasPassword: false));
            }

            return Task.FromResult(CreateErrors);
        }

        private sealed class Lock(FakeGateway owner) : IAdminUnitLock
        {
            public Task CommitAsync(CancellationToken cancellationToken)
            {
                owner.Committed = owner.CommitError is null;
                return owner.CommitError is null ? Task.CompletedTask : Task.FromException(owner.CommitError);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
