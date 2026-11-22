namespace Helinho.Infrastructure.Seeding;

/// <summary>Seção <c>Seed</c> da configuração (variável <c>Seed__Enabled</c>). Desligado por padrão: produção não recebe dados sintéticos.</summary>
public sealed class SeedOptions
{
    public const string Section = "Seed";

    public bool Enabled { get; set; }
}
