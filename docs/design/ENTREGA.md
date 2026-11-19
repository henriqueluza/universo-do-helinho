# Entrega — tema "Recreio" do painel Universo do Helinho

**Para:** dev responsável pelo painel (`repo/`, ASP.NET Core Razor Pages + Bootstrap).
**De:** design. **Data:** 11/11/2026.

## O que é isto e o que você precisa fazer

O painel foi redesenhado por completo, em uma **preview estática** (`docs/design/recreio/preview/`). Nada foi alterado no `repo/`.
**A implementação no projeto é sua**: portar o CSS, o JS e as imagens, e aplicar as classes nos `.cshtml` existentes.

**Achou algo quebrado, desalinhado ou que não fecha com o código real? Arrume.** A preview é a referência de intenção, não um contrato pixel a pixel. Se a correção mudar uma regra do tema, atualize `docs/design/` junto (seção 6).

Abra a preview com:

```bash
python -m http.server 4173 --bind 127.0.0.1 --directory docs/design/recreio/preview
```

Endereços: `http://127.0.0.1:4173/v2-entrar.html` (login), `/v2.html` (Painel), `/telas.html#alunos` (as demais telas; o botão **Preview** no canto inferior direito troca tema e estados).

---

## 1. Arquivos e para onde vão

| Preview | No repo | Observação |
|---|---|---|
| `recreio.css` + `telas.css` | `src/Helinho.Web/wwwroot/css/theme.css` | **Mantenha o caminho** `/css/theme.css` (os testes `PanelTests` conferem). Concatene os dois, nessa ordem. Já usam `@layer`, então a ordem das regras não briga. |
| `v2.js` | `wwwroot/js/painel.js` (`defer`, no `_Layout.cshtml`) | Remova o que é só da preview: estados `?estado=`, o feed "ao vivo" de exemplo e o envio simulado do login. Fica: tema, contagem dos números, gaveta do menu, gráficos, dicas, entrada das seções. |
| `img/*.webp` | `wwwroot/img/` | Personagens (corpo, carinhas), logos, Helinho. |
| Google Fonts (Baloo 2, Nunito) | `wwwroot/fonts/` + `@font-face` | Autohospedar. A preview usa o CDN só por praticidade. |
| Leaflet 1.9.4 (unpkg, com SRI) | só na página de Unidades | Ver seção 4. É dependência nova: pede Issue própria. |
| `GUIA-DE-EVOLUCAO.md` + `TEMA-PARA-IA.md` | `docs/design/` | Substituem/complementam o `docs/design/DESIGN.md` atual (ele "manda" pelas regras do repo, então precisa refletir o tema novo). |

## 1.1 Inventário completo de imagens

Todas em `preview/img/` → copiar para `wwwroot/img/` **com o mesmo nome** (o CSS e o HTML apontam para esses nomes). Sempre declarar `width` e `height` no `<img>` (evita a página "pular"). Imagem decorativa leva `alt=""`; logo leva o nome da marca.

**Personagens: corpo inteiro** (cartões do Painel, estados vazios, cena)

| Arquivo | Tamanho | Quem | Onde aparece |
|---|---|---|---|
| `helinho-corpo.webp` | 182×360 | Helinho (mascote) | Cena do Painel, estado vazio do feed |
| `helinho.webp` | 320×626 | Helinho | Login (frente da cena), telas de conta |
| `flora-corpo.webp` | 159×300 | Flora · Ambiental | Cartão de tema no Painel, telas |
| `byte-corpo.webp` | 185×300 | Byte · Conhecimento | idem |
| `luna-corpo.webp` | 141×300 | Luna · Cultural | idem |
| `max-corpo.webp` | 170×300 | Max · Esportivo | idem |
| `lia-corpo.webp` | 125×300 | Lia · Diversidade | idem |

**Personagens: carinhas com expressão** (login, seletor de tema do criador)

| Arquivo | Tamanho | Onde aparece |
|---|---|---|
| `cara-helinho.webp` | 312×320 | Login (em foco, na frente). Gerada a partir de `preview/entrega/helinho-cabeca.webp` |
| `cara-flora.webp` | 320×316 | Login (fundo, desfocada) e botão de tema "Ambiental" |
| `cara-byte.webp` | 320×314 | idem, "Conhecimento" |
| `cara-luna.webp` | 313×320 | idem, "Cultural" |
| `cara-max.webp` | 296×320 | idem, "Esportivo" |
| `cara-lia.webp` | 320×320 | idem, "Diversidade" |

**Personagens: miniaturas 160×160** (selos de tema `.tag-type`, avatares, prévia do jogo)

`flora.webp`, `byte.webp`, `luna.webp`, `max.webp`, `lia.webp`, `helinho-rosto.webp` (este último também é o **favicon**).

**Logos**

| Arquivo | Tamanho | Uso |
|---|---|---|
| `logo-jogo.webp` | 480×162 | Logo "Universo do Helinho": menu lateral, topo mobile, login |
| `fundhas.webp` | 480×127 | Logo FUNDHAS no tema claro (rodapé do menu) |
| `fundhas-invertido.webp` | 480×127 | Logo FUNDHAS no tema escuro e no login |
| `fundhas.png`, `fundhas-invertido.png` | 480×127 | Cópias PNG (reserva, para e-mail/PDF). Não usadas nas telas |

**Selos de conquista** (ficha do aluno, `#desempenho-aluno`)

| Arquivo | Usado na preview |
|---|---|
| `selo-amigo-da-natureza.webp`, `selo-curioso-do-mes.webp`, `selo-leitor-da-lia.webp` | sim |
| `selo-cem-perguntas.webp`, `selo-nivel-10-locked.webp`, `selo-visitou-o-atelie-locked.webp` | não (reserva; o sufixo `-locked` é a versão bloqueada) |

**Sem uso:** `vila.webp` (cenário da v1, removido a pedido). Pode ficar fora do repo.

**Originais em alta** (`preview/entrega/`, **não** vão para o `wwwroot`): `helinho.png`, `helinho-cabeca.webp` (1254×1254, gerada para o login), `flora.png`, `byte.png`, `luna.png`, `max.png`, `lia.png`, `logo-universo-do-helinho.png`, `logo-fundhas.png`, `logo-fundhas-invertido.png`. Use-os para gerar versões novas (ex.: um tamanho maior), sempre exportando em WebP.

## 1.2 Fontes, bibliotecas e ícones

| Item | Versão / origem | Onde |
|---|---|---|
| Nunito (500–900) | Google Fonts → autohospedar em `wwwroot/fonts` | Texto de todo o painel (`--font`) |
| Baloo 2 (700, 800) | Google Fonts → autohospedar | Títulos e números grandes (`--font-display`) |
| Bootstrap 5.3 | já está no repo (`wwwroot/lib`) | Base; o tema sobrescreve por camadas |
| Leaflet 1.9.4 | unpkg, com `integrity` (SRI) | Só no mapa de Unidades (proposta) |
| Ícones | SVG em linha, `<symbol>` no `<defs>` do layout, 16×16, traço 1,9, pontas arredondadas | `i-dash`, `i-alunos`, `i-desemp`, `i-quest`, `i-curio`, `i-miss`, `i-event`, `i-unid`, `i-adm`, `i-alert`, `i-x`, `i-go`, `i-menu`, `i-star`, `i-spark`, `i-up`, `i-down`, `i-table`, `i-clock`, `i-target`, `i-play`, `i-moon` (+ os de `telas.html`: `i-shuffle`, `i-pin`, `i-phone`, `i-bulb`, `i-palette`, `i-cap` e demais). Copie os `<symbol>` de `v2.html` e `telas.html` para um partial `_Icones.cshtml` |

## 1.3 Todos os arquivos da entrega

| Caminho | O que é |
|---|---|
| `preview/v2-entrar.html` | Login (estado de erro: `?estado=erro`) |
| `preview/v2.html` | Painel (estados: `?estado=vazio`, `?estado=carregando`) |
| `preview/telas.html` | Todas as outras telas, uma por endereço `#…` (mapa no `GUIA-DE-EVOLUCAO.md`) |
| `preview/recreio.css` | Tokens, base, componentes, casca, Painel, login, gráficos e camada de motion |
| `preview/telas.css` | Componentes das telas internas (tabela, formulário, criador, IA, mapa…) |
| `preview/v2.js` | Comportamento (tema, números, menu, gráficos, dicas, entrada das seções) |
| `preview/img/`, `preview/entrega/` | Imagens (seção 1.1) |
| `preview/index.html`, `estelar.css`, `caderno-estelar.html` | v1 **rejeitada**. Não portar; só histórico |
| `design-system/ENTREGA.md` | Este documento |
| `design-system/GUIA-DE-EVOLUCAO.md` | Regras e receitas para crescer o painel |
| `design-system/TEMA-PARA-IA.md` | Regras para a IA do projeto seguir o tema |

## 2. Ordem sugerida (um PR por linha, dentro do orçamento de tamanho)

1. **Tokens + base + componentes** no `theme.css` + fontes autohospedadas. Nenhuma tela muda de estrutura ainda.
2. **Casca**: menu lateral com ladrilhos, bloco do usuário, gaveta no celular, topo mobile (`_Layout.cshtml`).
3. **Entrar** e telas de conta (recuperar senha, acesso negado).
4. **Painel** (seção 3).
5. **Listas e formulários** que já existem: Alunos, Desempenho, Questões, Missões, Eventos, Unidades (lista), Administradores.
6. **Propostas** (cada uma com Issue + ADR, porque mexem em contrato/domínio): Curiosidades, criador de questões em 3 passos, perguntas do evento, mapa de Unidades, geração por IA.

O CSS inteiro passa de 1.400 linhas; não cabe num PR só. Fatiar por camada (item 1) e por tela resolve.

## 3. Painel — o que é novo e de onde vêm os números

Estrutura (`v2.html`), de cima para baixo:

1. **Cena**: saudação, respostas certas da semana com variação, **barras da semana** (hoje em amarelo), 3 números (alunos ativos, respostas, missões).
2. **Filtros**: faixa etária (chips), unidade, período, botão **Aplicar** (sem `onchange`).
3. **Indicadores** (`.kpis`): taxa de acerto, tempo médio por sessão, sessões por aluno, alunos sem jogar há 7+ dias. Cada um com variação (seta + texto, nunca só cor) e minigráfico (`data-spark="v1,v2,…"`).
4. **Respostas por dia** (linha, 4 semanas vs. 4 anteriores) + **Precisa de atenção**.
5. **Acertos por tipo de quiz** (os 5 personagens).
6. **Questões que mais confundem** (com botão Revisar) + **Unidades que mais jogam** (só Master).
7. **Quando a turma joga** (mapa de calor dia × turno) + **Acabou de acontecer**.

**Como os gráficos funcionam:** o servidor manda **HTML comum** e o JS desenha por cima.
- Linha: o Razor renderiza a `<table id="tab-resp">` (dentro de "Ver em tabela"); o JS lê a tabela e desenha o SVG. Sem JS, a tabela continua lá.
- Minigráficos: atributo `data-spark` com os 7 valores.
- Ranking, barras de acerto e mapa de calor: HTML/CSS puro, com `style="--v:0.42"` (valor de 0 a 1 calculado no servidor).

**Dados de exemplo:** todos os números da preview são sintéticos. Para cada bloco, confira se o `DashboardResult` já tem o dado:
- Já existe: alunos ativos, respostas, acertos por tipo, atividade recente.
- Provavelmente novo (consulta na Application, sem migração se os dados já estão gravados): respostas por dia, ranking de unidades, questões com menor acerto, mapa dia × turno.
- **Tempo médio por sessão** e **sessões por aluno** só se a sessão de jogo tiver início/fim gravados. Se não tiver, **tire esses cartões**; não invente número.

**Contratos que os testes do Painel conferem (não quebre):**
- O texto **"Alunos ativos"** (com A maiúsculo) aparece na página.
- Os 5 rótulos: Ambiental, Conhecimento, Esportivo, Cultural, Diversidade.
- `>Aplicar</button>` existe; **nenhum** `onchange` e **nenhum** `<noscript>`.
- Unidade sem dados mostra **"Nenhuma resposta neste período."** (a preview usa outro texto no estado vazio; mantenha o do repo).
- Menu: `data-bs-target="#menu"`, `aria-controls="menu"`, `href` de cada item, nome/papel/unidade do usuário (`PanelTests`).

## 4. Pontos de atenção

- **Mapa de Unidades:** os tiles `tile.openstreetmap.org` **não servem para produção** (política de uso do OSM). Escolher um provedor com chave (variável de ambiente, nunca no código). Endereços e telefones vieram de fundhas.org.br; 3 pinos são aproximados (`limoeiro`, `polo-cephas`, `campos-sao-jose`, marcados `aprox` no `telas.html`). As coordenadas devem ir para o cadastro da unidade (mudança de entidade → ADR).
- **Criador de questões:** trocar `select` por botões de rádio (`.theme-pick`, `.pill-pick`) muda o HTML que os testes de Questões conferem. Mantenha os `name` do modelo (`Input.Type`, `Input.AgeBand`, `Input.Correct`) e ajuste os testes no mesmo PR.
- **Curiosidades:** banco por tema, **sem dias**. O jogo sorteia uma curiosidade aprovada de cada tema a cada abertura (igual ao carrossel do APK). É tela e entidade novas.
- **Perguntas do evento:** relação `LiveEvent` ↔ `Question` no domínio; as perguntas aparecem no local do evento dentro do jogo.
- **IA:** tudo que for gerado nasce rascunho e passa por Pedir → Revisar → Aprovar. Lilás (`--ai`) é exclusivo de IA.
- **Dica dos gráficos (tooltip):** é montada com `textContent`, sem `innerHTML`. Se mexer, mantenha assim (dados vêm do banco).

## 5. Como conferir antes de cada PR

- [ ] `dotnet format --verify-no-changes`, `dotnet build --no-restore -warnaserror`, `dotnet test --no-build` verdes.
- [ ] Tela em 1440 px e 375 px, **claro e escuro**; sem rolagem horizontal (tabelas largas rolam dentro de `.tscroll`).
- [ ] Campos: `.input` e `.select` de formulário com 48 px, filtros com 40 px, linhas compactas com 36 px (tokens `--h-control-lg`, `--h-control`, `--h-control-sm`). Foco visível, `label` ligado ao campo, erro com `aria-invalid` + `aria-describedby`.
- [ ] Ícones: 16 px no menu e em botões, 18 px em listas de aviso, 14 px em links "Ver mais". Traço 1,9.
- [ ] Com "reduzir movimento" ligado no sistema, nada anima e tudo continua visível.
- [ ] Estados vazio, carregando e erro existem onde fazem sentido.

## 6. Quando entrar algo novo

- Leia `docs/design/GUIA-DE-EVOLUCAO.md` (receitas: lista, formulário, criador, menu, tema de quiz, status, gráfico, animação).
- Para a IA que ajuda no código, aponte o `docs/design/TEMA-PARA-IA.md` no `AGENTS.md` e no `CLAUDE.md` do repo (seção 7).
- Mudou uma regra do tema? Atualize o guia e o arquivo da IA no mesmo PR.

## 7. Ensinar a IA do projeto a seguir o tema

Copie `design-system/TEMA-PARA-IA.md` para `repo/docs/design/` e acrescente, no `AGENTS.md` **e** no `CLAUDE.md` do repo, na seção de design:

```markdown
## Tema do painel (Recreio)
Antes de tocar em `*.cshtml`, `Pages/` ou `wwwroot/css|js`, leia `docs/design/TEMA-PARA-IA.md` e siga-o.
Tela, campo, gráfico ou animação nova seguem as receitas de `docs/design/GUIA-DE-EVOLUCAO.md`.
Não crie cor, tamanho, raio ou duração fora dos tokens de `wwwroot/css/theme.css`.
```

Com isso, Claude Code, Codex ou outro agente leem as regras antes de mexer em UI e continuam o tema sozinhos.
