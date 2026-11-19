# Tema "Recreio" — regras para agentes de IA

Você (Claude Code, Codex ou outro agente) vai mexer na interface do painel do **Universo do Helinho**. Leia este arquivo inteiro **antes** de editar `*.cshtml`, `Pages/`, `wwwroot/css` ou `wwwroot/js`. Ele vale mais que seu gosto pessoal. Em caso de dúvida, copie o padrão de uma tela que já existe.

Referências: `docs/design/GUIA-DE-EVOLUCAO.md` (receitas) e a preview em `docs/design/recreio/preview/` (`v2.html`, `telas.html`).

## 1. Arquitetura do CSS

- Um arquivo: `wwwroot/css/theme.css`. Camadas na ordem `@layer tokens, base, components, screens, motion;`.
- Enquanto houver tela não portada, abaixo dessas ficam `@layer bootstrap, legado;`: o Bootstrap entra por `@import url("../lib/bootstrap/bootstrap.min.css") layer(bootstrap)` (nunca por `<link>`) e o tema antigo fica em `@layer legado`, a remover na limpeza. Nada fora de camada. Detalhes em `docs/handoff/123.md`.
- Regra nova entra **na camada certa**: valor → `tokens`; peça reutilizável → `components`; algo de uma tela → `screens`; animação → `motion`.
- **Proibido:** `!important`, cor em hex/rgb/oklch fora de `@layer tokens`, `style=""` com cor ou tamanho fixo (exceção: variáveis de dado como `style="--v:.42"`), estilos em `<style>` dentro de página.
- Tema escuro: `:root[data-theme="dark"]` redefine os **mesmos tokens**. Nunca escreva regra de componente só para o escuro se um token resolve.

## 2. Tokens (use sempre `var(--…)`)

| Para | Token |
|---|---|
| Fundo da página / cartão | `--canvas` / `--stage` |
| Réguas e bordas (2 px) | `--rule`, `--rule-strong` |
| Texto | `--ink` (principal), `--ink-2` (secundário), `--ink-3` (apoio; o mais claro permitido) |
| Ação (botão primário) | `--action`, `--action-lip`, `--action-hover`, `--on-action` (azul FUNDHAS no claro, amarelo no escuro) |
| Selecionado / foco | `--select-bg`, `--select-line`, `--select-ink`, `--focus` |
| Estados | `--ok*`, `--bad*`, `--warn*`, `--neutral*` (sempre em par `-bg` + `-ink`) |
| Tema de quiz | classes `.amb .con .cul .esp .div` → `--q`, `--qb`, `--qi` |
| IA (exclusivo) | `--ai`, `--ai-bg`, `--ai-ink` |
| Gráficos | `--data` (série principal), `--data-ref` (comparação, tracejada), `--data-wash` (área), `--grid` |
| Espaço | `--sp-1…--sp-12` (4, 8, 12, 16, 20, 24, 32, 40, 48 px) |
| Raio | `--r-sm` 8 · `--r-md` 12 · `--r-lg` 20 · `--r-xl` 28 · `--r-pill` |
| Altura de controle | `--h-control-sm` 36 · `--h-control` 40 · `--h-control-lg` 48 |
| Fonte | `--font` (Nunito, texto) · `--font-display` (Baloo 2, só títulos e números grandes) · `--fs-xs…--fs-hero` |
| Movimento | `--ease`, `--ease-out`, `--ease-pop`, `--dur-fast`, `--dur`, `--dur-slow`, `--dur-enter`, `--stagger` |

Precisa de um valor que não existe? **Crie o token** (claro e escuro) e documente no guia. Não use número solto.

## 3. Identidade

1. **Personagem = tema de quiz:** Flora = Ambiental, Byte = Conhecimento, Luna = Cultural, Max = Esportivo, Lia = Diversidade. Helinho é o mascote e não representa tema.
2. Tema aparece sempre com **nome + personagem** (`.tag.tag-type.<tema>` com a miniatura). Nunca só a cor.
3. Azul FUNDHAS = ação. Amarelo = estrela/destaque/"hoje". Lilás = **só IA**.
4. Botão tem "lábio" (borda inferior mais grossa) e afunda ao clicar. Não crie botão sem `.btn`.
5. Superfícies: `.panel` (borda 2 px + sombra curta). Títulos de seção em `.panel-head` com `h2` (Baloo) e `p` de apoio.

## 4. Campos de texto, selects, rádios e checkboxes

- Todo campo dentro de `.field`: `<label for>` + controle + `<small>` de ajuda. **Label sempre visível**, nunca só placeholder.
- Alturas: formulário `.input` / `.select.select-lg` = 48 px; barra de filtros = 40 px; linha compacta (IA, cartão de revisão, alternativas) = 36–40 px. Botão ao lado de um campo tem **a mesma altura** do campo.
- Estados obrigatórios (já no tema; não sobrescreva): hover (borda mais escura), foco (borda `--select-line` + anel 4 px `--select-bg`), desabilitado (fundo `--canvas`, borda tracejada), `readonly`, erro (`aria-invalid="true"`, borda `--bad`, mensagem com `id="<campo>-erro"` ligada por `aria-describedby`).
- Formulário com erro: resumo `#resumo-erros` (`.alert`, `role="alert"`, `tabindex="-1"`) com link para cada campo. **Ids e textos que os testes conferem não mudam.**
- Escolha com até ~6 opções: rádio estilizado (`.theme-pick` para temas, `.pill-pick` para opções curtas). Mais que isso: `.select`. Rádio/checkbox nativo usa `accent-color: var(--action)`.
- Filtros não enviam sozinhos: sempre botão **Aplicar**, sem `onchange`, sem `<noscript>`.
- Sem dado pessoal (CPF, RG, endereço) em lista; só no formulário de edição do aluno.

## 5. Gráficos e indicadores

- Escolha a forma pelo trabalho do dado: número único → cartão `.kpi`; evolução no tempo → linha; comparação entre itens → barras horizontais (`.rank`); dia × horário → mapa de calor (`.heat`); poucos itens com meta → `.meter`.
- **Uma cor de dado** (`--data`) + cinza tracejado para comparação (`--data-ref`). As 5 cores dos temas **não** servem como séries (reprovam no teste de daltonismo). Quando o gráfico é por tema, a identidade vem do rótulo + personagem.
- Um eixo só. Nunca dois eixos Y. Grade de 1 px em `--grid`. Texto do gráfico sempre em `--ink-*`, nunca na cor da série.
- Toda série tem legenda (`.legend`) quando há 2 ou mais; dica ao passar o mouse; navegação por setas no gráfico de linha; **tabela com os números** ("Ver em tabela", `.chart-table`).
- O servidor manda HTML (tabela, `data-spark`, `style="--v:0..1"`); o JS só desenha. Sem JS, os números continuam visíveis.
- Variação sempre com seta + texto (`.delta.good/.bad/.flat`), nunca só cor. Status (ok/ruim) não é cor de série.
- Dica (tooltip): montar com `textContent`. **Nunca `innerHTML` com dado do banco.**

## 6. Ícones

- SVG em linha via `<use href="#i-nome">`, 16×16, `stroke-width` 1,9, `stroke-linecap/linejoin: round`, `fill: none`, cor `currentColor`.
- Tamanhos: 16 px (menu, botões), 18 px (avisos, listas), 14 px (links "Ver mais", setas de variação). Ícone novo segue o mesmo estilo e entra no `<defs>` do layout.
- Ícone sozinho em botão precisa de `aria-label`. Ícone decorativo: `aria-hidden="true"`.

## 7. Movimento

- Toda animação em `@layer motion`, dentro de `@media (prefers-reduced-motion: no-preference)`. Com redução ligada, nada anima e **tudo fica visível**.
- Só anime `opacity`, `translate`, `scale`, `rotate`, `filter`, `clip-path`. Nunca `width`, `height`, `top`, `left`.
- Cada animação precisa de uma função: orientar (entrada em cascata), dar retorno (botão, seleção), contar algo (números contando, barras crescendo, linha desenhando). Sem função, não entra.
- Curvas: `--ease-out` em entradas, `--ease-pop` em personagens e conquistas. Durações dos tokens; cascata com `--stagger`.
- Troca de página: View Transitions já configuradas (`nav-ativo`, `conteudo`, `rail`, `logo-jogo`, `helinho`). Não dê o mesmo `view-transition-name` a dois elementos visíveis.

## 8. Layout e responsivo

- Larguras de referência: 1440 px e 375 px. Sem rolagem horizontal; tabela larga rola dentro de `.tscroll`.
- Grade de duas colunas: `.two` (vira uma coluna abaixo de 1240 px). Indicadores: `.kpis` (4 → 2 colunas).
- Menu vira gaveta abaixo de 768 px (`data-bs-target="#menu"`, `aria-controls="menu"` são conferidos por teste).

## 9. Receitas rápidas

- **Tela de lista:** `.page-head` → `.filters-bar` (com Aplicar) → `.panel.panel-pad` com `.table-wrap > .table` → `.pager` → estado vazio `id="<tela>-vazio"`.
- **Formulário:** `form.panel.panel-pad.form` `novalidate` → `.form-grid` → `.field` → `.form-actions` (primário à esquerda, Cancelar comum).
- **Criação de conteúdo do jogo:** padrão "criador" (`.maker`: até 3 passos `fieldset.step` + prévia no celular `.maker-preview` + checklist `.ready` + barra fixa `.maker-bar`).
- **Conteúdo de IA:** botão `.btn-ai`, selo `.tag-ai`, fluxo Pedir → Revisar → Aprovar; nunca publica sozinho.
- **Status novo:** escolha um selo existente pelo significado; se não houver, crie o par de tokens `-bg/-ink` nos dois temas.
- **Tema de quiz novo:** domínio + ADR primeiro; tokens `--q-x*`; classe `.x`; corpo e carinha do personagem.

## 10. Antes de dizer que terminou

- [ ] Só tokens; nada de valor solto, `!important` ou estilo inline de cor/tamanho.
- [ ] Conferido em 1440 e 375 px, claro e escuro, sem rolagem horizontal.
- [ ] Campos com label, foco visível, estados de erro/desabilitado; altura certa.
- [ ] Estados vazio, carregando e erro quando fazem sentido.
- [ ] Contraste AA (texto pequeno ≥ 4,5:1) nos dois temas.
- [ ] Animação nova com função e desligada em reduced-motion.
- [ ] Ids e textos conferidos pelos testes intactos; `dotnet test` verde.
- [ ] Guia e este arquivo atualizados se uma regra mudou.
