# DESIGN.md — Painel do Universo do Helinho

**Issue:** #3 · **Escopo:** painel administrativo (Razor Pages + Bootstrap 5.3). Não cobre o jogo Unity, mascotes nem a identidade do jogo.
**Consumidor:** a Issue 1d copia a seção 9 (tokens) para o arquivo de tema. Este documento não contém código executável do projeto.

## 1. Direção

Ferramenta de trabalho da equipe pedagógica e administrativa de uma fundação (crianças de 6 a 14 anos). Leitura rápida de números, formulários longos, listas com filtro. O painel é sóbrio e claro; a cor do jogo fica nos tipos de quiz, não na moldura.

- **Estilo (`design-taste-frontend --app`, kind `smb-admin`, seed 1650224535): "Accent ladder".** Um matiz de marca em força total só em marcas pequenas (botão primário, série de dado, ícone do item ativo de menu). Os tons pálidos desse matiz fazem o trabalho de área (selos, trilhas de progresso, destaque de filtro).
- **Contorno "Open plane":** menu lateral sobre o fundo da página, sem barra superior, cartões brancos sobre fundo off-white, sem sombra em repouso, densidade espaçosa com um único valor de espaçamento entre blocos.
- **`ui-ux-pro-max`** sugeriu glassmorphism escuro com Fira Code. **Descartado:** vidro e blur reduzem contraste de texto, e fundo escuro mais fonte monoespaçada não combinam com formulários longos lidos por horas. Aproveitado: a recomendação de tipografia para painéis de dados (sem serifa, números tabulares) e a checklist (ícones SVG, `cursor: pointer`, foco visível, `prefers-reduced-motion`).
- **Anti-padrões (não fazer):** gradiente, glassmorphism, borda em todo contêiner, pílulas decorativas, rótulos em caixa-alta espaçada, segunda família de acento, barra de acento no item ativo além do tom, emoji como ícone, mascotes no painel.

## 2. Cor

Um acento (framboesa). O restante é neutro. As cores dos tipos de quiz (seção 3) são categorias de dado, não acentos de interface: aparecem só em selos, séries de gráfico e marcas pequenas de tipo.

| Papel | Token | Valor | Uso |
|---|---|---|---|
| Fundo da página | `--ground` | `#f9fafb` | atrás de tudo |
| Superfície | `--surface` | `#ffffff` | cartões, tabelas, formulários |
| Elevado | `--raised` | `#f3f4f6` | cabeçalho de tabela, linha em hover |
| Borda | `--border` | `#e5e7eb` | contorno de cartão e divisor de linha |
| Régua | `--rule` | `#f2f2f2` | divisor entre linhas de tabela |
| Borda de controle | `--control` | `#8a919c` | contorno de input, select, checkbox (3,18:1 sobre branco) |
| Texto | `--text` | `#111827` | corpo e títulos |
| Texto secundário | `--muted` | `#5f6672` | legendas, rótulos de apoio |
| Acento | `--accent` | `#b8336a` | botão primário, link, série de dado, ícone ativo |
| Acento suave | `--accent-soft` | `#fdf2f7` | item ativo do menu, linha selecionada |
| Acento trilha | `--accent-track` | `#f8dbe8` | trilha de barra de progresso |
| Tinta do acento | `--accent-ink` | `#6b1d3e` | texto sobre `--accent-soft`/`--accent-track` |
| Positivo | `--positive` | `#247b50` | variação para cima, sucesso |
| Negativo | `--negative` | `#c4242a` | erro, ação destrutiva |
| Atenção | `--warning` | `#92400e` | aviso (texto e ícone; nunca só a cor) |

### Contraste (WCAG 2.2 AA) — razões calculadas

| Par | Razão | Mínimo | Resultado |
|---|---|---|---|
| `--text` / `--ground` | 16,98 | 4,5 | passa |
| `--muted` / `--ground` | 5,54 | 4,5 | passa |
| `--muted` / `--surface` | 5,78 | 4,5 | passa |
| branco / `--accent` (botão primário) | 5,65 | 4,5 | passa |
| `--accent` / `--surface` (link, texto) | 5,65 | 4,5 | passa |
| `--accent` / `--ground` | 5,40 | 4,5 | passa |
| `--accent-ink` / `--accent-soft` | 10,28 | 4,5 | passa |
| `--negative` / `--surface` | 5,78 | 4,5 | passa |
| `--positive` / `--surface` | 5,22 | 4,5 | passa |
| `--control` / `--surface` (borda de input) | 3,18 | 3 (1.4.11) | passa |
| tinta / fundo de cada selo (seções 3 e 5) | 6,84 a 9,37 | 4,5 | passa |

Regras:
1. Texto normal ≥ 4,5:1; texto grande (≥ 24 px, ou ≥ 18,66 px em negrito) e componentes de interface (borda de input, ícone que carrega significado, anel de foco) ≥ 3:1.
2. `--border` (`#e5e7eb`) é decorativa: nunca é a única indicação de um campo. O campo usa `--control`.
3. Cor nunca é o único sinal: status, erro e tipo de quiz sempre vêm com texto (e ícone quando útil).
4. Todo par novo de texto/fundo precisa de razão calculada antes de entrar no tema.
5. Alvos de toque/clique ≥ 24×24 px (WCAG 2.5.8); no painel, controles têm 32 px de altura mínima.

## 3. Cores dos cinco tipos de quiz (definidas uma única vez)

Fonte única: o `quiz_type` (`docs/context/rotas-api.md`). A cor nunca vem no payload da API; no painel, todo componente que colore por tipo (selo, série de gráfico, marca em tabela, filtro, cartão de indicador) lê **somente** estes tokens. Nenhuma tela escreve hexadecimal de tipo.

| `quiz_type` | Rótulo no painel | Personagem | Marca `--quiz-*` | Fundo `--quiz-*-bg` | Tinta `--quiz-*-ink` | Tinta/fundo |
|---|---|---|---|---|---|---|
| `ambiental` | Ambiental | Flora | `#2f7d4f` | `#dcfce7` | `#14532d` | 8,30 |
| `conhecimento` | Conhecimento | Byte | `#2563a8` | `#dbeafe` | `#1e3a8a` | 8,49 |
| `cultural` | Cultural | Luna | `#7a4fa3` | `#f3e8ff` | `#581c87` | 9,22 |
| `esportivo` | Esportivo | Max | `#c2570c` | `#ffedd5` | `#7c2d12` | 8,18 |
| `diversidade` | Diversidade | Lia | `#0f7f86` | `#ccfbf1` | `#134e4a` | 8,41 |

- **Marca** (círculo/quadrado de 8–10 px, barra de gráfico, linha de série): razão sobre branco entre 4,50 e 6,12, ou seja, ≥ 3:1 como componente gráfico.
- **Selo de tipo:** fundo `-bg`, texto `-ink`, rótulo escrito. Sem ícone de personagem.
- **Gráfico com cinco séries:** use a marca e rotule as séries diretamente (ou legenda com texto); nunca dependa de distinguir só pela cor.
- A framboesa do acento não coincide com nenhum tipo; `--warning` (marrom-âmbar) só aparece com texto, para não se confundir com o laranja de `esportivo`.
- O mapeamento `quiz_type` → token é uma tabela única no tema (Issue 1d); código C# só envia o nome do tipo para o tema (`data-quiz="ambiental"` ou classe `.quiz-ambiental`).

## 4. Tipografia

**DM Sans** (400, 500, 600, 700), com `Inter, system-ui, sans-serif` como reserva. Autohospedar (LGPD: sem requisição a Google Fonts em produção); o link do Google Fonts serve só para prototipar.

| Papel | Token | Tamanho | Peso |
|---|---|---|---|
| Título da página | `--fs-title` | 32 px (cai para 24 px abaixo de 576 px) | 700, só aqui |
| Seção / título de cartão | `--fs-section` | 18 px | 600 |
| Número de indicador | `--fs-kpi` | 24 px | 600 |
| Corpo, tabela, formulário | `--fs-body` | 14 px, entrelinha 1,5 | 400 (500 em rótulos) |
| Legenda, selo, ajuda | `--fs-small` | 12 px | 500 |

- `font-variant-numeric: tabular-nums` em toda tabela, indicador e número que atualiza.
- Texto em frase normal (sentence case); sem caixa-alta espaçada, sem itálico decorativo, sem kerning negativo.
- Tamanho mínimo de texto de leitura: 12 px (apenas legenda/selo); texto de formulário e tabela nunca abaixo de 14 px.
- O usuário pode ampliar o texto em 200 % sem perda de conteúdo (usar `rem`/`em` no tema; nada de altura fixa em texto).

## 5. Espaçamento, raios, densidade

- **Escala (base 4 px):** 4 · 8 · 12 · 16 · 24 · 32 · 48. Um único valor entre blocos (`--gap: 24px`) na horizontal e na vertical; preenchimento interno de cartão `--pad: 24px`.
- **Raios:** cartão `8px`, controle (input, botão, select) `6px`, selo `999px` (único uso de pílula: selos de status/tipo). Raio interno = raio externo − padding.
- **Sombra:** nenhuma em repouso. Só menu suspenso, modal e toast têm sombra (`0 8px 24px rgb(17 24 39 / .12)`).
- **Alturas:** controle `32px`, linha de tabela `45px`, item de menu `32px` em passo de `34px`.
- **Largura:** menu lateral `230px`; conteúdo fluido com margem de 24 px (`16px` em tela estreita).
- **Movimento:** transições CSS de 120–160 ms (cor, fundo, borda). Sem animação decorativa. `prefers-reduced-motion: reduce` desliga qualquer transição não essencial.

## 6. Estados

| Estado | Regra |
|---|---|
| **Foco** | Anel visível em todo elemento interativo: `outline: 2px solid var(--accent); outline-offset: 2px` (≥ 3:1). Nunca `outline: none` sem substituto. Foco não pode ficar escondido por menu fixo (WCAG 2.4.11). |
| **Hover** | Fundo `--raised` em linha e item neutro; botão primário escurece 8 %. Hover nunca é o único indicador. |
| **Selecionado/ativo** | Item de menu: fundo `--accent-soft`, texto `--accent-ink`, ícone `--accent`, `aria-current="page"`. Sem barra lateral. |
| **Desabilitado** | Opacidade 0,55, `cursor: not-allowed`, atributo `disabled`; o motivo aparece em texto de ajuda quando não for óbvio. |
| **Erro de campo** | Borda e ícone `--negative`, mensagem de 12–14 px abaixo do campo em `--negative`, ligada por `aria-describedby`, campo com `aria-invalid="true"`. A mensagem diz o que corrigir ("Informe o CPF com 11 dígitos"). |
| **Resumo de erros** | Formulário longo mostra, no topo, um aviso `role="alert"` com links para os campos inválidos; o foco vai para ele após o envio. |
| **Erro de página/sistema** | Cartão com título, causa em linguagem simples e ação ("Tentar de novo"). Não expor detalhes técnicos nem dados pessoais. |
| **Vazio** | Cartão com uma frase do que falta e a ação que resolve ("Nenhuma questão para esta faixa etária. Criar questão"). Lista filtrada sem resultado oferece "Limpar filtros". |
| **Carregando** | Esqueleto de linha/cartão com a altura final (sem salto de layout) e `aria-busy="true"`; indicador giratório só em botão de envio, que também fica desabilitado contra duplo clique. |
| **Sucesso** | Aviso `role="status"` após salvar, em `--positive`/`--pill-positive-*`, que some em ≥ 6 s ou por ação do usuário. |
| **Destrutivo** | Excluir/inativar pede confirmação em modal com o nome do item; botão em `--negative`. |

## 7. Padrões dos componentes do painel

Sempre sobre Bootstrap 5.3 (classes do framework + variáveis do tema); sem biblioteca de componentes extra.

### 7.1 Menu lateral
- 230 px, sobre `--ground`, uma linha `--border` à direita. Sem barra superior.
- **Topo:** nome do painel (texto, 18 px/600) e a **unidade** do usuário em `--muted` (para o master: "Todas as unidades").
- **Itens:** ícone SVG de 16 px (traço 1,5 px) + rótulo 14 px; altura 32 px, passo 34 px; grupos com rótulo cinza de 12 px em frase normal e 24–40 px de ar acima. Item ativo conforme seção 6.
- **Visibilidade por papel:** Unidades e Administradores só aparecem para o master.
- **Base (empurrada para baixo):** bloco do usuário com **nome**, **papel** (Master / Administrador de unidade) e **unidade**, mais "Sair". É o requisito B1 do PRD.
- **Telas < 768 px:** vira barra de 48 px com o nome da tela e botão "Menu" que abre uma folha em tela cheia com os mesmos itens e o bloco do usuário.
- Um link "Pular para o conteúdo" é o primeiro foco da página.

### 7.2 Cartão de indicador
- Fundo `--surface`, borda 1 px `--border`, raio 8 px, `--pad` 24 px, sem sombra.
- Estrutura: rótulo (`--fs-small`, `--muted`) → número (`--fs-kpi`, 600, tabular) → contexto de uma linha (período ou variação, com seta e texto, não só cor).
- Por tipo de quiz: uma marca `--quiz-*` de 8 px antes do rótulo e o nome do tipo em texto.
- Barra de progresso: trilha `--accent-track`, preenchimento `--accent` (ou `--quiz-*` quando for por tipo), valor em texto ao lado.
- Grade de 1–4 colunas, espaçamento `--gap`. O filtro de faixa etária da página recalcula todos os cartões (B2).

### 7.3 Tabela com filtros
- Barra de filtros acima da tabela, dentro do mesmo cartão, em uma linha que quebra em telas estreitas. **Faixa etária em destaque:** é o primeiro filtro, sempre visível, como grupo de botões segmentados com **Todas · 6–7 · 8–10 · 11–12 · 13–14** (o selecionado usa `--accent-soft`/`--accent-ink`, `aria-pressed="true"`). Os demais filtros (unidade, turma, tipo de quiz, status) são `select` de 32 px à direita; filtro de unidade só para o master.
- Filtros aplicados aparecem na URL (GET, links compartilháveis) e há "Limpar filtros".
- Tabela: cabeçalho `--raised` 12 px/600; linhas 45 px com divisor `--rule`; hover `--raised`; números alinhados à direita e tabulares; colunas de texto à esquerda. Ação de linha por menu "⋯" com rótulo acessível ou links de texto.
- Ordenação indicada por seta e `aria-sort`. Paginação abaixo, com "Mostrando 1–20 de N".
- Em telas estreitas a tabela rola na horizontal dentro do cartão (`.table-responsive`), mantendo a primeira coluna legível.
- Faixa etária e tipo aparecem como selos (seções 3 e 7.5).

### 7.4 Formulário
- Coluna única de até 640 px para campos de texto; agrupar em seções com título de 18 px (Dados pessoais, Endereço, Responsável, Acesso). Formulários longos (cadastro de aluno) usam seções numeradas por título, não abas, para permitir revisar tudo.
- Rótulo sempre visível acima do campo (14 px/500), nunca só placeholder. Obrigatório marcado com texto "(obrigatório)", não só asterisco. Ajuda de 12 px em `--muted` abaixo do campo.
- Controle de 32 px (`--control-height`), borda `--control`, raio 6 px, fundo `--surface`. Seleção de data com campo nativo.
- Ações no fim: botão primário "Salvar" (um por tela) e secundário "Cancelar" (contorno). Botão primário a `--accent`, texto branco.
- Erros e resumo conforme seção 6. Campos pessoais (CPF, RG) com `autocomplete="off"`, máscara apenas de apresentação e dado sintético em exemplos.
- Questão: 4 alternativas (rótulos A–D), marcação da correta com `radio` e rótulo "Resposta correta", campo de explicação e seletor de status.

### 7.5 Selos de status e de tipo
Selo = pílula de `--fs-small`/500, altura 22 px, padding 2×8 px, ponto/ícone de 8 px opcional, **sempre com texto**.

| Contexto | Texto | Fundo / tinta | Razão |
|---|---|---|---|
| Questão | Rascunho | `--pill-neutral-bg` `#f3f4f6` / `--pill-neutral-ink` `#374151` | 9,37 |
| Questão | Aprovada | `--pill-positive-bg` `#dcfce7` / `--pill-positive-ink` `#14532d` | 8,30 |
| Aprovação (aluno/cadastro) | Aprovado | `--pill-positive-*` | 8,30 |
| Aprovação (aluno/cadastro) | Pendente | `--pill-warning-bg` `#fffbeb` / `--pill-warning-ink` `#92400e` | 6,84 |
| Falha/erro | Reprovado, Erro | `--pill-negative-bg` `#ffedea` / `--pill-negative-ink` `#831c1e` | 8,63 |
| Destaque de marca | Selecionado, Novo | `--pill-accent-bg` `#fdf2f7` / `--pill-accent-ink` `#6b1d3e` | 10,28 |
| Tipo de quiz | nome do tipo | `--quiz-*-bg` / `--quiz-*-ink` | 8,18 a 9,22 |
| Faixa etária | 6–7, 8–10… | `--pill-neutral-*` (a faixa não tem cor própria) | 9,37 |

Regra: rascunho/aprovada (questão) e aprovado/pendente (aluno) usam vocabulário diferente de propósito. Não misturar "Aprovada" com "Aprovado".

## 8. Acessibilidade e responsividade (resumo)

- Landmarks: `nav`, `main`, `header` por página; um único `h1` por tela; ordem de títulos sem saltos.
- Todo ícone sem texto tem `aria-label`; ícone decorativo tem `aria-hidden="true"`.
- Navegação completa por teclado; ordem do foco segue a ordem visual.
- Tabela com `caption` (ou `aria-label`) e `th scope`.
- Breakpoints do Bootstrap: layout pensado para 360 px, 768 px e ≥ 1200 px; sem rolagem horizontal da página (só dentro de `.table-responsive`).
- Mensagens em português do Brasil, linguagem simples, sem jargão técnico.
- Dados de exemplo e capturas só sintéticos.

## 9. Tokens prontos para o tema (Issue 1d)

### 9.1 Variáveis CSS do tema

```css
:root {
  /* superfície e texto */
  --ground: #f9fafb;
  --surface: #ffffff;
  --raised: #f3f4f6;
  --border: #e5e7eb;
  --rule: #f2f2f2;
  --control: #8a919c;
  --text: #111827;
  --muted: #5f6672;

  /* acento (um só) */
  --accent: #b8336a;
  --accent-hover: #a22e5f;
  --accent-soft: #fdf2f7;
  --accent-track: #f8dbe8;
  --accent-ink: #6b1d3e;

  /* semânticas */
  --positive: #247b50;
  --negative: #c4242a;
  --warning: #92400e;

  /* selos */
  --pill-positive-bg: #dcfce7; --pill-positive-ink: #14532d;
  --pill-negative-bg: #ffedea; --pill-negative-ink: #831c1e;
  --pill-warning-bg:  #fffbeb; --pill-warning-ink:  #92400e;
  --pill-neutral-bg:  #f3f4f6; --pill-neutral-ink:  #374151;
  --pill-accent-bg:   #fdf2f7; --pill-accent-ink:   #6b1d3e;

  /* tipos de quiz: definidos aqui, uma vez */
  --quiz-ambiental:         #2f7d4f; --quiz-ambiental-bg:         #dcfce7; --quiz-ambiental-ink:         #14532d;
  --quiz-conhecimento:      #2563a8; --quiz-conhecimento-bg:      #dbeafe; --quiz-conhecimento-ink:      #1e3a8a;
  --quiz-cultural:          #7a4fa3; --quiz-cultural-bg:          #f3e8ff; --quiz-cultural-ink:          #581c87;
  --quiz-esportivo:         #c2570c; --quiz-esportivo-bg:         #ffedd5; --quiz-esportivo-ink:         #7c2d12;
  --quiz-diversidade:       #0f7f86; --quiz-diversidade-bg:       #ccfbf1; --quiz-diversidade-ink:       #134e4a;

  /* forma e medidas */
  --radius-card: 8px;
  --radius-control: 6px;
  --radius-pill: 999px;
  --row-height: 45px;
  --sidebar-width: 230px;
  --gap: 24px;
  --pad: 24px;
  --control-height: 32px;
  --shadow-overlay: 0 8px 24px rgb(17 24 39 / .12);
  --transition: 140ms ease;

  /* tipografia */
  --font-sans: "DM Sans", Inter, system-ui, -apple-system, "Segoe UI", sans-serif;
  --fs-title: 2rem;      /* 32px */
  --fs-section: 1.125rem; /* 18px */
  --fs-kpi: 1.5rem;      /* 24px */
  --fs-body: 0.875rem;   /* 14px */
  --fs-small: 0.75rem;   /* 12px */
}

@media (max-width: 575.98px) {
  :root { --fs-title: 1.5rem; --gap: 16px; --pad: 16px; }
}

@media (prefers-reduced-motion: reduce) {
  :root { --transition: 0ms; }
}
```

Mapeamento quiz → token (único, no tema): `.quiz-<tipo> { --quiz: var(--quiz-<tipo>); --quiz-bg: var(--quiz-<tipo>-bg); --quiz-ink: var(--quiz-<tipo>-ink); }` para os cinco valores de `quiz_type`. Componentes leem `--quiz`, `--quiz-bg`, `--quiz-ink`.

### 9.2 Sobrescrita das variáveis do Bootstrap 5.3

Carregar **depois** do `bootstrap.css`. Variáveis `--bs-*` globais:

```css
:root {
  --bs-body-font-family: var(--font-sans);
  --bs-body-font-size: var(--fs-body);
  --bs-body-line-height: 1.5;
  --bs-body-color: #111827;
  --bs-body-color-rgb: 17, 24, 39;
  --bs-body-bg: #f9fafb;
  --bs-body-bg-rgb: 249, 250, 251;
  --bs-secondary-color: #5f6672;
  --bs-secondary-bg: #f3f4f6;
  --bs-tertiary-bg: #f3f4f6;
  --bs-emphasis-color: #111827;

  --bs-primary: #b8336a;
  --bs-primary-rgb: 184, 51, 106;
  --bs-primary-text-emphasis: #6b1d3e;
  --bs-primary-bg-subtle: #fdf2f7;
  --bs-primary-border-subtle: #f8dbe8;
  --bs-success: #247b50;
  --bs-success-rgb: 36, 123, 80;
  --bs-success-text-emphasis: #14532d;
  --bs-success-bg-subtle: #dcfce7;
  --bs-danger: #c4242a;
  --bs-danger-rgb: 196, 36, 42;
  --bs-danger-text-emphasis: #831c1e;
  --bs-danger-bg-subtle: #ffedea;
  --bs-warning: #92400e;
  --bs-warning-rgb: 146, 64, 14;
  --bs-warning-text-emphasis: #92400e;
  --bs-warning-bg-subtle: #fffbeb;
  --bs-secondary: #5f6672;
  --bs-secondary-rgb: 95, 102, 114;

  --bs-link-color: #b8336a;
  --bs-link-color-rgb: 184, 51, 106;
  --bs-link-hover-color: #a22e5f;
  --bs-link-hover-color-rgb: 162, 46, 95;

  --bs-border-color: #e5e7eb;
  --bs-border-radius: 6px;
  --bs-border-radius-sm: 4px;
  --bs-border-radius-lg: 8px;
  --bs-border-radius-pill: 999px;
  --bs-focus-ring-color: rgba(184, 51, 106, .35);
  --bs-focus-ring-width: 3px;
  --bs-box-shadow: none;
  --bs-box-shadow-sm: none;
}
```

Variáveis por componente (Bootstrap 5.3 expõe no próprio seletor; sobrescrever no tema):

```css
.btn-primary {
  --bs-btn-bg: #b8336a;            --bs-btn-border-color: #b8336a;
  --bs-btn-color: #ffffff;
  --bs-btn-hover-bg: #a22e5f;      --bs-btn-hover-border-color: #a22e5f;
  --bs-btn-active-bg: #8f2955;     --bs-btn-active-border-color: #8f2955;
  --bs-btn-disabled-bg: #b8336a;   --bs-btn-disabled-border-color: #b8336a;
  --bs-btn-focus-shadow-rgb: 184, 51, 106;
}
.btn-outline-secondary {
  --bs-btn-color: #111827;         --bs-btn-border-color: #8a919c;
  --bs-btn-hover-bg: #f3f4f6;      --bs-btn-hover-color: #111827;
  --bs-btn-hover-border-color: #8a919c;
}
.btn { --bs-btn-padding-y: .25rem; --bs-btn-padding-x: .75rem; --bs-btn-font-size: .875rem; --bs-btn-border-radius: 6px; min-height: 32px; }
.form-control, .form-select { border-color: #8a919c; min-height: 32px; font-size: .875rem; background-color: #fff; }
.form-control:focus, .form-select:focus { border-color: #b8336a; box-shadow: 0 0 0 3px rgba(184, 51, 106, .35); }
.form-control.is-invalid, .form-select.is-invalid { border-color: #c4242a; }
.card { --bs-card-bg: #fff; --bs-card-border-color: #e5e7eb; --bs-card-border-radius: 8px; --bs-card-spacer-y: 24px; --bs-card-spacer-x: 24px; --bs-card-cap-bg: #f3f4f6; }
.table { --bs-table-bg: transparent; --bs-table-border-color: #f2f2f2; --bs-table-hover-bg: #f3f4f6; --bs-table-striped-bg: transparent; --bs-table-cell-padding-y: .75rem; }
.table > thead { --bs-table-bg: #f3f4f6; font-size: .75rem; font-weight: 600; }
.nav-pills { --bs-nav-pills-link-active-bg: #fdf2f7; --bs-nav-pills-link-active-color: #6b1d3e; --bs-nav-link-color: #111827; --bs-nav-link-hover-color: #111827; --bs-nav-link-padding-y: .25rem; --bs-nav-pills-border-radius: 6px; }
.badge { --bs-badge-font-size: .75rem; --bs-badge-font-weight: 500; --bs-badge-padding-x: .5rem; --bs-badge-padding-y: .125rem; --bs-badge-border-radius: 999px; }
.progress { --bs-progress-bg: #f8dbe8; --bs-progress-bar-bg: #b8336a; --bs-progress-height: .5rem; --bs-progress-border-radius: 999px; }
.alert { --bs-alert-border-radius: 8px; }
.modal { --bs-modal-border-radius: 8px; --bs-modal-box-shadow: 0 8px 24px rgb(17 24 39 / .12); }
:focus-visible { outline: 2px solid #b8336a; outline-offset: 2px; }
```

Observações para a 1d:
- Compilar via Sass é opcional; esta sobrescrita em CSS puro basta e evita nova dependência (contrato congelado: sem pacote novo).
- Selos usam `.badge` com `--pill-*` (ex.: `.badge.badge-status-approved { background: var(--pill-positive-bg); color: var(--pill-positive-ink); }`); não usar `.bg-success` puro (branco sobre `#247b50` dá 5,22, passa, mas o selo suave é o padrão do painel).
- Validar contraste de qualquer variante nova com a fórmula WCAG antes de entrar (regra 4 da seção 2).
- Autohospedar DM Sans (WOFF2, 400/500/600/700) em `wwwroot`; sem CDN de fonte.

## 10. Registro do protocolo de design

| Etapa | Resultado |
|---|---|
| `design-references` | Não se aplica (tela de app). |
| `ui-ux-pro-max` | Busca `--design-system` para "admin dashboard education children foundation"; resultado glassmorphism escuro descartado (seção 1); aproveitadas tipografia de painel e checklist. |
| `design-taste-frontend --app` | `draw.mjs --app --kind smb-admin` (seed 1650224535): estilo "Accent ladder", casco "Open plane"; matiz framboesa mantido como sorteado; tokens do sorteio adotados, com `--control` escurecido (3,18:1) por exigência do WCAG 1.4.11 e a escala de contraste recalculada. |
| Restantes (impeccable, gsap, lenis, unslop, web-design-guidelines) | Etapas de implementação; entram na 1d, que gera o tema. Este PR é só documento. |
