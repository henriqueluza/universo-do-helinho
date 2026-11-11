using Helinho.Application.LiveEvents;

namespace Helinho.UnitTests;

public class BrasiliaTimeTests
{
    [Fact]
    public void Converte_Brasilia_para_UTC_e_volta()
    {
        var utc = BrasiliaTime.ToUtc(new DateTime(2090, 6, 15, 10, 0, 0));

        Assert.Equal(new DateTimeOffset(2090, 6, 15, 13, 0, 0, TimeSpan.Zero), utc);
        Assert.Equal(new DateTime(2090, 6, 15, 10, 0, 0), BrasiliaTime.ToLocal(utc));
    }
}
