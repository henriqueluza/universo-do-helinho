using Helinho.Infrastructure.Fakes;

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
}
