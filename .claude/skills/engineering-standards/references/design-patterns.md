# Catálogo de design patterns — com "quando NÃO usar"

Regra: um pattern entra no código para resolver um problema **presente** e é citado no PR ("Strategy para os meios de pagamento: 3 variações hoje"). Se você não consegue escrever essa frase, não use.

## Criacionais
| Pattern | Use quando | Evite quando |
|---|---|---|
| Factory / método de fábrica | a criação tem regra (validação, invariantes) ou escolhe implementação por configuração | `new` simples resolve |
| Builder | objeto com muitos opcionais, construção em etapas, testes que precisam de dados variados (Test Data Builder) | ≤ 4 parâmetros; a linguagem tem parâmetros nomeados/defaults |
| Singleton | nunca como classe estática global; use o escopo singleton do container de DI | sempre que for estado global mutável |

## Estruturais
| Pattern | Use quando | Evite quando |
|---|---|---|
| Adapter | integrar SDK/API externa atrás de uma porta do domínio | a lib já tem a forma da porta e não há troca prevista |
| Facade | simplificar um subsistema complexo para um consumidor específico | vira um "service-deus" que esconde tudo |
| Decorator | adicionar comportamento transversal (cache, retry, métricas, logging) sem mexer no original | o framework já oferece middleware/interceptor equivalente |
| Composite | estruturas em árvore tratadas uniformemente (menus, regras compostas) | dados planos |
| Proxy | lazy loading, controle de acesso, chamada remota transparente | adiciona indireção sem motivo |

## Comportamentais
| Pattern | Use quando | Evite quando |
|---|---|---|
| Strategy | 2+ variações reais de um algoritmo selecionadas em runtime (tarifa, meio de pagamento, frete) | uma única variação "por enquanto" |
| Specification | regras de negócio combináveis e reutilizadas em validação e consulta | regra usada em um único lugar |
| State | entidade com máquina de estados e transições com regra (pedido, pagamento, partida de jogo) | 2–3 estados triviais: um enum + guarda basta |
| Observer / eventos de domínio | efeitos colaterais desacoplados (e-mail após pagamento confirmado) | o fluxo precisa ser síncrono e transacional e há um só consumidor |
| Command | operações enfileiráveis, desfazer/refazer, auditoria de intenções | chamada direta de método é suficiente |
| Chain of Responsibility | pipeline de validações/regras ordenadas e configuráveis | o framework já tem pipeline (middlewares, pipes) |
| Template Method | variações de um algoritmo com esqueleto fixo, em linguagens com herança | prefira Strategy/composição na maioria dos casos |
| Mediator / CQRS | muitos casos de uso com cross-cutting uniforme; leitura e escrita com modelos muito diferentes | app pequeno — vira cerimônia e indireção |

## Patterns de arquitetura/aplicação
| Pattern | Use quando | Evite quando |
|---|---|---|
| Repository | fronteira de persistência para um agregado, com métodos do domínio | repositório genérico que só repassa o ORM |
| Unit of Work | múltiplos agregados/repositórios na mesma transação | uma escrita por request; o ORM já é UoW (EF Core DbContext, SQLAlchemy Session) — não embrulhe de novo |
| Result / Either | erros de negócio esperados (saldo insuficiente) em vez de exceção para fluxo | erros realmente excepcionais/infra |
| Outbox | publicar evento + gravar no banco com garantia (pagamentos, integrações) | sem mensageria ou consistência eventual não importa |
| Idempotency key | qualquer endpoint de pagamento, webhook ou retry de cliente | operações naturalmente idempotentes (GET, PUT por id) |
| Circuit breaker / retry com backoff | chamadas a serviços externos instáveis | chamadas locais; retry sem idempotência = efeito duplicado |
| Anti-Corruption Layer | integrar sistema legado ou API com modelo ruim | API externa bem modelada e estável |

## Sinais de over-engineering (o code-reviewer bloqueia)
- Interface com uma implementação e sem I/O.
- Factory que só faz `new`.
- Camada/pasta criada sem conteúdo real.
- Pattern escolhido "porque é boa prática" sem problema nomeado.
- Generics/abstrações para casos hipotéticos.
