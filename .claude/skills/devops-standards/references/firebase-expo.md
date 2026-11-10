# Firebase e Expo EAS

Backend Firebase e builds/distribuição de apps React Native com Expo. Leia junto com `ci-cd.md`, `environments.md` e `observability.md`. CLIs e opções do Firebase e do EAS mudam com frequência: **confirme na documentação atual via Context7**.

**Política da casa:** agentes nunca publicam. Após o merge humano em `main`, o CI publica em **staging**; **produção** só por disparo manual humano (`workflow_dispatch` do workflow de produção, ver `ci-cd.md`) no GitHub Environment `production`. **Envio às lojas (`eas submit`) é sempre humano.**

---

## Parte 1 — Firebase

### 1. Um projeto Firebase por ambiente

| Ambiente | Projeto Firebase | Uso |
|---|---|---|
| local | Emulator Suite | desenvolvimento e testes, sem custo nem dado real |
| staging | `reservas-staging` | deploy automático após merge |
| production | `reservas-prod` | deploy só por disparo manual humano |

```json
// .firebaserc
{ "projects": { "staging": "reservas-staging", "production": "reservas-prod" } }
```

Nunca use um projeto só com "coleções de teste": rules, cotas e dados são do projeto inteiro.

### 2. O que fica versionado

```
firebase.json            # hosting, functions, emuladores
firestore.rules          # Security Rules
firestore.indexes.json   # índices compostos
storage.rules
functions/               # código das Cloud Functions (ver guia da stack)
```

- **Índices** só via `firestore.indexes.json`. Índice criado pelo console tem que ser trazido para o arquivo (`firebase firestore:indexes`) no mesmo dia.
- **Security Rules** são código de produção: revisadas como código e **com testes**.

### 3. Security Rules testadas no CI

```js
// rules.test.ts (com @firebase/rules-unit-testing)
const alice = env.authenticatedContext("alice");
await assertFails(getDoc(doc(alice.firestore(), "reservas/da-bia")));
await assertSucceeds(getDoc(doc(alice.firestore(), "reservas/da-alice")));
```

```yaml
- run: npx firebase emulators:exec --only firestore,storage "npm run test:rules"
```

Mínimo de testes por coleção: dono acessa, outro usuário é negado, não autenticado é negado, campos protegidos (ex.: `role`, `saldo`) não podem ser escritos pelo cliente. Regra `allow read, write: if true` fora do emulador = bloqueante.

### 4. Deploy via CI

```yaml
deploy-staging:
  environment: staging
  steps:
    - run: npx firebase deploy --project staging --only firestore:rules,firestore:indexes,storage,functions,hosting
```

- Autenticação: Workload Identity Federation do Google Cloud (OIDC, `google-github-actions/auth`) com service account de deploy por projeto. Evite chave JSON de service account; se inevitável, só como secret do Environment.
- Produção: workflow separado disparado manualmente (`workflow_dispatch`), com `environment: production` e `--project production`.
- Deploy parcial (`--only`) explícito; nunca `firebase deploy` sem alvo.

### 5. Segurança e custo

- **App Check** ativado (Play Integrity / App Attest / reCAPTCHA) e com enforcement em Firestore, Storage e Functions chamáveis em produção.
- **Segredos das Functions** via Secret Manager (`defineSecret` / `firebase functions:secrets:set`, este último feito por humano). Nunca em `.env` commitado nem em config de cliente.
- A config do app web (`apiKey` etc.) é pública por design: a proteção são Rules + App Check, não o segredo da chave.
- **Alertas de orçamento** no Cloud Billing de cada projeto; limites de instâncias nas Functions; cuidado com listeners sem `limit()` e loops de trigger (function que escreve no documento que a dispara).

---

## Parte 2 — Expo EAS

### 6. Perfis no `eas.json`

```json
{
  "cli": { "appVersionSource": "remote" },
  "build": {
    "development": { "developmentClient": true, "distribution": "internal", "environment": "development" },
    "preview":     { "distribution": "internal", "channel": "preview", "environment": "preview" },
    "production":  { "channel": "production", "environment": "production", "autoIncrement": true }
  },
  "submit": { "production": {} }
}
```

| Perfil | Para quê | Quem dispara |
|---|---|---|
| development | dev client para o time | humano/agente localmente |
| preview | build interno de QA (staging) | CI após merge |
| production | build para as lojas | CI, disparo manual humano |

### 7. Variáveis e segredos

- Variáveis por ambiente nas **EAS environment variables** (`development`, `preview`, `production`), com visibilidade adequada (secret quando sensível).
- `EXPO_PUBLIC_*` vai **para dentro do bundle**: só URLs públicas, chaves publicáveis e flags. **Nunca segredo** (chave de API privada, token de serviço).
- Segredo de verdade fica no backend; o app chama o backend.

### 8. Builds e updates no CI

```yaml
- uses: expo/expo-github-action@<sha> # vX — pine por SHA (ci-cd.md)
  with:
    eas-version: "<versão fixa do eas-cli>"
    token: ${{ secrets.EXPO_TOKEN }}
- run: eas build --profile preview --platform all --non-interactive --no-wait
```

- `EXPO_TOKEN` (token de robô/conta de CI) como secret do Environment.
- **EAS Update (OTA)** por canal: `eas update --channel preview` automático após merge; `--channel production` só no workflow de produção disparado por humano.
- **OTA só para JS e assets.** Mudou dependência nativa, plugin de config, permissões, `app.json` nativo ou SDK do Expo → **build nova**, nunca OTA.
- `runtimeVersion` com policy (`appVersion` ou `fingerprint`) para garantir que um update nunca chegue a um binário incompatível.

### 9. Versionamento e credenciais

- `appVersionSource: "remote"` + `autoIncrement` no perfil production: o EAS controla `buildNumber`/`versionCode`. A versão de marketing (`version`) muda em PR `chore` próprio.
- Credenciais de assinatura (keystore, certificados, provisioning) **gerenciadas pelo EAS**; nunca commitadas.
- **`eas submit` às lojas: somente humano**, depois de testar o build de produção.

## O que agentes podem e não podem fazer

| Pode | Não pode |
|---|---|
| Escrever Rules, índices, `firebase.json`, `eas.json` e workflows | `firebase deploy` em qualquer projeto real |
| Rodar Emulator Suite e testes de Rules | `firebase functions:secrets:set`, alterar config no console |
| `eas build --profile development` local | `eas build`/`eas update` em `preview`/`production` fora do CI |
| Propor channel, runtimeVersion e perfis no PR | `eas submit`, mexer em credenciais de assinatura |
| Definir variáveis públicas `EXPO_PUBLIC_*` não sensíveis | Colocar segredo em `EXPO_PUBLIC_*` ou no código do app |

## Checklist de PR

- [ ] Mudança de Rules acompanhada de testes no emulador (dono, outro usuário, anônimo, campos protegidos).
- [ ] Índices novos em `firestore.indexes.json`.
- [ ] Deploy com `--only` e projeto explícito; produção só via workflow de disparo manual.
- [ ] Segredos de Functions via Secret Manager; nenhum segredo em `EXPO_PUBLIC_*`.
- [ ] App Check considerado para endpoints novos.
- [ ] Mudança nativa → build nova (não OTA); `runtimeVersion` coerente.
- [ ] Perfis/canais corretos (`preview` = staging, `production` = disparo manual).
- [ ] Impacto de custo avaliado (listeners, triggers, instâncias).
