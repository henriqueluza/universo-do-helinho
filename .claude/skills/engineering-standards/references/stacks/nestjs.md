# NestJS / TypeScript (backend)

Guia para agentes implementando Issues em APIs NestJS. Leia junto com `../clean-architecture.md`, `../solid.md` e `../design-patterns.md`. Versões de bibliotecas: use a versão estável atual — confirme via Context7.

## 1. Detecção

Arquivos que identificam a stack:
- `nest-cli.json` (decisivo)
- `package.json` com `@nestjs/core` e `@nestjs/common`
- `tsconfig.json`, `tsconfig.build.json`
- ORM: `prisma/schema.prisma` (Prisma), `ormconfig.*` ou `DataSource` em `src/**/data-source.ts` (TypeORM), `firebase.json` / uso de `firebase-admin` (Firestore)

Confirme versões:
```bash
node --version
npx nest --version
npm ls @nestjs/core typescript prisma typeorm firebase-admin --depth=0
```

## 2. Níveis de arquitetura

O ADR do projeto define o nível. Na dúvida, pergunte — não suba de nível por conta própria.

`simple` — CRUD, back-office, integrações finas. Módulo por recurso, service fala direto com o ORM:
```
src/
  main.ts
  app.module.ts
  users/
    users.module.ts
    users.controller.ts
    users.service.ts
    dto/create-user.dto.ts
    users.service.spec.ts
  common/ (filters, guards, pipes)
```

`clean` — regra de negócio relevante, múltiplas integrações, troca provável de infraestrutura:
```
src/
  main.ts
  app.module.ts                 # composition root
  modules/
    orders/
      domain/                   # entidades, value objects, erros de domínio — TS puro
        order.entity.ts
        order-status.ts
      application/              # casos de uso + portas
        ports/order.repository.ts
        ports/payment.gateway.ts
        use-cases/place-order.use-case.ts
      infrastructure/           # adapters: Prisma/TypeORM/Firebase, HTTP clients
        prisma-order.repository.ts
        stripe-payment.gateway.ts
      presentation/             # controllers, DTOs, mappers HTTP
        orders.controller.ts
        dto/place-order.dto.ts
      orders.module.ts          # liga portas → adapters
  shared/ (config, logger, filters)
```

`clean+ddd` — igual ao `clean`, com agregados, eventos de domínio e repositórios por agregado. Use só em domínios ricos (pagamentos, financeiro, contábil).

## 3. Mapeamento Clean Architecture → NestJS

| Conceito | Onde fica no Nest |
|---|---|
| Entidades / value objects | `domain/` — classes TS puras, sem decorators do Nest nem do ORM |
| Casos de uso | `application/use-cases/` — `@Injectable()` é aceitável aqui (único acoplamento tolerado) |
| Portas | `application/ports/` — `interface` + token (`Symbol`) para injeção |
| Adapters | `infrastructure/` — Prisma, TypeORM, Firebase Admin, clientes HTTP |
| Controllers | `presentation/` — só traduzem HTTP ↔ caso de uso |
| DI / composition root | `*.module.ts` (providers `{ provide: TOKEN, useClass: Adapter }`) e `app.module.ts` |

Interfaces TS somem em runtime, então injete por token:
```ts
export const ORDER_REPOSITORY = Symbol('ORDER_REPOSITORY');
export interface OrderRepository {
  save(order: Order): Promise<void>;
  findById(id: string): Promise<Order | null>;
}
// orders.module.ts
providers: [
  PlaceOrderUseCase,
  { provide: ORDER_REPOSITORY, useClass: PrismaOrderRepository },
],
```
```ts
@Injectable()
export class PlaceOrderUseCase {
  constructor(@Inject(ORDER_REPOSITORY) private readonly orders: OrderRepository) {}
}
```

Entidades do ORM (`@Entity` do TypeORM, modelos do Prisma, documentos do Firestore) **não** são entidades de domínio: mapeie no adapter.

## 4. Impondo a regra de dependência

Use `eslint-plugin-boundaries` (ou `dependency-cruiser` no CI). Exemplo com boundaries (flat config):
```js
// eslint.config.mjs (trecho)
settings: {
  'boundaries/elements': [
    { type: 'domain', pattern: 'src/modules/*/domain/**' },
    { type: 'application', pattern: 'src/modules/*/application/**' },
    { type: 'infrastructure', pattern: 'src/modules/*/infrastructure/**' },
    { type: 'presentation', pattern: 'src/modules/*/presentation/**' },
  ],
},
rules: {
  'boundaries/element-types': ['error', { default: 'disallow', rules: [
    { from: 'domain', allow: ['domain'] },
    { from: 'application', allow: ['domain', 'application'] },
    { from: 'infrastructure', allow: ['domain', 'application', 'infrastructure'] },
    { from: 'presentation', allow: ['application', 'domain', 'presentation'] },
  ]}],
},
```
Regra extra: `domain/` não importa `@nestjs/*`, `@prisma/client`, `typeorm` nem `firebase-admin` (`no-restricted-imports`).

## 5. SOLID idiomático

**S — Responsabilidade única.** A violação típica é o *service-deus* (`UsersService` com 40 métodos: cadastro, e-mail, relatório, billing). Quebre por caso de uso (`RegisterUserUseCase`, `SendWelcomeEmailUseCase`). Controller não contém regra de negócio.

**O — Aberto/fechado.** Novos meios de pagamento entram como novo adapter da porta `PaymentGateway`, não como `if (provider === 'x')` no caso de uso.

**L — Substituição de Liskov.** Todo adapter de uma porta respeita o mesmo contrato (incluindo erros lançados). Um `InMemoryOrderRepository` de teste deve se comportar como o Prisma.

**I — Segregação de interfaces.** Prefira portas pequenas (`OrderReader`, `OrderWriter`) a um `IRepository<T>` genérico com 15 métodos que ninguém usa por inteiro.

**D — Inversão de dependência.** Casos de uso dependem de portas (tokens), nunca de `PrismaService` ou `Firestore` diretamente:
```ts
// errado
constructor(private readonly prisma: PrismaService) {}
// certo
constructor(@Inject(ORDER_REPOSITORY) private readonly orders: OrderRepository) {}
```
No nível `simple`, injetar `PrismaService` no service é aceitável — não crie porta sem necessidade.

## 6. Padrões recomendados e quando NÃO usar

| Padrão | Use quando | Evite quando |
|---|---|---|
| Módulo por feature | Sempre | — |
| Provider com token de interface | Há mais de um adapter possível ou precisa de fake em teste | CRUD `simple` com um só banco |
| Guards (auth/roles) | Toda regra de autorização por rota | Para lógica de negócio (vai no caso de uso) |
| Pipes (`ValidationPipe`) | Validação/transformação de entrada | Para regra de domínio |
| Interceptors | Logging, métricas, mapeamento de resposta, cache transversal | Para alterar regra de negócio |
| Exception filters | Traduzir erros de domínio → HTTP | Para engolir erros |
| Repository | Isolar ORM/Firestore do domínio no nível `clean` | Envolver o Prisma 1:1 sem ganho no nível `simple` |
| Strategy | Variações intercambiáveis (frete, pagamento, notificação) | Só existe uma variante |
| CQRS (`@nestjs/cqrs`) | Leitura e escrita com modelos realmente diferentes, event sourcing | CRUD comum — adiciona burocracia |
| Outbox / eventos de domínio | Efeito colateral precisa ser confiável após commit (pagamentos) | Projetos sem integração assíncrona |

Anti-padrões comuns:
- Service-deus que concentra o módulo inteiro.
- Entidade do ORM vazando para controller/resposta HTTP (expõe colunas internas, acopla API ao schema).
- `forwardRef` para resolver dependência circular entre módulos — sinal de fronteira errada; extraia um módulo compartilhado.
- Lógica de negócio dentro de guard, pipe ou interceptor.
- `any` e `as unknown as X` para silenciar o compilador.
- Repositório genérico `BaseRepository<T>` usado por tudo.

## 7. Erros, validação, configuração e logging

- **Validação:** `ValidationPipe` global com `whitelist: true, forbidNonWhitelisted: true, transform: true`; DTOs com `class-validator`. Alternativa aceitável: `zod` com pipe próprio — escolha um por projeto.
- **Erros:** domínio lança erros próprios (`OrderAlreadyPaidError`), sem `HttpException`. Um `ExceptionFilter` traduz para HTTP:
```ts
@Catch(DomainError)
export class DomainErrorFilter implements ExceptionFilter {
  catch(err: DomainError, host: ArgumentsHost) {
    const res = host.switchToHttp().getResponse();
    res.status(err.httpStatus ?? 422).json({ code: err.code, message: err.message });
  }
}
```
- Nunca retorne stack trace ou mensagem do banco ao cliente.
- **Configuração:** `@nestjs/config` com schema validado na inicialização (falhe cedo se faltar variável). Segredos só via variável de ambiente/secret manager; nunca no repositório. Credencial do Firebase Admin via variável/arquivo fora do repo.
- **Logging:** logger estruturado (JSON) com correlation id por requisição; não logue tokens, senhas, CPF, dados de cartão.

## 8. Testes

- **Unitários (Jest ou Vitest):** domínio e casos de uso com fakes das portas — sem Nest, sem banco.
- **Integração:** adapters contra banco real em container (Testcontainers ou serviço do CI), não mock do ORM.
- **E2E de API:** `@nestjs/testing` + `supertest` nos fluxos críticos.

```ts
it('não permite pagar pedido já pago', async () => {
  const repo = new InMemoryOrderRepository([paidOrder()]);
  const useCase = new PayOrderUseCase(repo, new FakePaymentGateway());
  await expect(useCase.execute({ orderId: 'o-1' }))
    .rejects.toBeInstanceOf(OrderAlreadyPaidError);
});
```

Cubra o caminho feliz, os erros de domínio e as regras de autorização de cada endpoint novo.

## 9. Comandos de qualidade

```bash
npx prettier --check .
npx eslint .
npx tsc --noEmit
npm test
npm run build
```

Steps para o job `validate`:
```yaml
      - uses: actions/setup-node@v4
        with:
          node-version-file: '.nvmrc'
          cache: npm
      - run: npm ci
      - run: npx prettier --check .
      - run: npx eslint .
      - run: npx tsc --noEmit
      - run: npm test -- --ci
      - run: npm run build
```
Se usar Prisma, adicione `npx prisma generate` antes do typecheck e `npx prisma validate`.

## 10. Checklist de PR desta stack

- [ ] Controller sem regra de negócio; caso de uso testável sem Nest.
- [ ] `domain/` não importa `@nestjs/*`, ORM nem `firebase-admin` (nível `clean`).
- [ ] Entidades de ORM não aparecem em DTOs de resposta.
- [ ] Todo DTO de entrada validado; `ValidationPipe` com whitelist ativo.
- [ ] Rotas novas protegidas pelo guard correto (authn + authz).
- [ ] Erros de domínio traduzidos por filter; nenhum stack trace exposto.
- [ ] Nenhum segredo em código, log ou commit; config validada no boot.
- [ ] Sem `forwardRef` novo nem dependência circular entre módulos.
- [ ] Migrations versionadas e reversíveis quando o schema mudou.
- [ ] Testes unitários do caso de uso + teste de integração/E2E do fluxo alterado.
- [ ] `lint`, `tsc --noEmit`, testes e build verdes.
