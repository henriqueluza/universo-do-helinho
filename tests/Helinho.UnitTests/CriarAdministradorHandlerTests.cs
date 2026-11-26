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
    private readonly CriarAdministradorHandler _handler;

    public CriarAdministradorHandlerTests()
    {
        _gateway = new FakeGateway(_unit);
        _handler = new CriarAdministradorHandler(_gateway, new FixedClock());
    }

    private NewAdminAccount Account(string user = "adm.norte") => new(user, user + "@example.test", _unit.Id, "Segredo-Inicial9x");

    [Fact]
    public async Task Cria_administrador_quando_a_unidade_nao_tem_ativo()
    {
        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal(CriarAdministradorStatus.Created, result.Status);
        Assert.Single(_gateway.Admins);
        Assert.True(_gateway.Committed);
    }

    [Fact]
    public async Task Recusa_segundo_administrador_ativo_na_mesma_unidade()
    {
        _gateway.Admins.Add(new AdminAccount("existente", "e@example.test", _unit.Id, null));

        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal(CriarAdministradorStatus.UnitHasActiveAdmin, result.Status);
        Assert.Single(_gateway.Admins);
        Assert.False(_gateway.Committed);
    }

    [Fact]
    public async Task Administrador_com_bloqueio_vigente_nao_conta_como_ativo()
    {
        _gateway.Admins.Add(new AdminAccount("bloqueado", "b@example.test", _unit.Id, Now.AddHours(1)));

        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal(CriarAdministradorStatus.Created, result.Status);
    }

    [Fact]
    public async Task Bloqueio_vencido_conta_como_ativo()
    {
        _gateway.Admins.Add(new AdminAccount("vencido", "v@example.test", _unit.Id, Now.AddMinutes(-1)));

        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal(CriarAdministradorStatus.UnitHasActiveAdmin, result.Status);
    }

    [Fact]
    public async Task Admin_ativo_de_outra_unidade_nao_bloqueia()
    {
        _gateway.Admins.Add(new AdminAccount("outro", "o@example.test", Guid.NewGuid(), null));

        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal(CriarAdministradorStatus.Created, result.Status);
    }

    [Fact]
    public async Task Recusa_e_mail_ja_usado_por_outra_conta_mesmo_de_outra_unidade()
    {
        _gateway.Admins.Add(new AdminAccount("outro", "ADM.NORTE@example.test", Guid.NewGuid(), null));

        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal(CriarAdministradorStatus.EmailInUse, result.Status);
        Assert.Single(_gateway.Admins);
        Assert.False(_gateway.Committed);
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
        _gateway.CreateErrors = ["PasswordRequiresNonAlphanumeric"];

        var result = await _handler.HandleAsync(Account(), Ct);

        Assert.Equal(CriarAdministradorStatus.Rejected, result.Status);
        Assert.Equal(["PasswordRequiresNonAlphanumeric"], result.ErrorCodes);
        Assert.False(_gateway.Committed);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeGateway(Unit unit) : IAdminAccountGateway
    {
        public List<AdminAccount> Admins { get; } = [];

        public IReadOnlyList<string> CreateErrors { get; set; } = [];

        public bool LockOpened { get; private set; }

        public bool Committed { get; private set; }

        public Task<IReadOnlyList<Unit>> ListUnitsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Unit>>([unit]);

        public Task<IReadOnlyList<AdminAccount>> ListAdminsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdminAccount>>(Admins.ToList());

        public Task<bool> EmailInUseAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(LockOpened && Admins.Exists(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase)));

        public Task<IAdminUnitLock> BeginUnitLockAsync(Guid unitId, CancellationToken cancellationToken)
        {
            LockOpened = true;
            return Task.FromResult<IAdminUnitLock>(new Lock(this));
        }

        public Task<IReadOnlyList<string>> CreateAdminAsync(NewAdminAccount account, CancellationToken cancellationToken)
        {
            if (CreateErrors.Count == 0)
            {
                Admins.Add(new AdminAccount(account.UserName, account.Email, account.UnitId, null));
            }

            return Task.FromResult(CreateErrors);
        }

        private sealed class Lock(FakeGateway owner) : IAdminUnitLock
        {
            public Task CommitAsync(CancellationToken cancellationToken)
            {
                owner.Committed = true;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
