# PHP / Laravel

Guia para agentes que implementam Issues em projetos Laravel. Leia junto com `../clean-architecture.md`, `../solid.md` e `../design-patterns.md`. Versões: use a versão estável atual do PHP e do Laravel — confirme APIs via Context7 (estrutura de `bootstrap/app.php` e registro de middleware mudaram nas versões recentes).

## 1. Detecção

- `composer.json` com `laravel/framework` + arquivo `artisan` na raiz → esta stack.
- `composer.lock` versionado; `pestphp/pest` ou `phpunit/phpunit` em `require-dev` → framework de testes em uso. **Use o que já existe.**
- `phpstan.neon`/`larastan`, `pint.json`, `deptrac.yaml` → ferramentas de qualidade configuradas.
- Confirmar versões: `php -v`, `php artisan --version`, `composer show laravel/framework`.

## 2. Níveis de arquitetura

`simple` — **é o Laravel convencional. Não brigue com o framework.** Eloquent nos controllers/actions é aceitável; padrões extras só quando o código pedir:

```
app/
  Http/
    Controllers/OrderController.php
    Requests/StoreOrderRequest.php
    Resources/OrderResource.php
  Models/Order.php
  Actions/PlaceOrder.php        # lógica que não cabe no controller
  Policies/OrderPolicy.php
  Jobs/  Events/  Listeners/
database/migrations/
routes/api.php
tests/{Feature,Unit}/
```

`clean` — domínio relevante (pagamentos, regras financeiras) que precisa ser testado e evoluir isolado de Eloquent:

```
app/                            # camada de framework (presentation + composition root)
  Http/Controllers/  Http/Requests/  Http/Resources/
  Providers/DomainServiceProvider.php   # binds de interfaces → implementações
src/
  Domain/Orders/                # entidades e VOs em PHP puro, interfaces de repositório, exceções
  Application/Orders/           # casos de uso (PlaceOrder), DTOs de comando
  Infrastructure/Persistence/   # Eloquent models + EloquentOrderRepository (mapeia model ↔ entidade)
```

Registre `"Src\\": "src/"` (ou o namespace do projeto) no `autoload.psr-4` do `composer.json`.

`clean+ddd` — agregados, VOs ricos e eventos de domínio despachados após commit. Só se o ADR pedir.

## 3. Mapeamento Clean Architecture → Laravel

| Conceito | `simple` | `clean` |
|---|---|---|
| Entidades | Eloquent `Model` | Classes PHP puras em `src/Domain` (`final`, `readonly` onde couber) |
| Casos de uso | `app/Actions` (classe com `handle`/`__invoke`) | `src/Application` |
| Portas | — | Interfaces em `src/Domain` (`OrderRepository`) |
| Adapters | — | `src/Infrastructure` (Eloquent, clients HTTP) |
| Validação de entrada | Form Request | Form Request → DTO de comando |
| Controllers | Finos: Request → Action → Resource | Idem, chamando caso de uso |
| Autorização | Policies / Gates | Policies (na borda) |
| DI / composition root | Container automático | `DomainServiceProvider` com `$this->app->bind(Interface::class, Impl::class)` |

## 4. Impondo a regra de dependência

**Pest arch tests** (se o projeto usa Pest):

```php
arch('domínio não depende do framework')
    ->expect('Src\Domain')
    ->not->toUse(['Illuminate', 'Src\Infrastructure', 'App']);

arch('aplicação não depende de infraestrutura')
    ->expect('Src\Application')
    ->not->toUse(['Illuminate\Database', 'Src\Infrastructure']);

arch()->preset()->laravel();   // convenções do Laravel em app/
```

Alternativa sem Pest: **deptrac** com camadas `Domain`, `Application`, `Infrastructure`, `Framework` e ruleset declarando quem pode depender de quem; rode `vendor/bin/deptrac analyse` no CI.

## 5. SOLID idiomático

- **S** — Controller recebe, autoriza, delega e responde. Violação típica: "fat controller" com validação manual, queries e envio de e-mail. Validação → Form Request; lógica → Action; e-mail → Listener/Job.
- **O** — Novo provedor (pagamento, SMS) = nova classe implementando interface, trocada no Service Provider; não `if ($provider === 'x')` espalhado.
- **L** — Implementações de uma interface lançam as mesmas exceções de domínio; o fake de teste não pode ser mais permissivo que a implementação real.
- **I** — Interfaces pequenas por necessidade do caso de uso; evite `RepositoryInterface` genérico com 20 métodos.
- **D** — Dependa de interfaces via injeção no construtor (o container resolve); nunca `new` de serviço de infraestrutura dentro de caso de uso. Facades são aceitáveis em `app/` no nível `simple`, nunca em `src/Domain`/`src/Application`.

```php
final class PlaceOrder
{
    public function __construct(
        private readonly OrderRepository $orders,
        private readonly PaymentGateway $payments,
    ) {}

    public function handle(PlaceOrderCommand $cmd): Order
    {
        $order = Order::create($cmd->customerId, $cmd->items); // invariantes no domínio
        $this->payments->authorize($order->total());
        $this->orders->save($order);
        return $order;
    }
}
```

## 6. Padrões recomendados e quando NÃO usar

| Padrão | Use quando | Evite quando |
|---|---|---|
| Form Request | Toda entrada HTTP com validação/autorização | — |
| Action (classe única com `handle`) | Lógica que não cabe no controller ou é reutilizada (controller, job, comando) | Ação trivial de uma linha |
| Policy | Toda regra "quem pode fazer o quê" sobre um recurso | Regras de negócio que não são de autorização |
| API Resource | Formatar respostas JSON (não expor model cru) | — |
| Job (fila) | Trabalho lento ou externo (e-mail, webhooks, relatórios); jobs idempotentes | Algo que o usuário precisa ver concluído na mesma requisição |
| Event + Listener | Efeitos colaterais desacoplados de um fato do domínio | Fluxo principal que precisa de ordem e transação — chame direto |
| Repository | Nível `clean`, isolando domínio de Eloquent | Nível `simple` — repositório que só repassa Eloquent é cerimônia |
| DTO (`readonly` class) | Passar dados validados para Action/caso de uso | Arrays associativos não tipados atravessando camadas |

Anti-padrões comuns:
- Fat controller / fat model (model com regras de negócio de vários contextos).
- Consultas N+1 — use eager loading (`with`) e, em dev, `Model::preventLazyLoading()`.
- `$request->all()` em `create()`/`update()` (mass assignment) — use `$request->validated()` e `$fillable`.
- Lógica de negócio em observers ocultos que ninguém lembra que existem.
- `env()` fora de arquivos `config/` (quebra com `config:cache`).
- Facades e helpers globais dentro do domínio no nível `clean`.

## 7. Erros, validação, configuração e logging

- Validação de entrada: Form Requests. Invariantes de negócio: exceções de domínio no domínio/Action.
- Exceções mapeadas para HTTP em um ponto (`withExceptions` em `bootstrap/app.php` ou `render()` na exceção), com corpo JSON consistente e sem stack trace em produção (`APP_DEBUG=false`).
- Transações com `DB::transaction()` envolvendo o caso de uso inteiro quando grava em mais de uma tabela.
- Configuração: `.env` → arquivos `config/*.php` → `config('x.y')` no código. `.env` nunca versionado; `.env.example` sem valores reais.
- Logging com `Log::` (canais em `config/logging.php`), contexto estruturado (`Log::withContext`); nunca logue senhas, tokens, cartão, CPF. Use `#[\SensitiveParameter]` em parâmetros sensíveis.

## 8. Testes

- **Feature** (maioria): requisição HTTP real contra a app com `RefreshDatabase`, factories e asserts de resposta/banco.
- **Unit**: domínio e casos de uso com fakes das interfaces — sem framework no nível `clean`.
- Fakes do framework: `Queue::fake()`, `Event::fake()`, `Mail::fake()`, `Http::fake()` para integrações externas.
- Banco de teste do mesmo tipo da produção quando houver SQL específico (container no CI); SQLite em memória só se o projeto não depende de recursos específicos.

```php
it('cria pedido e enfileira confirmação', function () {
    Queue::fake();
    $user = User::factory()->create();

    $this->actingAs($user)
        ->postJson('/api/orders', ['items' => [['sku' => 'A1', 'qty' => 2]]])
        ->assertCreated()
        ->assertJsonPath('data.status', 'pending');

    Queue::assertPushed(SendOrderConfirmation::class);
});
```

## 9. Comandos de qualidade

```bash
vendor/bin/pint --test
vendor/bin/phpstan analyse        # com Larastan, no nível configurado em phpstan.neon
php artisan test                  # ou vendor/bin/pest
vendor/bin/deptrac analyse        # se usar deptrac
```

Steps para o job `validate`:

```yaml
      - uses: shivammathur/setup-php@v2
        with:
          php-version: '8.3'      # ajuste para a versão do composer.json
          coverage: none
      - run: composer install --no-interaction --prefer-dist
      - run: cp .env.example .env && php artisan key:generate
      - run: vendor/bin/pint --test
      - run: vendor/bin/phpstan analyse --no-progress
      - run: php artisan test
```

## 10. Checklist de PR

- [ ] Controller fino; validação em Form Request e autorização em Policy
- [ ] Respostas via API Resource, sem model cru
- [ ] Sem mass assignment (`validated()` + `$fillable`)
- [ ] Sem N+1 (eager loading onde há relação iterada)
- [ ] `env()` só em `config/`; nenhum segredo versionado
- [ ] Gravações múltiplas dentro de `DB::transaction()`
- [ ] Jobs idempotentes e com retry/backoff definidos
- [ ] Nível `clean`: domínio sem `Illuminate` (arch test/deptrac passando)
- [ ] Migration nova para toda mudança de schema
- [ ] Feature test do endpoint + unit test da regra nova
- [ ] `pint`, `phpstan` e testes passando
