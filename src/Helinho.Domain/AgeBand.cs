namespace Helinho.Domain;

/// <summary>Faixas etárias do produto. Não há aluno fora delas.</summary>
public enum AgeBand
{
    Age6To7,
    Age8To10,
    Age11To12,
    Age13To14,
}

public static class AgeBands
{
    /// <summary>Faixa do aluno em <paramref name="today"/>; lança se a idade estiver fora de 6–14.</summary>
    public static AgeBand FromBirthDate(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year - (today < birthDate.AddYears(today.Year - birthDate.Year) ? 1 : 0);
        return age switch
        {
            >= 6 and <= 7 => AgeBand.Age6To7,
            >= 8 and <= 10 => AgeBand.Age8To10,
            >= 11 and <= 12 => AgeBand.Age11To12,
            >= 13 and <= 14 => AgeBand.Age13To14,
            _ => throw new DomainException($"Idade {age} fora das faixas 6–14."),
        };
    }
}
