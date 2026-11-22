# DESIGN.md — Painel do Universo do Helinho (tema Recreio)

> **Transição.** O `src/Helinho.Web/wwwroot/css/theme.css` **ainda é o tema antigo** (acento framboesa, DM Sans) até a Issue de tokens da Sprint 2 ser mergeada (primeiro item da ordem em `ENTREGA.md` §2). O tema vigente para qualquer tela nova ou alterada é o **Recreio**, descrito aqui. A especificação do tema antigo está no histórico deste arquivo (commit `90a93c8`); os comentários do `theme.css` que citam "DESIGN.md seção 7/9" se referem a ela.

**Escopo:** painel administrativo (Razor Pages + Bootstrap 5.3). Não cobre o jogo Unity.
Este arquivo é um resumo e um índice. As regras completas estão nos dois guias abaixo; em caso de divergência, eles valem.

## 1. Onde está cada coisa

| Arquivo | Para que serve | Quando ler |
|---|---|---|
| [`TEMA-PARA-IA.md`](TEMA-PARA-IA.md) | Regras do tema para agentes: camadas do CSS, tokens, identidade, campos, gráficos, ícones, movimento, layout, checklist final | **Antes** de editar `*.cshtml`, `Pages/`, `wwwroot/css` ou `wwwroot/js` |
| [`GUIA-DE-EVOLUCAO.md`](GUIA-DE-EVOLUCAO.md) | Mapa de telas, regras que não mudam, receitas ("vamos adicionar uma lista, um formulário, um gráfico, um tema de quiz…"), tabela de tokens | Ao criar tela, campo, status, gráfico ou animação |
| [`ENTREGA.md`](ENTREGA.md) | Documento de entrega do design: o que portar e para onde, inventário de imagens, ordem dos PRs, contratos que os testes conferem, pontos de atenção | Ao planejar ou implementar uma Issue de porte |
| [`recreio/preview/`](recreio/preview/) | Preview estática, a referência visual: `v2-entrar.html` (login), `v2.html` (Painel), `telas.html` (as demais telas, uma por `#endereço`), `recreio.css`, `telas.css`, `v2.js`, `img/` | Para comparar cada tela implementada |

Abrir a preview: `python -m http.server 4173 --bind 127.0.0.1 --directory docs/design/recreio/preview` e acessar `/v2-entrar.html`, `/v2.html`, `/telas.html#alunos`.

**Caminhos da entrega × repositório.** Os documentos foram escritos para o pacote da entrega: onde citam `preview/…`, leia `docs/design/recreio/preview/…`; onde citam `design-system/…`, leia `docs/design/…`. Não foram importados: a v1 rejeitada (`index.html`, `estelar.css`, `caderno-estelar.html`), `img/vila.webp`, as cópias PNG do logo FUNDHAS e a pasta `preview/entrega/` (originais em alta, com o Henrique).

**A preview é referência, não código de produção.** Ela carrega fontes do Google Fonts, Leaflet do unpkg e tiles do OpenStreetMap; nada disso vai para o `wwwroot` como está (fontes são autohospedadas; o mapa pede Issue e ADR próprios). Todos os dados são de exemplo. Em `telas.html`, a lista de unidades do mapa teve homenageados, endereços, CEPs e telefones trocados por valores fictícios na importação.

## 2. Identidade em uma tela

- **Personagem = tema de quiz:** Flora = Ambiental, Byte = Conhecimento, Luna = Cultural, Max = Esportivo, Lia = Diversidade. Helinho é o mascote e não representa tema. Tema aparece sempre com nome + personagem, nunca só com a cor.
- **Azul FUNDHAS é ação** (vira amarelo no tema escuro). **Amarelo** é estrela, destaque e "hoje". **Lilás é exclusivo de conteúdo de IA.**
- Botão tem "lábio" e afunda ao clicar. Superfícies são `.panel` com borda de 2 px e sombra curta.
- Dois temas, claro e escuro, pelos **mesmos tokens** (`:root[data-theme="dark"]`).

## 3. Camadas do CSS

Um arquivo, `wwwroot/css/theme.css`, na ordem:

```css
@layer tokens, base, components, screens, motion;
```

Valor entra em `tokens`; peça reutilizável em `components`; regra de uma tela em `screens`; animação em `motion`. Nunca `!important` para vencer outra camada.

## 4. Tokens principais

Fonte da verdade até o porte: `@layer tokens` em `recreio/preview/recreio.css` (e os tokens de IA em `telas.css`). Lista completa por grupo: `GUIA-DE-EVOLUCAO.md` §4 e `TEMA-PARA-IA.md` §2.

| Grupo | Tokens | Valor de referência (tema claro) |
|---|---|---|
| Marca | `--fundhas-blue`, `--fundhas-yellow` | `oklch(0.44 0.13 250)` (#005497), `oklch(0.87 0.175 90)` (#FFCD06) |
| Superfície | `--canvas`, `--stage`, `--rule`, `--rule-strong` | fundo da página, cartão, réguas de 2 px |
| Texto | `--ink`, `--ink-2`, `--ink-3` | `--ink-3` é o cinza mais claro permitido em texto |
| Ação e seleção | `--action*`, `--on-action`, `--select-*`, `--focus` | botão primário, item ativo, anel de foco |
| Estado | `--ok*`, `--bad*`, `--warn*`, `--neutral*` | sempre em par `-bg` + `-ink` |
| Tema de quiz | `--q-amb/con/cul/esp/div` (+ `-bg`, `-ink`) | usados pelas classes `.amb .con .cul .esp .div` |
| IA | `--ai`, `--ai-bg`, `--ai-ink` | só conteúdo gerado por IA |
| Gráfico | `--data`, `--data-ref`, `--data-wash`, `--grid` | uma cor de dado + cinza tracejado de comparação |
| Tipografia | `--font`, `--font-display`, `--fs-xs…--fs-hero` | Nunito (texto, 500–900) e Baloo 2 (títulos e números grandes, 700–800) |
| Espaço e forma | `--sp-1…--sp-12`, `--r-sm/md/lg/xl/pill`, `--lip` | 4–48 px; 8 · 12 · 20 · 28 px; lábio de 3 px |
| Altura de controle | `--h-control-sm`, `--h-control`, `--h-control-lg` | 36 · 40 · 48 px |
| Movimento | `--ease*`, `--dur-*`, `--stagger` | zerados com `prefers-reduced-motion: reduce` |

## 5. Regras essenciais

1. **Token, nunca valor solto.** Sem hex/rgb/oklch fora de `@layer tokens`, sem `style=""` de cor ou tamanho (exceção: variável de dado, como `style="--v:.42"`), sem `<style>` em página. Falta um valor? Crie o token nos dois temas e documente no guia.
2. **Contraste AA** nos dois temas (texto pequeno ≥ 4,5:1). Cor nunca é o único sinal: variação tem seta + texto, tema tem nome + personagem.
3. **Campos** sempre em `.field` com `label` visível; erro com `aria-invalid` + `aria-describedby`; filtros só enviam pelo botão **Aplicar** (sem `onchange`, sem `<noscript>`).
4. **Gráficos:** o servidor manda HTML (tabela, `data-spark`, `--v`), o JS só desenha; sem JS os números continuam visíveis. Dica montada com `textContent`, nunca `innerHTML` com dado do banco.
5. **Movimento com função** (orientar, dar retorno, contar algo), em `@layer motion` dentro de `prefers-reduced-motion: no-preference`; só `opacity`, `translate`, `scale`, `rotate`, `filter`, `clip-path`.
6. **Larguras de referência:** 1440 px e 375 px, sem rolagem horizontal (tabela larga rola em `.tscroll`); menu vira gaveta abaixo de 768 px.
7. **Sem dado pessoal em lista** (CPF, RG, endereço só no formulário de edição do aluno). Exemplos e capturas só com dado sintético.
8. **Ids e textos que os testes conferem não mudam** (lista em `ENTREGA.md` §3 e nos testes de `PanelTests`/`DashboardTests`).
9. **IA sugere, a equipe decide:** conteúdo gerado nasce rascunho e passa por Pedir → Revisar → Aprovar.

## 6. Protocolo e verificação

O protocolo de design e a verificação visual obrigatória (capturas em 1440 px e 375 px, claro e escuro, comparadas com a preview) estão em `CLAUDE.md`/`AGENTS.md`, seção "Design (tema Recreio)". Checklists: `ENTREGA.md` §5, `TEMA-PARA-IA.md` §10 e `GUIA-DE-EVOLUCAO.md` §5. Mudou uma regra do tema? Atualize o guia, o arquivo da IA e este resumo no mesmo PR.
