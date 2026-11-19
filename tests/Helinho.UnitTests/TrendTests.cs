using Helinho.Application.Painel;

namespace Helinho.UnitTests;

public class TrendTests
{
    [Theory]
    [InlineData(11, 10, TrendDirection.Up, "+10%")]
    [InlineData(0, 10, TrendDirection.Down, "−100%")]
    [InlineData(1004, 1000, TrendDirection.Flat, Trend.Stable)] // arredonda para 0%: seta e texto dizem a mesma coisa
    [InlineData(5, 0, TrendDirection.None, Trend.NoBaseline)]
    [InlineData(0, 0, TrendDirection.None, Trend.NoBaseline)]
    public void Variacao_de_contagem(int current, int previous, TrendDirection direction, string text) =>
        Assert.Equal(new Trend(direction, text), Trend.OfCount(current, previous));

    [Theory]
    [InlineData(51, 50, TrendDirection.Up, "+1 pt")]
    [InlineData(49, 50, TrendDirection.Down, "−1 pt")]
    [InlineData(50, 50, TrendDirection.Flat, Trend.Stable)]
    [InlineData(null, 50, TrendDirection.None, Trend.NoData)]
    [InlineData(null, null, TrendDirection.None, Trend.NoData)]
    [InlineData(50, null, TrendDirection.None, Trend.NoBaseline)]
    public void Variacao_de_taxa_em_pontos(int? current, int? previous, TrendDirection direction, string text) =>
        Assert.Equal(new Trend(direction, text), Trend.OfPoints(current, previous));
}
