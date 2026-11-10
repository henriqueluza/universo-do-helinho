# Clean Architecture — guia prático

## A regra que importa
Dependências de código-fonte apontam **para dentro**. Camadas internas não conhecem nomes das externas.

```
presentation  ─┐
infrastructure ─┼──►  application  ──►  domain
(adapters)     ─┘     (casos de uso)    (regras de negócio puras)
```

| Camada | Contém | Pode depender de | Nunca contém |
|---|---|---|---|
| domain | entidades, value objects, regras/invariantes, erros de domínio, eventos de domínio | nada (só a linguagem + libs utilitárias puras) | ORM, HTTP, framework, I/O, relógio do sistema, `env` |
| application | casos de uso (um por intenção do usuário), portas (interfaces) para o que precisa de fora, DTOs de entrada/saída | domain | SQL, chamada HTTP, código de framework web |
| infrastructure | implementações das portas: repositórios, clientes HTTP, filas, e-mail, storage, relógio | application, domain | regra de negócio |
| presentation | controllers, handlers, resolvers, CLI, telas; validação de formato; mapeamento request→DTO | application | regra de negócio, acesso direto a banco |
| composition root | wiring de DI, configuração | tudo | lógica |

## Portas e adapters
- A **porta** (interface) mora em `application` e é desenhada pelo que o caso de uso precisa, não pelo que o banco oferece. `PaymentGateway.charge(amount, customer)`, não `StripeClient.createPaymentIntent(...)`.
- O **adapter** mora em `infrastructure` e traduz para a tecnologia concreta.
- Interface só existe quando há fronteira de I/O ou mais de uma implementação real. Não crie `IUserService` com uma única implementação que não toca I/O.

## Casos de uso
- Um caso de uso = uma intenção (`ConfirmarReserva`, `EstornarPagamento`). Nome no imperativo do domínio, não `UserService.doStuff`.
- Recebe DTO de entrada, retorna DTO de saída ou `Result`. Não retorna entidade de ORM para a apresentação.
- Orquestra: carrega agregados pelas portas, chama regras do domínio, persiste, publica eventos. A regra em si fica no domínio.
- Transação é responsabilidade do caso de uso (ou de um Unit of Work injetado), não do controller.

## Domínio
- Invariantes protegidas na própria entidade (construtor/fábrica valida; sem setters públicos que quebrem regra).
- Value objects para conceitos com regra: `Money`, `Email`, `Cpf`, `DateRange`. Dinheiro nunca em float.
- Entidade anêmica + service gigante é o anti-padrão mais comum: se a regra é sobre o estado de uma entidade, ela mora na entidade.

## Fluxo de dados
Request → (presentation valida formato, mapeia para DTO) → caso de uso → domínio → porta → adapter → resposta mapeada. Mapeamentos explícitos em cada fronteira; não vaze tipos do ORM nem do framework web.

## Proporcionalidade
| Sinal no projeto | Nível |
|---|---|
| CRUD, site, landing, admin interno, protótipo | `simple` |
| Regras de negócio reais, integrações externas, vida útil > 1 ano | `clean` |
| Dinheiro, estados com transições complexas, auditoria, muitas invariantes | `clean+ddd` |

No nível `simple`, mantenha ao menos: regra de negócio fora do controller e acesso a dados atrás de uma função/classe nomeada. Isso permite subir para `clean` depois sem reescrever.

## Erros comuns que o code-reviewer vai bloquear
- Domínio importando ORM/annotations de persistência (exceção aceitável só se o ADR registrar o trade-off).
- Controller com regra de negócio ou query direta.
- Caso de uso chamando outro caso de uso em cadeia longa (extraia regra para o domínio ou um serviço de domínio).
- "Repository genérico" que só repassa o ORM (`IRepository<T>` com 30 métodos) — portas devem ser específicas do agregado.
- Camadas criadas vazias "para seguir o padrão".
