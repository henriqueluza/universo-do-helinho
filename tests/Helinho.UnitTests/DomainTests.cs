using Helinho.Domain;
using Helinho.Domain.Entities;

namespace Helinho.UnitTests;

public class DomainTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    private static Student NewStudent(DateOnly birth, string rg = "00.000.000-0") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "M001", "Aluno Fictício", rg, "000.000.000-00", "Rua Sintética, 1", birth, "Responsável Fictício", "5A", Today);

    private static (string, bool)[] Options(int correct) =>
        [("A", correct == 0), ("B", correct == 1), ("C", correct == 2), ("D", correct == 3)];

    [Theory]
    [InlineData(2020, 11, 10, AgeBand.Age6To7)]
    [InlineData(2019, 11, 11, AgeBand.Age6To7)] // ainda 6 (faz 7 amanhã)
    [InlineData(2018, 11, 10, AgeBand.Age8To10)]
    [InlineData(2015, 11, 10, AgeBand.Age11To12)]
    [InlineData(2013, 11, 10, AgeBand.Age13To14)]
    [InlineData(2012, 11, 11, AgeBand.Age13To14)] // ainda 13
    public void Faixa_etaria_deriva_do_nascimento(int y, int m, int d, AgeBand expected) =>
        Assert.Equal(expected, AgeBands.FromBirthDate(new DateOnly(y, m, d), Today));

    [Theory]
    [InlineData(2021, 1, 1)]
    [InlineData(2011, 11, 10)]
    public void Idade_fora_de_6_a_14_e_rejeitada(int y, int m, int d) =>
        Assert.Throws<DomainException>(() => NewStudent(new DateOnly(y, m, d)));

    [Fact]
    public void Aluno_exige_campos_cadastrais() =>
        Assert.Throws<DomainException>(() => NewStudent(new DateOnly(2016, 1, 1), rg: " "));

    [Fact]
    public void Aluno_nasce_sem_aprovacao_do_responsavel()
    {
        var student = NewStudent(new DateOnly(2016, 1, 1));

        Assert.False(student.GuardianApproved);
        student.ApproveByGuardian();
        Assert.True(student.GuardianApproved);
        Assert.Equal(AgeBand.Age8To10, student.AgeBandAt(Today));
    }

    [Fact]
    public void Questao_tem_quatro_alternativas_com_chaves_a_a_d_e_uma_correta()
    {
        var q = new Question(QuizType.Ambiental, AgeBand.Age8To10, "Enunciado", "Explicação", Options(1));

        Assert.Equal(["a", "b", "c", "d"], q.Options.Select(o => o.Key));
        Assert.Equal("b", Assert.Single(q.Options, o => o.IsCorrect).Key);
        Assert.Equal(QuestionStatus.Draft, q.Status);
    }

    [Fact]
    public void Questao_rejeita_numero_errado_de_alternativas_ou_corretas()
    {
        Assert.Throws<DomainException>(() => new Question(QuizType.Cultural, AgeBand.Age6To7, "E", "X", Options(0)[..3]));
        Assert.Throws<DomainException>(() => new Question(QuizType.Cultural, AgeBand.Age6To7, "E", "X", [("A", true), ("B", true), ("C", false), ("D", false)]));
        Assert.Throws<DomainException>(() => new Question(QuizType.Cultural, AgeBand.Age6To7, "E", "X", [("A", false), ("B", false), ("C", false), ("D", false)]));
    }

    [Fact]
    public void Quiz_tem_os_cinco_tipos_do_contrato() =>
        Assert.Equal(["Ambiental", "Conhecimento", "Esportivo", "Cultural", "Diversidade"], Enum.GetNames<QuizType>());

    [Theory]
    [InlineData(0, 1, 100)]
    [InlineData(99, 1, 100)]
    [InlineData(100, 2, 300)]
    [InlineData(320, 3, 600)]
    public void Nivel_e_limiar_do_proximo_nivel(int xp, int level, int next)
    {
        Assert.Equal(level, LevelPolicy.LevelFor(xp));
        Assert.Equal(next, LevelPolicy.NextLevelXp(xp));
    }

    [Theory]
    [InlineData(1, "Novato")]
    [InlineData(3, "Explorador")]
    [InlineData(7, "Aventureiro")]
    [InlineData(12, "Mestre")]
    public void Nome_do_nivel(int level, string name) => Assert.Equal(name, LevelPolicy.LevelName(level));

    [Fact]
    public void Sessao_da_api_expira_e_e_revogada()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new ApiSession(Guid.NewGuid(), "hash", now.AddHours(8));

        Assert.True(session.IsActive(now));
        Assert.False(session.IsActive(now.AddHours(8)));
        session.Revoke(now);
        Assert.False(session.IsActive(now));
    }
}
