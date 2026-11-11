using Helinho.Application.Contracts;

namespace Helinho.Web.Api;

public static class ApiResults
{
    /// <summary>Resposta padrão das rotas ainda sem lógica: 501 com o corpo de erro do contrato.</summary>
    public static IResult NotImplemented() =>
        Results.Json(ApiErrors.CreateNotImplemented(), statusCode: StatusCodes.Status501NotImplemented);
}
