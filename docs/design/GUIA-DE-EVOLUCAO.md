# Recreio — guia de evolução do sistema de design

Sistema visual do painel do **Universo do Helinho** (FUNDHAS). Este guia diz **onde está cada coisa** e **o que fazer quando algo novo entrar no painel** (tela, campo, status, tema de quiz, conteúdo gerado por IA) sem quebrar a identidade nem os testes.

> Estado: rascunho v2 "Recreio" (preview). Ainda não aplicado ao `repo/`. Quando for, os arquivos de CSS viram `wwwroot/css/theme.css` (o caminho `/css/theme.css` é verificado pelos testes).

---

## 1. Onde está cada coisa

| Arquivo (`docs/design/recreio/preview/`) | Papel |
|---|---|
| `recreio.css` | Tokens, base, componentes, casca (menu, topo), Painel, Entrar e a camada de **motion** |
| `telas.css` | Componentes das telas internas: cabeçalho de página, tabela, paginação, formulário, alternativas, IA, ficha, curiosidade, telas de conta |
| `v2.js` | Tema claro/escuro, contagem dos números, gaveta do menu no celular, gráficos (linha, minigráficos), dicas e entrada das seções ao rolar |
| `v2.html` | Painel (dashboard) |
| `v2-entrar.html` | Entrar (com estado de erro `?estado=erro`) |
| `telas.html` | Todas as outras telas, uma por endereço (`telas.html#questoes`, `#curiosidades`…) |
| `img/` | Personagens (`*-corpo.webp` corpo inteiro, `cara-*.webp` carinhas com expressão), logos, selos |

Ordem das camadas CSS: `tokens → base → components → screens → motion`. Regra nova entra **na camada certa**; nunca use `!important` para ganhar de outra camada.

### Mapa de telas

| Menu | Telas (rascunho) | Situação no repo |
|---|---|---|
| Painel | `v2.html` (+ `?estado=vazio`, `?estado=carregando`): cena, filtros, indicadores, respostas por dia, atenção, temas, questões que confundem, ranking de unidades, mapa de calor, feed | existe; **blocos novos precisam de consultas novas** (ver `ENTREGA.md` §3) |
| Alunos | `#alunos`, `#aluno-novo` | existe |
| Desempenho | `#desempenho`, `#desempenho-aluno` | existe |
| Questões | `#questoes`, `#questao-editar` (criador em 3 passos), **`#questoes-ia`** | lista/edição existem; **criador, "Onde aparece" e IA são propostas** |
| **Curiosidades** | **`#curiosidades`, `#curiosidades-ia`, `#curiosidade-editar`** | **nova (proposta)**. Banco por tema, sem dias: a cada abertura o jogo sorteia uma aprovada de cada tema (carrossel `curiosities-carousel` do app) |
| Missões | `#missoes`, `#missao-editar` | existe |
| Eventos | `#eventos`, **`#evento-editar`** (dados + tema + **perguntas do evento**) | lista/form existem; **perguntas próprias do evento são proposta** (exige `LiveEvent` ↔ `Question` no domínio) |
| Unidades (Master) | `#unidades` (**mapa da rede**), `#unidade-nova` | lista existe; **mapa é proposta**. 18 espaços reais (fundhas.org.br/unidades), números do jogo de exemplo |
| Administradores (Master) | `#administradores`, `#admin-novo` (com erro) | existe |
| Conta | `v2-entrar.html`, `#recuperar-senha`, `#acesso-negado` | existe |

---

## 2. Regras que não mudam

1. **Token, nunca valor solto.** Cor, espaço, raio, fonte e duração vêm de variável (`var(--…)`). Se o valor não existe, crie o token antes (seção 4).
2. **Personagem = tema de quiz.** Flora = Ambiental, Byte = Conhecimento, Luna = Cultural, Max = Esportivo, Lia = Diversidade. Helinho é o mascote (não representa tema). Toda vez que um tema aparece, use a classe do tema (`.amb .con .cul .esp .div`) — ela entrega `--q` (cor), `--qb` (fundo) e `--qi` (texto).
3. **Azul FUNDHAS é ação; amarelo é destaque/estrela.** No tema escuro a ação vira amarela. Não use o azul como fundo decorativo nem o amarelo em texto sobre branco.
4. **Lilás (`--ai`) é exclusivo de IA.** Botão "Gerar com IA", selo "Sugerida pela IA", painel de geração. Nada mais usa essa cor — é assim que a equipe reconhece conteúdo de máquina.
5. **IA sugere, a equipe decide.** Todo conteúdo gerado nasce **Rascunho** e passa por **Pedir → Revisar → Aprovar**. Só aprovado chega ao jogo. Editar um aprovado devolve a rascunho (mesma regra que já existe para questões).
6. **Contraste AA.** Texto pequeno ≥ 4,5:1 nos dois temas. `--ink-3` é o cinza mais claro permitido para texto.
7. **Movimento com função e opcional.** Toda animação nova entra em `@layer motion` dentro de `@media (prefers-reduced-motion: no-preference)`.
8. **Sem dado pessoal em lista.** CPF, RG e endereço só aparecem no formulário de edição do aluno (os testes verificam isso).

---

## 3. Receitas: "vamos adicionar…"

### 3.1 …uma tela nova de lista (ex.: "Turmas")
1. Copie uma seção de `telas.html` que já é lista (ex.: `#unidades`).
2. Estrutura fixa: `.page-head` (h1 + `.sub` + `.page-actions`) → `.filters-bar` (se houver filtro, sempre com botão **Aplicar**, sem `onchange`) → `.panel.panel-pad` com `.table-wrap > .table` → `.pager`.
3. Primeira coluna é `<th scope="row">` (nome do item). Números à direita com `.num`. Ações à direita com `.actions` e `.link-btn`.
4. Status sempre como selo: `.tag-ok` (positivo), `.tag-draft` (rascunho), `.tag-bad` (pendente/problema), `.tag-neutral` (encerrado/inativo).
5. Escreva o **estado vazio** junto (texto + ação), com `id="<tela>-vazio"` — é o padrão dos testes.

### 3.2 …um formulário novo
1. `form.panel.panel-pad.form` com `novalidate`, `.form-grid` (2 colunas; `.full` ocupa a linha toda; vira 1 coluna no celular).
2. Cada campo: `.field` > `label` + `.input`/`.select.select-lg`/`.textarea` + `small` de ajuda.
3. Erro: resumo `#resumo-erros` (`.alert`, `role="alert"`, `tabindex="-1"`, com link para o campo) + no campo `aria-invalid="true"` e `aria-describedby="Input_X-erro"`. **Não mude esses ids** — os testes dependem deles.
4. Botões: primário à esquerda (`Salvar`/`Criar…`), `Cancelar` como `.btn` comum.

### 3.3 …um conteúdo gerado por IA (como Curiosidades)
1. Na lista: botão `.btn.btn-ai` "Gerar com IA" antes do botão primário; aviso `.note.ai` quando houver sugestões esperando revisão.
2. Tela de geração: `.ai-panel` com `.ai-steps` (Pedir · Revisar · Aprovar/Agendar) e os parâmetros (tema, faixa, quantidade/dias).
3. Revisão: um `.rcard` por sugestão com `.tag-ai`, tema e faixa; ações **Aprovar · Editar · Descartar** (descartar anima a saída).
4. Na lista principal, marque a origem com `.tag-ai` "IA" e permita filtrar por **Origem**.

### 3.3.1 …uma tela de criação (padrão "criador")
Toda criação de conteúdo do jogo usa o mesmo molde, para ser fácil e previsível:
1. `.maker` = `.maker-form` (passos) + `.maker-preview` (celular com a prévia do jogo, fixa ao rolar).
2. Cada passo é `fieldset.step` com `legend.step-h` (`.n` numerado + pergunta simples: "Qual é o tema?", "Para quem?", "Escreva…"). No máximo 3 passos.
3. Tema = `.theme-pick` (personagens como botões de rádio). Opções curtas = `.pill-pick`. Nada de `select` para escolhas com até ~6 opções.
4. IA dentro do passo de escrita: `.ai-help` com "Sugerir"; o que a IA preenche pisca em lilás (`.ai-filled`) e mostra `.ai-flag`. Nunca salva sozinha.
5. Prévia ligada por atributos: campo `data-bind="x"` → elemento `data-show="x"` (com `data-empty` para o texto-guia). Checklist `.ready` com `data-check`; a `.maker-bar` (fixa embaixo) diz quantos itens faltam.
6. No repo: os rádios mantêm os `name` do modelo (`Input.Type`, `Input.AgeBand`, `Input.Correct`). **Atenção:** trocar `select` por rádio muda o HTML que os testes conferem; ajustar os testes junto.

### 3.4 …um item no menu
1. Ícone: SVG 16×16, traço 1,9, cantos arredondados, no `<defs>` (mesmo estilo dos outros).
2. Ladrilho: cada item tem uma cor **só dele** (`--tile-1…9`). Precisa de mais? Crie `--tile-10` em `tokens` com luminosidade entre 0,47 e 0,8 e contraste do ícone branco ≥ 3:1.
3. Agrupe em "Conteúdo do jogo" ou "Gestão". Itens só de Master ficam em "Gestão".
4. No repo, o item é registrado em `Panel/NavMenu.cs` — o teste de menu verifica o `href` de cada item.

### 3.5 …um tema de quiz novo (ex.: "Saúde")
1. Domínio primeiro (enum `QuizType` no repo, com ADR — é mudança de contrato).
2. Tokens: `--q-sau`, `--q-sau-bg`, `--q-sau-ink` nos dois temas (matiz diferente dos 5 atuais; confira contraste do `-ink` sobre o `-bg`).
3. Classe `.sau { --q: …; --qb: …; --qi: … }` na camada `components`.
4. Personagem: precisa de **corpo** (`img/<nome>-corpo.webp`, ~300px de altura) e **carinha** (`img/cara-<nome>.webp`, 320px, fundo transparente). Sem personagem, o tema não entra no painel.
5. Atualize: cartões do Painel (`.cast`), filtros "Tipo de quiz", selects dos formulários e este guia.

### 3.6 …um status novo (ex.: "Reprovada")
Escolha um selo existente pelo **significado**, não pela cor. Precisa de um significado novo? Crie o par `--x-bg/--x-ink` nos dois temas e a classe `.tag-x`. Documente aqui.

### 3.6.1 …um gráfico ou indicador
1. Forma pelo trabalho do dado: número único → `.kpi` (com `.delta` e `data-spark`); tempo → linha (`.linechart` lendo uma tabela); comparação → `.rank`; dia × horário → `.heat`.
2. Cor: só `--data` (+ `--data-ref` tracejado para comparação). **As cores dos temas não são séries** (falham no teste de daltonismo); tema = rótulo + personagem.
3. Um eixo, grade de 1 px, texto em `--ink-*`, legenda com 2+ séries, dica no hover, tabela "Ver em tabela".
4. O servidor manda os números em HTML; o JS só desenha. Dica com `textContent`, nunca `innerHTML`.

### 3.7 …uma animação
Pergunte: ela **orienta, dá retorno ou conta algo**? Se não, não entra. Use `--ease-out` (entradas), `--ease-pop` (personagens, conquistas), `--dur-*` e `--stagger`. Anime só `opacity`, `translate`, `scale`, `rotate`, `filter`, `clip-path`.

---

## 4. Tokens essenciais

| Grupo | Tokens | Uso |
|---|---|---|
| Superfície | `--canvas` `--stage` `--sky` `--sunny` `--rule` `--rule-strong` | fundo da página, cartões, céu do topo, destaque quente, réguas de 2px |
| Texto | `--ink` `--ink-2` `--ink-3` | principal, secundário, apoio (mínimo AA) |
| Ação | `--action` `--action-lip` `--action-hover` `--on-action` | botão primário (azul FUNDHAS; amarelo no escuro) |
| Seleção | `--select-bg` `--select-line` `--select-ink` | item ativo do menu, chip selecionado, foco de input |
| Estado | `--ok*` `--bad*` `--warn*` `--neutral*` | selos e avisos |
| Tema de quiz | `--q-amb/con/cul/esp/div` (+ `-bg`, `-ink`) | sempre via classe do tema |
| IA | `--ai` `--ai-bg` `--ai-ink` | só conteúdo de IA |
| Gráfico | `--data` `--data-ref` `--data-wash` `--grid` | séries, comparação, área, grade |
| Menu | `--tile-1…9` | um por item |
| Tipo | `--font` (Nunito) `--font-display` (Baloo 2) `--fs-xs…--fs-hero` | Baloo só em títulos e números grandes |
| Forma | `--sp-1…12` `--r-sm/md/lg/xl/pill` `--h-control-sm/-/-lg` (36/40/48) `--lip` | espaço, raio, altura de controle, "lábio" do botão |
| Movimento | `--ease` `--ease-out` `--ease-pop` `--dur-fast/dur/dur-slow/dur-enter` `--stagger` | zerados em reduced-motion |

---

## 5. Checklist antes de aprovar uma mudança visual

- [ ] Só tokens; nenhum hex/oklch solto fora de `@layer tokens`.
- [ ] Testado em 1440 px e 375 px, claro e escuro; sem rolagem horizontal.
- [ ] Estados desenhados: vazio, carregando, erro (quando houver).
- [ ] Contraste AA conferido nos textos pequenos.
- [ ] Animação nova dentro de `prefers-reduced-motion: no-preference`.
- [ ] Ids e textos que os testes verificam continuam iguais (lista no `docs/` do repo e no inventário de telas).
- [ ] Conteúdo de IA com selo lilás e passando por revisão humana.
- [ ] Este guia e o mapa de telas atualizados.
- [ ] Revisão por alguém que não construiu (crítico independente da `frontend-doctor`, meta ≥ 4,3).
