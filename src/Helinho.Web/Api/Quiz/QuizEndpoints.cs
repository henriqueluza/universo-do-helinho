using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Application.Quizzes;
using Microsoft.AspNetCore.Mvc;

namespace Helinho.Web.Api.Quiz;

public static class QuizEndpoints
{
    public static RouteGroupBuilder MapQuizEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet(
                "/quiz/random",
                async (
                    [FromQuery(Name = "quiz_type")] string? quizType, IAppDbContext db, ICurrentStudent current, IClock clock,
                    CancellationToken ct) =>
                {
                    var result = await GetRandomQuiz.HandleAsync(db, current, clock, quizType, ct);
                    return result.Status switch
                    {
                        RandomQuizStatus.Ok => Results.Ok(result.Question),
                        RandomQuizStatus.InvalidQuizType => Results.Json(
                            ApiErrors.CreateInvalidQuizType(), statusCode: StatusCodes.Status400BadRequest),
                        RandomQuizStatus.NoQuestions => Results.Json(
                            ApiErrors.CreateNoQuestions(), statusCode: StatusCodes.Status404NotFound),
                        _ => Results.Json(ApiErrors.CreateForbidden(), statusCode: StatusCodes.Status403Forbidden),
                    };
                })
            .Produces<QuizQuestionResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        api.MapPost(
                "/quiz/{question_id}/answer",
                ([FromRoute(Name = "question_id")] string questionId, AnswerRequest request) => ApiResults.NotImplemented())
            .Produces<AnswerResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        return api;
    }
}
