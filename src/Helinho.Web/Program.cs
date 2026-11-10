using Helinho.Infrastructure.DependencyInjection;
using Helinho.Web.Composition;

var builder = WebApplication.CreateBuilder(args);

// Slices da abertura (1b, 1c, 1d) e áreas: cada método vive em arquivo próprio, em Composition/.
// Nenhuma Issue posterior edita este arquivo.
builder.Services.AddInfrastructureServices();
builder.Services.AddDomainPersistence(builder.Configuration);
builder.Services.AddApiContracts();
builder.Services.AddPanel();
builder.Services.AddAreas();

var app = builder.Build();

if (await app.TryRunSeedCommandAsync(args))
{
    return;
}

app.MapHealth();
app.MapApiContracts();
app.MapPanel();
app.MapAreas();

await app.RunAsync();

/// <summary>Exposto para o WebApplicationFactory dos testes de integração.</summary>
public partial class Program;
