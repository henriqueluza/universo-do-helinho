using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Web.Api;
using Helinho.Web.Api.Auth;
using Helinho.Web.Api.Devices;
using Helinho.Web.Api.Home;
using Helinho.Web.Api.Quests;
using Helinho.Web.Api.Quiz;
using Helinho.Web.Api.User;
using Microsoft.AspNetCore.Authentication;
using Scalar.AspNetCore;

namespace Helinho.Web.Composition;

/// <summary>Slice 1c: JSON snake_case, autenticação Bearer, grupo /api (P16), OpenAPI e Scalar.</summary>
public static class ApiContracts
{
    public static IServiceCollection AddApiContracts(this IServiceCollection services)
    {
        var json = ApiJson.CreateOptions();
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = json.PropertyNamingPolicy;
            o.SerializerOptions.DictionaryKeyPolicy = json.DictionaryKeyPolicy;
            o.SerializerOptions.DefaultIgnoreCondition = json.DefaultIgnoreCondition;
            foreach (var converter in json.Converters)
            {
                o.SerializerOptions.Converters.Add(converter);
            }
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentStudent, HttpContextCurrentStudent>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ApiBearerHandler>(ApiAuth.Scheme, null);
        services.AddAuthorizationBuilder().AddPolicy(
            ApiAuth.Policy, p => p.AddAuthenticationSchemes(ApiAuth.Scheme).RequireAuthenticatedUser());
        services.AddOpenApi();
        return services;
    }

    public static WebApplication MapApiContracts(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization(ApiAuth.Policy);
        api.MapAuthEndpoints().MapHomeEndpoints().MapQuizEndpoints()
            .MapQuestsEndpoints().MapUserEndpoints().MapDevicesEndpoints();

        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference().AllowAnonymous();
        return app;
    }
}
