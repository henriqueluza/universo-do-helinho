# React Native + Expo (Expo Router)

Guia para agentes implementando Issues em apps mobile com Expo e Expo Router. Leia junto com `../clean-architecture.md`, `../solid.md` e `../design-patterns.md`. Versões: use o SDK do Expo estável atual — confirme via Context7 e prefira `npx expo install` para manter dependências compatíveis com o SDK.

## 1. Detecção

- `app.json` ou `app.config.ts`/`app.config.js` com chave `expo`
- `package.json` com `expo`, `react-native`, `expo-router`
- Pasta `app/` com rotas (Expo Router); `eas.json` quando há build via EAS
- `metro.config.js`, `babel.config.js`

Confirme versões:
```bash
node --version
npx expo --version
npm ls expo react-native expo-router typescript --depth=0
npx expo-doctor        # aponta dependências incompatíveis com o SDK
```

## 2. Níveis de arquitetura

Como no web, o mobile raramente precisa de clean completo — regra de negócio pesada fica no backend. O ADR define o nível.

`simple` — apps de conteúdo, formulários, consumo direto de API:
```
app/
  _layout.tsx
  index.tsx
  (tabs)/_layout.tsx
  (tabs)/perfil.tsx
components/
hooks/
lib/            # cliente de API, storage, utils
```

`clean` — apps com várias features, offline, regras locais (financeiro, eventos, carteira). Camadas UI → hooks/view-models → casos de uso/serviços → adapters:
```
app/                         # só rotas e layouts; telas finas que delegam
  _layout.tsx
  (tabs)/eventos/index.tsx
  eventos/[id].tsx
src/
  features/
    events/
      ui/                    # componentes/telas da feature
      hooks/                 # view-models: useEvents, useEventDetail
      application/           # regras puras (filtros, cálculos, validações)
      api/                   # adapter HTTP + mapeamento DTO → modelo
      storage/               # adapter de persistência local (SQLite/MMKV)
      model/
      index.ts               # API pública
  shared/
    ui/                      # design system (tokens, Button, Text)
    lib/                     # http client, i18n, formatadores
    config/
```
`clean+ddd` quase nunca se aplica. Se o ADR pedir, confirme antes.

## 3. Mapeamento Clean Architecture → Expo

| Conceito | Onde fica |
|---|---|
| Entidades/modelos | `features/*/model/` — tipos e funções puras |
| Casos de uso | `features/*/application/` — sem React Native, testáveis em Node |
| Portas | interfaces (`EventsApi`, `EventsStore`) em `api/` e `storage/` |
| Adapters | `api/` (fetch/axios), `storage/` (expo-sqlite, MMKV, SecureStore), módulos nativos (câmera, notificações) |
| Telas/rotas | `app/**` (Expo Router) → renderizam componentes de `features/*/ui/` |
| View-models | custom hooks em `features/*/hooks/` |
| Composition root | `app/_layout.tsx` (providers: QueryClient, tema, sessão, i18n) |

**Separação de navegação:** arquivos em `app/` só declaram rota, params e layout; não contêm regra. Leia params com `useLocalSearchParams`, valide e passe para a tela da feature. Navegação programática centralizada em helpers da feature quando há fluxos complexos (onboarding, checkout).

**APIs nativas** (câmera, localização, push, biometria) ficam atrás de adapters — facilita teste e lidar com permissões negadas.

## 4. Impondo a regra de dependência

`eslint-plugin-boundaries`, igual ao web:
```js
// eslint.config.js (trecho)
settings: {
  'boundaries/elements': [
    { type: 'routes', pattern: 'app/**' },
    { type: 'feature', pattern: 'src/features/*', capture: ['feature'] },
    { type: 'shared', pattern: 'src/shared/**' },
  ],
},
rules: {
  'boundaries/element-types': ['error', { default: 'disallow', rules: [
    { from: 'routes', allow: ['feature', 'shared'] },
    { from: 'feature', allow: ['shared', ['feature', { feature: '${from.feature}' }]] },
    { from: 'shared', allow: ['shared'] },
  ]}],
},
```
Regra adicional: `features/*/application/**` e `model/**` não importam `react-native` nem `expo-*` (`no-restricted-imports`).

## 5. SOLID idiomático

**S — Responsabilidade única.** Violação típica: tela gigante que faz fetch, persiste offline, pede permissão, formata e renderiza lista. Separe em hook (view-model), adapters e componentes de apresentação.

**O — Aberto/fechado.** Variações de comportamento por plataforma: use arquivos `.ios.tsx`/`.android.tsx` ou adapters, não `Platform.OS === 'ios'` espalhado pela lógica.

**L — Substituição.** Componentes do design system que encapsulam `Pressable`/`TextInput` repassam props nativas e de acessibilidade.

**I — Segregação.** Hooks e componentes recebem só o que usam; evite passar `navigation` ou o estado inteiro da feature como prop.

**D — Inversão.** Telas dependem de hooks; hooks dependem de portas, não de `fetch`/`SQLite` direto:
```ts
export function useEvents(api: EventsApi = eventsApi, store: EventsStore = eventsStore) {
  return useQuery({
    queryKey: ['events'],
    queryFn: async () => {
      const events = await api.list();
      await store.saveAll(events);
      return events;
    },
  });
}
```

## 6. Padrões recomendados e quando NÃO usar

| Padrão | Use quando | Evite quando |
|---|---|---|
| Composição + design system em `shared/ui` | Sempre — consistência visual e de acessibilidade | — |
| Custom hooks (view-model) | Estado, efeitos, permissões, ciclo de vida do app | Hook que só embrulha um `useState` |
| Adapter para API e APIs nativas | Sempre que tocar rede, storage ou hardware | — |
| Offline-first (cache persistido + fila de sincronização) | Requisito explícito de uso sem rede (eventos em local sem sinal, coleta em campo) | Não é requisito — adiciona conflito de sync e complexidade |
| TanStack Query para estado de servidor | Dados remotos com cache, refetch, retry | Para estado de UI local |
| Store global (Zustand) | Estado de cliente transversal (sessão, preferências) | Para duplicar cache de servidor |
| Compound components | Componentes com partes coordenadas (BottomSheet, Tabs custom) | Componentes simples |

Anti-padrões comuns:
- Regra de negócio dentro dos arquivos de rota em `app/`.
- `FlatList` com `renderItem` inline pesado e sem `keyExtractor` — ou `ScrollView` para listas longas.
- Token/segredo em `AsyncStorage` (use `expo-secure-store`) ou em variável `EXPO_PUBLIC_*`.
- Dependência nativa instalada com `npm install` em vez de `npx expo install`, quebrando compatibilidade com o SDK.
- `Platform.OS` espalhado pela lógica de negócio.
- Ignorar permissões negadas (o app trava ou fica em branco).

## 7. Erros, validação, configuração e logging

- **Validação:** `zod` para formulários (com `react-hook-form`) e para validar respostas da API no adapter; o backend sempre revalida.
- **Erros:** Error Boundary no layout raiz (Expo Router aceita `export function ErrorBoundary` por rota); adapters convertem erros de rede em erros tipados; a UI trata offline, timeout e 401 de forma explícita.
- **Configuração:** variáveis `EXPO_PUBLIC_*` são embutidas no bundle do app — trate como públicas. **Nunca coloque segredo no app**: tudo que vai no binário pode ser extraído. Chaves privadas ficam no backend; o app chama o backend. Config por ambiente via `app.config.ts` + perfis do `eas.json`.
- **Armazenamento sensível:** tokens em `expo-secure-store`; dados não sensíveis em MMKV/SQLite.
- **Logging:** monitoramento de erros/crash (ex.: Sentry) sem PII; remova `console.log` de produção.

## 8. Testes

- **Unitários (Jest com preset `jest-expo`):** `application/` e `model/` sem dependências nativas.
- **Componentes:** `@testing-library/react-native`, consultando por papel/texto acessível.
- **Rede:** MSW ou fakes das portas nos hooks.
- **E2E:** Maestro (fluxos em YAML, simples de manter) ou Detox nos fluxos críticos (login, compra, criação de evento).

```tsx
it('mostra estado vazio quando não há eventos', async () => {
  const api: EventsApi = { list: async () => [] };
  render(<EventsScreen api={api} />, { wrapper: QueryWrapper });
  expect(await screen.findByText(/nenhum evento/i)).toBeOnTheScreen();
});
```

## 9. Comandos de qualidade

```bash
npx prettier --check .
npx eslint .
npx tsc --noEmit
npx jest --ci
npx expo-doctor
npx expo export --platform all   # garante que o bundle compila
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
      - run: npx jest --ci
      - run: npx expo-doctor
      - run: npx expo export --platform all
```
Builds nativos (EAS Build) e E2E ficam em workflow separado, não no `validate` de todo PR.

## 10. Checklist de PR desta stack

- [ ] Arquivos de `app/` só fazem roteamento/layout; lógica está na feature.
- [ ] Nenhum segredo no app (`EXPO_PUBLIC_*`, código ou assets); tokens em SecureStore.
- [ ] Dependências nativas instaladas via `npx expo install`; `expo-doctor` limpo.
- [ ] Permissões solicitadas no momento certo e caso de negação tratado.
- [ ] Estados de loading, vazio, erro e offline implementados.
- [ ] Listas longas com `FlatList`/`FlashList`, `keyExtractor` e itens memoizados quando necessário.
- [ ] Acessibilidade: `accessibilityLabel`/`accessibilityRole` em elementos interativos, alvos de toque ≥ 44pt, suporte a fonte dinâmica, contraste.
- [ ] Funciona em iOS e Android (ou PR declara qual plataforma foi testada).
- [ ] Testes de comportamento do fluxo alterado; E2E atualizado se o fluxo é crítico.
- [ ] `lint`, `tsc --noEmit`, testes e `expo export` verdes.
