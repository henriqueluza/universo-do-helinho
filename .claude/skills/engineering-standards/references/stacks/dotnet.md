# Stack: C# / ASP.NET Core / EF Core

> Versões: use a versão LTS atual do .NET e as versões estáveis atuais dos pacotes — confirme via Context7 antes de adicionar dependências.

## 1. Detecção

- Arquivos: `*.sln` / `*.slnx`, `*.csproj`, `global.json`, `Directory.Build.props`, `Directory.Packages.props` (central package management).
- Web: `Program.cs` com `WebApplication.CreateBuilder`; EF Core: pacote `Microsoft.EntityFrameworkCore.*` no `.csproj`.
- Confirmar versão: `dotnet --version`, `dotnet --list-sdks`, `cat global.json`, `dotnet list package`.

## 2. Níveis de arquitetura

O ADR do projeto define o nível. Não suba de nível sem ADR novo.

`simple` — um projeto web, pastas por feature (CRUD, sites, back-office pequeno):

```
src/App.Api/
  Features/Orders/        # endpoint + DTOs + serviço da feature
    OrdersEndpoints.cs
    OrderService.cs
    OrderDtos.cs
  Data/AppDbContext.cs
  Program.cs
tests/App.Api.Tests/
```

`clean` — um projeto por camada; referências só para dentro:

```
src/
  App.Domain/             # entidades, value objects, regras; zero pacotes externos
  App.Application/        # casos de uso, portas (interfaces), DTOs; referencia Domain
  App.Infrastructure/     # EF Core, HTTP clients, e-mail; implementa portas
  App.Api/                # endpoints/controllers, DI (composition root)
tests/
  App.Domain.Tests/  App.Application.Tests/  App.Api.IntegrationTests/  App.ArchitectureTests/
```

`clean+ddd` — igual a `clean`, com agregados, eventos de domínio e repositórios por agregado em `App.Domain`.

## 3. Mapeamento Clean Architecture → construtos

| Conceito | Onde fica no .NET |
|---|---|
| Entidades / value objects | `App.Domain` — classes com invariantes no construtor; `record` para value objects |
| Casos de uso | `App.Application/<Feature>/` — uma classe por caso de uso (`PlaceOrderHandler`) |
| Portas | Interfaces em `App.Application/Abstractions` (`IOrderRepository`, `IClock`, `IPaymentGateway`) |
| Adapters | `App.Infrastructure` — `EfOrderRepository`, `StripePaymentGateway`, `AppDbContext` |
| Entrada HTTP | `App.Api` — Minimal APIs (`MapGroup`) ou controllers finos |
| Composition root | `Program.cs` + métodos de extensão `AddApplication()` / `AddInfrastructure()` |

`DbContext` só é visível em `Infrastructure`. Em `simple`, usar o `DbContext` direto no serviço é aceitável — ele já é unit of work + repositório.

## 4. Impondo a regra de dependência

1. Referências de projeto (`<ProjectReference>`) já impedem `Domain → Infrastructure`. Não adicione referências "por conveniência".
2. Teste de arquitetura com NetArchTest.Rules (ou ArchUnitNET):

```csharp
[Fact]
public void Domain_nao_depende_de_infra()
{
    var result = Types.InAssembly(typeof(Order).Assembly)
        .ShouldNot().HaveDependencyOnAny("App.Infrastructure", "Microsoft.EntityFrameworkCore")
        .GetResult();
    Assert.True(result.IsSuccessful);
}
```

## 5. SOLID idiomático

- **SRP**: controllers e endpoints só traduzem HTTP ↔ caso de uso. Violação típica: controller com 400 linhas acessando `DbContext`, mandando e-mail e calculando preço.
- **OCP**: novas variações via nova implementação de interface registrada no DI (ex.: estratégias de frete), não via `switch` crescendo em todo lugar.
- **LSP**: não implemente interface lançando `NotImplementedException`. Se precisar, a interface está grande demais.
- **ISP**: prefira `IOrderReader` / `IOrderWriter` pequenos a um `IRepository<T>` genérico com 20 métodos.
- **DIP**: o caso de uso depende de `IPaymentGateway` (definida em Application), não do SDK do provedor.

```csharp
public sealed class PlaceOrderHandler(IOrderRepository orders, IClock clock)
{
    public async Task<Result<OrderId>> Handle(PlaceOrder cmd, CancellationToken ct)
    {
        var order = Order.Create(cmd.CustomerId, cmd.Items, clock.UtcNow);
        if (order.IsFailure) return order.Error;
        await orders.AddAsync(order.Value, ct);
        return order.Value.Id;
    }
}
```

## 6. Padrões recomendados e quando NÃO usar

| Padrão | Use quando | Evite quando |
|---|---|---|
| Options pattern (`IOptions<T>` + `ValidateOnStart`) | Toda configuração tipada | — (sempre use; nunca leia `IConfiguration["x"]` espalhado) |
| Result pattern | Falhas de negócio esperadas (validação, regra violada) | Erros inesperados de infra — deixe a exceção subir |
| Repository por agregado | `clean`/`clean+ddd`, domínio rico, troca/teste de persistência | `simple` — `DbContext` já é repositório; não embrulhe por embrulhar |
| MediatR / CQRS | Muitos casos de uso com cross-cutting (log, validação, transação) via pipeline | Projetos pequenos; um handler injetado direto resolve. Verifique a licença atual da lib antes de adotar |
| Strategy | Regras que variam por tipo (frete, taxa, gateway) | Só existe uma variação hoje |
| Decorator (Scrutor ou manual) | Cache, retry, log em volta de uma porta | Lógica de negócio |
| Domain events | Efeitos colaterais desacoplados dentro do mesmo bounded context | Fluxo simples e síncrono num só caso de uso |
| Outbox | Publicar evento/mensagem de forma confiável junto com a transação | Sem mensageria |

Anti-padrões comuns:
- `IRepository<T>` genérico sobre EF Core expondo `IQueryable` — vaza a infra e não abstrai nada.
- Service locator (`IServiceProvider.GetService` dentro de classes de negócio).
- `async void`, `.Result` / `.Wait()` — deadlock e exceções perdidas.
- Entidade anêmica com setters públicos e toda regra em "Services".
- AutoMapper escondendo regra de negócio em perfis de mapeamento; prefira mapeamento explícito.
- Retornar entidades EF direto na API (vaza schema, causa over-posting).

## 7. Erros, validação, configuração e logging

- Validação de entrada na borda (FluentValidation ou DataAnnotations); invariantes no domínio (construtor/métodos de fábrica).
- Erros HTTP no formato ProblemDetails (`AddProblemDetails()` + `IExceptionHandler` global). Não vaze stack trace em produção.
- `CancellationToken` em toda cadeia async de I/O.
- Config: `appsettings.json` sem segredos; `appsettings.{Environment}.json` para ambiente; segredos via variáveis de ambiente, User Secrets (dev) ou secret manager do provedor.
- Logging: `ILogger<T>` com templates estruturados (`logger.LogInformation("Order {OrderId} placed", id)`), nunca interpolação de string; nunca logue tokens, senhas, documentos ou dados de cartão.
- Health checks (`AddHealthChecks`) e OpenTelemetry quando houver deploy.

## 8. Testes

- Ferramentas: xUnit, FluentAssertions ou Shouldly (verifique licença atual), NSubstitute, `WebApplicationFactory<Program>` para integração, Testcontainers para banco real.
- Pirâmide: muitos testes de domínio/casos de uso (rápidos, sem I/O) → alguns de integração (API + banco real em container) → poucos E2E.
- Não use EF InMemory para testar queries — o comportamento difere do banco real. Use Testcontainers (PostgreSQL) ou SQLite apenas se o ADR aceitar.

```csharp
[Fact]
public void Pedido_sem_itens_falha()
{
    var result = Order.Create(CustomerId.New(), items: [], DateTime.UtcNow);
    result.IsFailure.Should().BeTrue();
    result.Error.Should().Be(OrderErrors.Empty);
}
```

## 9. Comandos de qualidade

```bash
dotnet format --verify-no-changes
dotnet build --no-restore -warnaserror
dotnet test --no-build --collect:"XPlat Code Coverage"
dotnet list package --vulnerable --include-transitive
```

Ative `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` e analyzers no `Directory.Build.props`.

```yaml
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - run: dotnet restore
      - run: dotnet format --verify-no-changes --no-restore
      - run: dotnet build --no-restore -warnaserror
      - run: dotnet test --no-build
      - run: dotnet list package --vulnerable --include-transitive
```

## 10. Checklist de PR (.NET)

- [ ] Nível de arquitetura do ADR respeitado; nenhuma referência de projeto nova apontando para fora
- [ ] Teste de arquitetura passando
- [ ] Endpoints/controllers finos; regra de negócio no domínio ou caso de uso
- [ ] Entidades EF não expostas na API; DTOs explícitos
- [ ] `CancellationToken` propagado; sem `.Result`, `.Wait()` ou `async void`
- [ ] Falhas de negócio via Result/ProblemDetails; sem `catch (Exception)` engolindo erro
- [ ] Config tipada com Options + validação; nenhum segredo em `appsettings*.json`
- [ ] Logs estruturados, sem dados sensíveis
- [ ] Migration EF gerada e revisada (sem `DROP` acidental) quando o schema mudou
- [ ] Testes de domínio + integração com banco real para queries novas
- [ ] `dotnet format`, build com warnings como erro e testes verdes
