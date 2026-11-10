# Stack: Java e Kotlin / Spring Boot

> Versões: use o JDK LTS atual e a versão estável atual do Spring Boot — confirme via Context7 antes de adicionar dependências. Gerencie versões pelo BOM do Spring Boot, não fixe versões de starters à mão.

## 1. Detecção

- Maven: `pom.xml` com parent `spring-boot-starter-parent`; `mvnw`.
- Gradle: `build.gradle(.kts)` com plugin `org.springframework.boot`; `gradlew`, `settings.gradle(.kts)`.
- Kotlin: plugin `kotlin("jvm")` + `kotlin("plugin.spring")`; arquivos `*.kt`.
- Entrada: classe com `@SpringBootApplication`.
- Confirmar versão: `java -version`, `./mvnw -v` ou `./gradlew --version`, `./mvnw dependency:tree | grep spring-boot` ou `./gradlew dependencies`.

## 2. Níveis de arquitetura

O ADR do projeto define o nível. Não suba de nível sem ADR novo.

`simple` — pacote por feature, camadas enxutas dentro dele:

```
src/main/java/com/acme/app/
  order/
    OrderController.java
    OrderService.java
    OrderRepository.java      # Spring Data
    Order.java                # @Entity
    OrderDtos.java
  AppApplication.java
src/test/java/com/acme/app/order/
```

`clean` — domínio sem Spring; adapters em volta (hexagonal). Módulos Gradle/Maven separados ou pacotes com ArchUnit:

```
src/main/java/com/acme/app/
  domain/          # entidades, value objects, regras — sem anotações Spring/JPA
  application/     # casos de uso (@Service opcional), portas: port/in, port/out
  infrastructure/  # adapters: persistence (JPA entities + repositories), http clients, messaging
  web/             # @RestController, DTOs, @ControllerAdvice
  config/          # @Configuration, wiring de beans (composition root)
```

`clean+ddd` — `clean` com agregados, eventos de domínio e um pacote por bounded context. Considere Spring Modulith para verificar módulos.

## 3. Mapeamento Clean Architecture → construtos

| Conceito | Onde fica no Spring |
|---|---|
| Entidades / value objects | `domain` — classes puras; `record` (Java) / `data class` ou `@JvmInline value class` (Kotlin) |
| Casos de uso | `application` — uma classe por caso de uso ou serviço por agregado |
| Portas | Interfaces em `application/port/out` (`LoadOrderPort`, `PaymentGateway`) |
| Adapters | `infrastructure` — `OrderJpaAdapter implements LoadOrderPort`, entidades `@Entity` separadas do domínio em `clean` |
| Entrada HTTP | `web` — `@RestController` fino |
| Composition root | Container do Spring; `@Configuration` em `config` para beans de domínio que não carregam anotações |

Em `simple`, usar `@Entity` como modelo de domínio e Spring Data direto no serviço é aceitável.

## 4. Impondo a regra de dependência

ArchUnit (JUnit 5) no módulo de testes:

```java
@AnalyzeClasses(packages = "com.acme.app")
class ArchitectureTest {
  @ArchTest
  static final ArchRule domainIsolado = noClasses()
      .that().resideInAPackage("..domain..")
      .should().dependOnClassesThat()
      .resideInAnyPackage("org.springframework..", "jakarta.persistence..", "..infrastructure..", "..web..");
}
```

Alternativas: módulos Gradle/Maven separados (o compilador impede o import) ou `ApplicationModules.of(App.class).verify()` do Spring Modulith.

## 5. SOLID idiomático

- **SRP**: `@RestController` só valida/converte e chama o caso de uso. Violação típica: "God service" `OrderService` com 40 métodos para tudo do pedido.
- **OCP**: novas variações como novos beans de uma interface; injete `List<ShippingStrategy>` ou `Map<String, ShippingStrategy>` em vez de `if/else` por tipo.
- **LSP**: subclasses de entidade JPA (herança `@Inheritance`) frequentemente quebram LSP; prefira composição.
- **ISP**: portas pequenas por necessidade (`LoadOrderPort`, `SaveOrderPort`) em vez de expor `JpaRepository` inteiro (com `deleteAll`) para o caso de uso.
- **DIP**: injeção por construtor (campo `final`/`val`); nunca `@Autowired` em campo.

```java
@Service
public class PlaceOrderUseCase {
  private final SaveOrderPort orders;
  private final Clock clock;

  public PlaceOrderUseCase(SaveOrderPort orders, Clock clock) {
    this.orders = orders;
    this.clock = clock;
  }

  public OrderId handle(PlaceOrderCommand cmd) {
    var order = Order.place(cmd.customerId(), cmd.items(), Instant.now(clock));
    return orders.save(order).id();
  }
}
```

## 6. Padrões recomendados e quando NÃO usar

| Padrão | Use quando | Evite quando |
|---|---|---|
| `@ConfigurationProperties` + `@Validated` (records) | Toda configuração tipada | — (evite `@Value` espalhado) |
| Perfis (`application-{profile}.yml`) | Diferenças de ambiente (dev/test/prod) | Para ligar/desligar regra de negócio — use feature flag/config |
| Ports & adapters | `clean`; integrações externas trocáveis/testáveis | `simple` com Spring Data suficiente |
| Eventos de domínio (`ApplicationEventPublisher`, `@TransactionalEventListener`) | Efeitos colaterais após commit (e-mail, notificação) dentro do mesmo serviço | Fluxo principal que precisa de resposta síncrona |
| Strategy via beans | Regras que variam por tipo | Só uma variação |
| Outbox (ou Spring Modulith event publication registry) | Mensagem confiável junto da transação | Sem mensageria |
| Specification / Criteria / Querydsl | Filtros dinâmicos combináveis | Consultas fixas — use método derivado ou `@Query` |

Anti-padrões comuns:
- `@Autowired` em campo e dependências circulares.
- `@Transactional` em controller, ou em método `private`/chamado internamente (o proxy não intercepta).
- Expor `@Entity` na API (lazy loading em serialização, over-posting); use DTOs.
- N+1 por `FetchType.EAGER` ou acesso lazy em loop; use `JOIN FETCH`/`@EntityGraph`.
- `open-in-view` ligado em APIs (deixe `spring.jpa.open-in-view=false`).
- Kotlin: `data class` como entidade JPA (equals/hashCode quebram com proxies); use classe normal com plugin `kotlin-jpa`/`allopen`.

## 7. Erros, validação, configuração e logging

- Validação de entrada com Jakarta Validation (`@Valid` + anotações no DTO); invariantes no domínio.
- Erros: `@RestControllerAdvice` global retornando `ProblemDetail` (RFC 9457); exceções de domínio mapeadas para 4xx; não vaze stack trace.
- Config: `application.yml` sem segredos; segredos via variáveis de ambiente ou secret manager; `@ConfigurationProperties` validado no startup.
- Logging: SLF4J com placeholders (`log.info("Order {} placed", id)`), formato estruturado (JSON) em produção; MDC para correlation id; nunca logue PII, tokens ou cartão.
- Actuator só com endpoints necessários expostos e protegidos.

## 8. Testes

- Ferramentas: JUnit 5, AssertJ, Mockito (Java) / MockK + Kotest opcional (Kotlin), Spring Boot Test, Testcontainers (`@ServiceConnection`), ArchUnit.
- Pirâmide: testes de domínio puros (sem Spring, milissegundos) → slices (`@WebMvcTest`, `@DataJpaTest` com Testcontainers) → poucos `@SpringBootTest` completos.
- Não use H2 no lugar do PostgreSQL de produção para testar queries — use Testcontainers.

```java
class OrderTest {
  @Test
  void pedidoSemItensFalha() {
    assertThatThrownBy(() -> Order.place(CustomerId.random(), List.of(), Instant.now()))
        .isInstanceOf(EmptyOrderException.class);
  }
}
```

## 9. Comandos de qualidade

Maven:

```bash
./mvnw -B spotless:check        # ou checkstyle:check
./mvnw -B verify                # compila, testa, ArchUnit, jacoco
```

Gradle (Java/Kotlin):

```bash
./gradlew spotlessCheck         # ou ktlintCheck / detekt para Kotlin
./gradlew build                 # compila + testes
```

```yaml
      - uses: actions/setup-java@v4
        with:
          distribution: temurin
          java-version: '21'   # ajuste ao LTS definido no ADR
          cache: gradle        # ou maven
      - run: ./gradlew spotlessCheck build --no-daemon
      # Maven: - run: ./mvnw -B spotless:check verify
```

## 10. Checklist de PR (Spring)

- [ ] Nível de arquitetura do ADR respeitado; ArchUnit/Modulith verde
- [ ] Injeção por construtor; nenhum `@Autowired` em campo
- [ ] Controllers finos; regra no domínio/caso de uso
- [ ] `@Entity` não exposta na API; DTOs explícitos com `@Valid`
- [ ] `@Transactional` no lugar certo (caso de uso/adapter, método público)
- [ ] Sem N+1 nas queries novas; `open-in-view` desligado
- [ ] Erros via `@RestControllerAdvice` + `ProblemDetail`
- [ ] Config tipada e validada; nenhum segredo em `application*.yml`
- [ ] Logs com placeholders, sem PII
- [ ] Migração (Flyway/Liquibase) versionada e revisada quando o schema mudou
- [ ] Testes de domínio + slice/integração com Testcontainers
- [ ] Formatter/linter e build verdes
