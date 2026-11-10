# React + Next.js (App Router, frontend web)

Guia para agentes implementando Issues em apps Next.js com App Router. Leia junto com `../clean-architecture.md`, `../solid.md` e `../design-patterns.md`. Versões: use a versão estável atual — confirme via Context7 (APIs do Next mudam entre majors; não confie em memória).

## 1. Detecção

- `next.config.js` / `next.config.mjs` / `next.config.ts`
- `package.json` com `next`, `react`, `react-dom`
- Pasta `app/` (App Router) — se existir só `pages/`, é Pages Router: siga o padrão já existente e não migre sem Issue
- `tsconfig.json`, `tailwind.config.*` / `postcss.config.*` quando houver

Confirme versões:
```bash
node --version
npx next --version
npm ls next react typescript --depth=0
```

## 2. Níveis de arquitetura

Frontend raramente precisa de clean completo. Regra de negócio pesada mora no backend; o front orquestra UI, estado e chamadas. O ADR define o nível.

`simple` — sites institucionais, landing pages, dashboards finos:
```
app/
  layout.tsx
  page.tsx
  (marketing)/sobre/page.tsx
components/        # UI compartilhada
lib/               # fetchers, utils, cliente de API
```

`clean` — produto com várias features, regras de UI relevantes (checkout, onboarding, carteira). Organização por feature, camadas UI → hooks/view-models → casos de uso/serviços → adapters de API:
```
app/                          # só rotas: page/layout/loading/error finos
  (app)/orders/page.tsx
  (app)/orders/[id]/page.tsx
src/
  features/
    orders/
      ui/                     # componentes da feature
      hooks/                  # view-models: useOrders, useCheckout
      application/            # casos de uso/serviços puros (cálculos, regras de UI)
      api/                    # adapter: chamadas HTTP/Server Actions, mapeamento DTO → modelo
      model/                  # tipos/modelos da feature
      index.ts                # API pública da feature
  shared/
    ui/                       # design system
    lib/                      # http client, formatadores
    config/
```
`clean+ddd` praticamente nunca se aplica ao frontend. Se o ADR pedir, confirme antes.

## 3. Mapeamento Clean Architecture → Next.js

| Conceito | Onde fica |
|---|---|
| Entidades/modelos | `features/*/model/` — tipos e funções puras |
| Casos de uso | `features/*/application/` — funções puras ou serviços sem React |
| Portas | interfaces em `features/*/api/` (ex.: `OrdersApi`) |
| Adapters | `features/*/api/` — `fetch`, SDKs, Server Actions; mapeiam DTO → modelo |
| Controllers/telas | `app/**/page.tsx` (Server Components) e componentes em `features/*/ui/` |
| View-models | custom hooks em `features/*/hooks/` (Client Components) |
| Composition root | `app/layout.tsx` + providers (`QueryClientProvider`, tema, sessão) |

**Server vs Client Components:**
- Padrão é Server Component: busca dados no servidor, não envia JS ao cliente.
- `'use client'` só onde há estado, efeitos, eventos do browser ou hooks de cliente — empurre a fronteira para as folhas da árvore.
- Server Component pode renderizar Client Component; o inverso só via `children`/props.
- Código que usa segredos fica em módulos server-only (`import 'server-only'`).

**Data fetching:**
- Leitura inicial: no Server Component (`await` direto no componente async) ou via função do adapter.
- Mutações: Server Actions ou route handlers (`app/api/**/route.ts`), sempre validando entrada no servidor.
- Estado de servidor no cliente (polling, cache, otimista): TanStack Query. Estado de UI local: `useState`/`useReducer`. Estado global de cliente só se necessário (Zustand/Context) — não duplique cache de servidor em store global.

## 4. Impondo a regra de dependência

`eslint-plugin-boundaries` com features isoladas: uma feature só importa outra pela API pública (`index.ts`).
```js
// eslint.config.mjs (trecho)
settings: {
  'boundaries/elements': [
    { type: 'app', pattern: 'app/**' },
    { type: 'feature', pattern: 'src/features/*', capture: ['feature'] },
    { type: 'shared', pattern: 'src/shared/**' },
  ],
},
rules: {
  'boundaries/element-types': ['error', { default: 'disallow', rules: [
    { from: 'app', allow: ['feature', 'shared'] },
    { from: 'feature', allow: ['shared', ['feature', { feature: '${from.feature}' }]] },
    { from: 'shared', allow: ['shared'] },
  ]}],
},
```
Complemente com `no-restricted-imports` proibindo importar `features/*/ui/**` ou `features/*/api/**` de fora da feature.

## 5. SOLID idiomático

**S — Responsabilidade única.** A violação típica é o componente gigante (300+ linhas) que busca dados, valida formulário, formata valores e renderiza. Separe: hook (estado/efeitos), funções puras (regras), componentes de apresentação.

**O — Aberto/fechado.** Extenda por composição (`children`, slots, render props), não por props booleanas acumuladas (`isCompact`, `isDark`, `hasIcon`...).

**L — Substituição.** Componentes que encapsulam elementos nativos repassam as props nativas (`...props`, `ref`) e respeitam o comportamento esperado (um `Button` continua sendo `<button>` acessível).

**I — Segregação de interfaces.** Props mínimas: passe `order.total`, não o objeto `order` inteiro, se o componente só usa o total.

**D — Inversão de dependência.** Componentes e hooks não chamam `fetch` com URL solta; dependem do adapter da feature, que pode ser trocado em teste:
```tsx
// hooks/useOrders.ts
export function useOrders(api: OrdersApi = ordersApi) {
  return useQuery({ queryKey: ['orders'], queryFn: () => api.list() });
}
```

## 6. Padrões recomendados e quando NÃO usar

| Padrão | Use quando | Evite quando |
|---|---|---|
| Composição (`children`, slots) | Variações visuais e de layout | — |
| Custom hooks (view-model) | Estado + efeitos reutilizáveis ou para tirar lógica do JSX | Hook que só embrulha um `useState` |
| Compound components | Componentes com partes coordenadas (Tabs, Select, Accordion) | Componentes simples sem estado compartilhado |
| Container/presentational | Componente precisa ser testado/visualizado sem dados reais | Como regra geral — hooks já resolvem |
| Adapter de API | Sempre que houver backend externo; isola DTO e erros | — |
| Server Actions | Mutações simples ligadas a formulários | Quando a API precisa ser consumida por outros clientes (use route handler/backend) |
| Store global (Zustand/Context) | Estado de cliente realmente transversal (tema, carrinho local) | Para cache de dados do servidor (use TanStack Query/RSC) |

Anti-padrões comuns:
- `'use client'` no `layout.tsx` ou no topo da árvore inteira.
- `useEffect` para buscar dados que poderiam vir do Server Component.
- Prop drilling por 4+ níveis em vez de composição ou contexto local.
- Segredo/API key em variável `NEXT_PUBLIC_*` ou em Client Component.
- Lógica de negócio crítica só no front (preço, permissão) sem revalidação no servidor.
- `div` clicável no lugar de `button`/`a`.

## 7. Erros, validação, configuração e logging

- **Validação:** `zod` compartilhado entre formulário (com `react-hook-form`) e servidor; Server Action/route handler sempre revalida — validação do cliente é só UX.
- **Erros:** `error.tsx` por segmento de rota (é Client Component), `not-found.tsx`, e `global-error.tsx`. Adapters convertem erro HTTP em erro tipado; a UI mostra mensagem amigável, nunca o erro cru.
- **Configuração:** só variáveis com prefixo `NEXT_PUBLIC_` vão para o bundle do cliente — trate-as como públicas. Segredos ficam em variáveis sem prefixo, usadas apenas em código de servidor. Valide env no build/boot com zod:
```ts
// src/shared/config/env.server.ts
import 'server-only';
import { z } from 'zod';
export const env = z.object({
  API_URL: z.string().url(),
  API_SECRET: z.string().min(1),
}).parse(process.env);
```
- **Logging:** no servidor, logger estruturado; no cliente, ferramenta de monitoramento de erros (sem PII). Nada de `console.log` esquecido.

## 8. Testes

- **Unitários (Vitest ou Jest):** funções puras de `application/` e `model/`.
- **Componentes:** Testing Library — teste comportamento pelo que o usuário vê (`getByRole`), não detalhes de implementação.
- **Mock de rede:** MSW no adapter, não mock do hook.
- **E2E:** Playwright nos fluxos críticos (login, checkout), incluindo checagem básica de acessibilidade (`@axe-core/playwright`).

```tsx
it('desabilita o botão enquanto envia', async () => {
  render(<CheckoutButton onPay={() => new Promise(() => {})} />);
  await userEvent.click(screen.getByRole('button', { name: /pagar/i }));
  expect(screen.getByRole('button', { name: /pagar/i })).toBeDisabled();
});
```

## 9. Comandos de qualidade

```bash
npx prettier --check .
npx eslint .
npx tsc --noEmit
npx vitest run        # ou: npm test
npm run build
npx playwright test   # E2E, quando configurado
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
      - run: npm test -- --run
      - run: npm run build
```

## 10. Checklist de PR desta stack

- [ ] `'use client'` apenas nos componentes que precisam; dados buscados no servidor quando possível.
- [ ] Nenhum segredo em `NEXT_PUBLIC_*`, Client Component ou bundle.
- [ ] Entradas de Server Actions/route handlers validadas no servidor.
- [ ] Features importam outras só pela API pública (`index.ts`); lint de boundaries verde.
- [ ] Componentes com responsabilidade clara; lógica extraída para hooks/funções puras.
- [ ] Estados de loading, vazio e erro implementados (`loading.tsx`/`error.tsx` ou equivalentes).
- [ ] Acessibilidade: elementos semânticos, `label` em inputs, foco visível, navegação por teclado, contraste, `alt` em imagens.
- [ ] Imagens com `next/image` e dimensões definidas; sem layout shift visível.
- [ ] Testes de comportamento para o fluxo alterado; E2E atualizado se o fluxo é crítico.
- [ ] `lint`, `tsc --noEmit`, testes e `build` verdes.
