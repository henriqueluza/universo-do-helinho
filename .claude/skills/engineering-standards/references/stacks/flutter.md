# Flutter / Dart

Guia para agentes que implementam Issues em apps Flutter. Leia junto com `../clean-architecture.md`, `../solid.md` e `../design-patterns.md`. Versões: use a versão estável atual do Flutter/Dart e das libs — confirme APIs via Context7 antes de usar algo que você não viu neste repositório.

## 1. Detecção

- `pubspec.yaml` com `flutter:` em `dependencies` → app Flutter. Sem `flutter:` → pacote Dart puro.
- `analysis_options.yaml` → regras de lint do projeto (respeite as existentes).
- Pastas `android/`, `ios/`, `web/`, `macos/` → plataformas habilitadas.
- Gerência de estado: procure `flutter_riverpod`/`hooks_riverpod` ou `flutter_bloc` no `pubspec.yaml`. **Nunca introduza uma segunda lib de estado.**
- Confirmar versões: `flutter --version`, `dart --version`, `flutter pub deps --style=compact`.

## 2. Níveis de arquitetura

O ADR do projeto define o nível. Se não definir, use `simple` e registre a dúvida no handoff.

`simple` — app pequeno, poucas telas, lógica majoritariamente de UI/CRUD remoto:

```
lib/
  main.dart
  app/            # MaterialApp, rotas, tema
  features/
    orders/
      orders_page.dart
      orders_controller.dart   # Notifier/Cubit
      order_api.dart           # chamadas HTTP
      order.dart               # modelo imutável
  shared/         # widgets e utilitários comuns
```

`clean` — regras de negócio relevantes, offline/cache, múltiplas fontes de dados:

```
lib/
  main.dart                    # composition root (ProviderScope / MultiBlocProvider)
  core/                        # erros, Result, utilitários sem Flutter
  features/
    orders/
      domain/                  # entidades, value objects, interfaces de repositório, casos de uso
      data/                    # DTOs, data sources (API, local DB), implementações de repositório
      presentation/            # páginas, widgets, Notifiers/Cubits
```

`clean+ddd` — igual a `clean`, com agregados, value objects ricos e eventos de domínio em `domain/`. Raro em apps mobile; só se o ADR pedir.

## 3. Mapeamento Clean Architecture → Flutter

| Conceito | Onde fica |
|---|---|
| Entidades / value objects | `domain/` — Dart puro, imutáveis, **sem import de `package:flutter`** |
| Casos de uso | `domain/usecases/` — uma classe por ação (`PlaceOrder`), método `call` |
| Portas | `domain/repositories/` — classes `abstract interface class` |
| Adapters | `data/` — `OrderRepositoryImpl`, data sources HTTP (`dio`/`http`), local (`drift`, `shared_preferences`) |
| DTOs / mapeamento | `data/models/` com `fromJson`/`toEntity` |
| Apresentação | `presentation/` — widgets + Notifier (Riverpod) ou Cubit/Bloc |
| DI / composition root | Providers do Riverpod ou `RepositoryProvider`/`BlocProvider` montados em `main.dart` |

**Riverpod vs Bloc — critério:** use o que o projeto já usa. Em projeto novo, o ADR decide:
- **Riverpod**: DI e estado num mecanismo só, menos boilerplate, bom para apps com muitas fontes de dados derivadas.
- **Bloc**: fluxo evento → estado explícito e auditável, bom para times grandes ou fluxos complexos (checkout, pagamentos) em que rastreabilidade de transições importa.

## 4. Impondo a regra de dependência

- `domain/` não importa `package:flutter`, `data/` nem `presentation/`. `presentation/` não importa `data/` diretamente (só via providers/DI).
- Para projetos `clean` grandes, separe em pacotes locais (`packages/orders_domain`) — o `pubspec.yaml` do pacote de domínio simplesmente não declara Flutter, e o compilador impõe a regra.
- Em projeto único, adicione um teste de arquitetura que varre imports:

```dart
// test/architecture_test.dart
import 'dart:io';
import 'package:test/test.dart';

void main() {
  test('domain não depende de flutter/data/presentation', () {
    final files = Directory('lib').listSync(recursive: true)
        .whereType<File>().where((f) => f.path.contains('/domain/'));
    for (final f in files) {
      final src = f.readAsStringSync();
      expect(src, isNot(matches(RegExp(r"import 'package:flutter/|/data/|/presentation/"))),
          reason: f.path);
    }
  });
}
```

## 5. SOLID idiomático

- **S** — Widget só desenha. Lógica (validação, chamadas, formatação de negócio) vai para Notifier/Cubit ou caso de uso. Violação típica: `build()` com `http.get` ou `if` de regra de negócio.
- **O** — Estenda comportamento compondo widgets e injetando estratégias, não com `switch` crescente sobre tipo. `sealed class` + `switch` exaustivo é aceitável para **estados** (o compilador avisa quando falta caso).
- **L** — Implementações de repositório (remoto, cache, fake) devem honrar o contrato: mesmos erros tipados, mesma semântica de vazio vs. erro.
- **I** — Interfaces de repositório pequenas por feature (`OrderReader`, `OrderWriter` se o uso for separado), não um `ApiService` gigante.
- **D** — Notifier/Cubit depende da interface do domínio, recebida por construtor/provider:

```dart
class OrdersNotifier extends AsyncNotifier<List<Order>> {
  @override
  Future<List<Order>> build() => ref.watch(orderRepositoryProvider).fetchAll();
}

final orderRepositoryProvider = Provider<OrderRepository>(
  (ref) => OrderRepositoryImpl(ref.watch(dioProvider)),
);
```

## 6. Padrões recomendados e quando NÃO usar

| Padrão | Use quando | Evite quando |
|---|---|---|
| Repository | Há fonte de dados externa (API, DB local, cache) | Dado é estado puramente de UI |
| Caso de uso (classe `call`) | Regra envolve mais de um repositório ou tem lógica própria | Seria só um repasse de `repo.fetch()` — chame o repositório direto no nível `simple` |
| Imutabilidade (`freezed` ou classes `final` com `copyWith`) | Sempre em entidades e estados | — |
| `sealed class` para estados/erros | Estados com variantes (loading/success/failure) | Um booleano basta |
| Injeção via providers / `RepositoryProvider` | Sempre que um objeto tem dependências | Widgets folha sem dependências |
| Result/Either tipado | Domínio precisa distinguir falhas esperadas | Erros inesperados — deixe propagar e trate no limite |
| Adapter de plataforma | Plugins nativos (câmera, biometria) — esconda atrás de interface | Uso pontual e trivial |

Anti-padrões comuns:
- `setState` gerenciando dados remotos em telas grandes.
- Singletons globais (`static final instance`) em vez de DI — impossibilita teste.
- Widgets de 400+ linhas; extraia subwidgets em vez de métodos `_buildX()`.
- DTO usado como entidade de domínio (JSON vazando para a UI).
- `BuildContext` usado após `await` sem checar `context.mounted`.
- Duas libs de estado no mesmo app.

## 7. Erros, validação, configuração e logging

- Converta exceções de infraestrutura (`DioException`, `SocketException`) em falhas de domínio tipadas em `data/`; a UI só vê falhas de domínio.
- Valide entrada em formulários (`Form` + `validator`) **e** no domínio (value objects que não podem ser criados inválidos).
- Configuração por ambiente com `--dart-define`/`--dart-define-from-file` e flavors. **Nunca** coloque chaves secretas no app: tudo no binário é extraível. Segredos ficam no backend.
- Logging com `logger` ou `dart:developer` `log()`; nada de `print` em código final. Não logue tokens nem dados pessoais.
- Erros não tratados: `FlutterError.onError` + `PlatformDispatcher.instance.onError` encaminhando ao serviço de crash reporting do projeto.

## 8. Testes

- **Unitário** (`package:test`/`flutter_test`): domínio e Notifiers/Cubits — maioria dos testes.
- **Widget** (`testWidgets`): comportamento de tela com repositórios fake/mocks (`mocktail`).
- **Golden**: componentes visuais críticos do design system; rode sempre na mesma plataforma de CI.
- **Integração** (`integration_test/`): fluxos principais ponta a ponta em emulador; poucos.
- `bloc_test` para Cubits/Blocs; `ProviderContainer` com `overrides` para Riverpod.

```dart
test('carrega pedidos', () async {
  final container = ProviderContainer(overrides: [
    orderRepositoryProvider.overrideWithValue(FakeOrderRepository([order1])),
  ]);
  addTearDown(container.dispose);
  expect(await container.read(ordersNotifierProvider.future), [order1]);
});
```

## 9. Comandos de qualidade

```bash
dart format --output=none --set-exit-if-changed .
flutter analyze
flutter test --coverage
dart run build_runner build --delete-conflicting-outputs   # só se usar codegen (freezed, json_serializable, riverpod_generator)
```

Steps para o job `validate`:

```yaml
      - uses: subosito/flutter-action@v2
        with:
          channel: stable
          cache: true
      - run: flutter pub get
      - run: dart format --output=none --set-exit-if-changed .
      - run: flutter analyze
      - run: flutter test --coverage
```

## 10. Checklist de PR

- [ ] `domain/` sem import de `package:flutter`, `data/` ou `presentation/`
- [ ] Nenhuma lib de estado nova introduzida
- [ ] Widgets sem chamadas de rede ou regra de negócio no `build()`
- [ ] Entidades e estados imutáveis
- [ ] Falhas de infraestrutura convertidas em falhas de domínio
- [ ] `context.mounted` checado após `await` que usa `BuildContext`
- [ ] Nenhum segredo no código ou em `--dart-define` versionado
- [ ] Testes unitários do domínio/Notifier e widget test da tela alterada
- [ ] `dart format`, `flutter analyze` e `flutter test` passando
- [ ] Codegen regenerado e commitado (se o projeto versiona arquivos gerados)
