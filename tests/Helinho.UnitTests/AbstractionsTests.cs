using Helinho.Application.Abstractions;
using Helinho.Infrastructure.DependencyInjection;
using Helinho.Infrastructure.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.UnitTests;

public class AbstractionsTests
{
    [Fact]
    public void Usuario_anonimo_nao_esta_autenticado_nem_tem_papel()
    {
        var user = new AnonymousCurrentUser();

        Assert.False(user.IsAuthenticated);
        Assert.Null(user.UserId);
        Assert.False(user.IsInRole("Master"));
    }

    [Fact]
    public void Relogio_devolve_hora_utc()
    {
        var before = DateTimeOffset.UtcNow;

        var now = new SystemClock().UtcNow;

        Assert.InRange(now, before, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.Zero, now.Offset);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Issue #297: o processador da fila usa TimeProvider e os casos de uso usam IClock; a fonte é uma só.</summary>
    [Fact]
    public void Relogio_registrado_le_a_hora_do_TimeProvider_do_host()
    {
        var instant = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        using var replaced = new ServiceCollection().AddInfrastructureServices().AddSingleton<TimeProvider>(new FixedTime(instant)).BuildServiceProvider();
        using var standard = new ServiceCollection().AddInfrastructureServices().BuildServiceProvider();
        var before = TimeProvider.System.GetUtcNow();

        Assert.Equal(instant, replaced.GetRequiredService<IClock>().UtcNow);
        Assert.InRange(standard.GetRequiredService<IClock>().UtcNow, before, TimeProvider.System.GetUtcNow()); // sem registro: a hora do sistema
    }
}
