namespace Helinho.Application.Abstractions;

/// <summary>Semeador de dados sintéticos; executado em ordem crescente de <see cref="Order"/> pelo comando seed.</summary>
public interface ISeeder
{
    int Order { get; }

    Task SeedAsync(CancellationToken cancellationToken);
}
