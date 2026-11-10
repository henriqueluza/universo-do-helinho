using Helinho.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helinho.Web.Api.Quiz;

public static class QuizEndpoints
{
    public static RouteGroupBuilder MapQuizEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/quiz/random", ([FromQuery(Name = "quiz_type")] string quizType) => ApiResults.NotImplemented())
            .Produces<QuizQuestionResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        api.MapPost(
                "/quiz/{question_id}/answer",
                ([FromRoute(Name = "question_id")] string questionId, AnswerRequest request) => ApiResults.NotImplemented())
            .Produces<AnswerResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        return api;
    }
}
