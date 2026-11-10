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

    public static ErrorResponse CreateInvalidCredentials() => new(InvalidCredentials, "Credencial inválida.");

    public static ErrorResponse CreateTokenExpired() => new(TokenExpired, "Sessão vencida ou ausente. Entre de novo.");

    public static ErrorResponse CreateForbidden() => new(Forbidden, "Sem permissão para esta operação.");

    public static ErrorResponse CreateNotImplemented() => new(NotImplemented, "Rota ainda não implementada.");
}
