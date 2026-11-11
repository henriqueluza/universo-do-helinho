namespace Helinho.Application.Contracts;

/// <summary>Corpo de erro padrão (P4). Vale para todas as rotas.</summary>
public sealed record ErrorResponse(string Error, string Message);

/// <summary>Códigos de erro do contrato (P4), num único lugar.</summary>
public static class ApiErrors
{
    public const string InvalidCredentials = "invalid_credentials";
    public const string TokenExpired = "token_expired";
    public const string Forbidden = "forbidden";
    public const string NotImplemented = "not_implemented";
    public const string InvalidQuizType = "invalid_quiz_type";
    public const string NoQuestions = "no_questions";
    public const string InvalidInventory = "invalid_inventory";
    public const string InvalidPushToken = "invalid_push_token";

    public static ErrorResponse CreateInvalidCredentials() => new(InvalidCredentials, "Credencial inválida.");

    public static ErrorResponse CreateTokenExpired() => new(TokenExpired, "Sessão vencida ou ausente. Entre de novo.");

    public static ErrorResponse CreateForbidden() => new(Forbidden, "Sem permissão para esta operação.");

    public static ErrorResponse CreateNotImplemented() => new(NotImplemented, "Rota ainda não implementada.");

    public static ErrorResponse CreateInvalidQuizType() =>
        new(InvalidQuizType, "quiz_type obrigatório: ambiental, conhecimento, esportivo, cultural ou diversidade.");

    public static ErrorResponse CreateNoQuestions() => new(NoQuestions, "Sem perguntas disponíveis para este quiz.");

    public static ErrorResponse CreateInvalidInventory() => new(
        InvalidInventory, "Seleção inválida: no máximo um item por type (cabelo, pele, roupa, rosto, acessorio), com id e name.");

    public static ErrorResponse CreateInvalidPushToken() => new(
        InvalidPushToken, "token obrigatório (até 512 caracteres) e platform android ou ios.");
}
