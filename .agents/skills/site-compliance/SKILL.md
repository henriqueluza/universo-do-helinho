---
name: site-compliance
description: Garante a qualidade não estética de um site antes de ir ao ar — legal, privacidade, ética e acessibilidade. Verifica e implementa páginas de política de privacidade, termos de serviço, política de reembolso e de cookies; banner de consentimento de cookies que realmente bloqueia; consentimento explícito em formulários; coleta mínima de dados (LGPD/GDPR); auditoria de SDKs e scripts de terceiros; remoção de dark patterns; sem taxas escondidas, avaliações falsas ou alegações sem prova; alt text e contraste; navegação por teclado; dados da empresa (CNPJ, endereço, contato); verificação de idade quando há menores; link de descadastro em e-mails; licença de imagens de banco; opção de exclusão de dados. Use ao criar ou alterar páginas públicas, formulários, checkout, e-mails, rodapé, scripts de analytics/marketing, e como revisor (compliance-reviewer) de PRs que tocam essas áreas; obrigatório antes do primeiro deploy em produção.
---

# Qualidade do site além da estética

Um site bonito que coleta dado demais, esconde taxa ou não tem política de privacidade é um site com problema. Esta skill tem dois modos: **implementar** (seção 2 — o que todo site público precisa ter) e **auditar** (seção 3 + `references/checklist.md`). Não é parecer jurídico: aponta o que falta e dá o padrão mínimo; texto legal final é revisado por humano (label `needs-human` com o rascunho).

## 1. Como rodar

```bash
python3 <skill-dir>/scripts/compliance_check.py <pasta-do-front-ou-build> [--emails pasta-de-templates]
```
Procura: páginas/rotas de privacidade, termos, reembolso e cookies; links para elas no rodapé; componente de consentimento de cookies e se scripts de terceiros carregam **antes** do consentimento; formulários com checkbox de consentimento e campos além do necessário; inventário de SDKs/scripts externos; `alt` ausente; dados da empresa no rodapé; link de descadastro em templates de e-mail; padrões de dark pattern no texto ("só hoje", pré-marcado, "não, não quero economizar"). Estático e heurístico: complete com a leitura da página renderizada e, com Playwright, teste o fluxo real (consentimento, teclado, exclusão de conta).

## 2. O que todo site público precisa (implementar)

**Documentos legais** (rotas estáveis, linkadas no rodapé e no cadastro/checkout, data de "última atualização", linguagem clara, em português para público brasileiro):
- **Política de privacidade:** quem é o controlador, quais dados, finalidade e base legal de cada um (LGPD art. 7), compartilhamento com terceiros (nomeados: analytics, pagamento, e-mail), transferência internacional, retenção, direitos do titular (acesso, correção, exclusão, portabilidade, revogação) e **como exercê-los**, contato do encarregado/DPO, cookies.
- **Termos de serviço:** o que o serviço faz, conta e responsabilidades, pagamento e renovação, cancelamento, propriedade intelectual, limitação de responsabilidade, foro, alterações.
- **Política de reembolso:** prazo, condições, como pedir, prazo para devolver o dinheiro. No Brasil, direito de arrependimento de 7 dias para compra online (CDC art. 49) — explícito.
- **Política de cookies:** tabela de cookies (nome, fornecedor, finalidade, duração, categoria), como gerir o consentimento.

**Consentimento e dados**
- **Banner de cookies** que: não pré-marca categorias opcionais; tem "Rejeitar" com o mesmo peso de "Aceitar"; **bloqueia** analytics/marketing até o aceite (scripts só injetados após consentimento, não só "avisados"); permite mudar depois (link "Preferências de cookies" no rodapé); registra a escolha. Estritamente necessários não precisam de consentimento.
- **Formulários:** consentimento explícito e específico (um checkbox por finalidade: newsletter ≠ termos), **nunca pré-marcado**, link para a política ao lado; mensagem de erro acessível; só campos necessários (nome completo e telefone são necessários mesmo? CPF só se obrigatório por lei/pagamento).
- **Coleta mínima:** liste todo dado coletado e justifique a finalidade. Sem finalidade, apague o campo. Dados sensíveis (saúde, biometria, origem, religião, orientação) só com base legal específica e consentimento destacado.
- **Exclusão de dados:** opção visível na conta ("Excluir minha conta e dados") ou formulário/e-mail documentado na política, com prazo de atendimento (LGPD: 15 dias). Exclusão real ou anonimização, incluindo em terceiros (processadores).
- **Menores:** se o público pode incluir menores de 18, verificação de idade/consentimento dos pais (LGPD art. 14), sem coleta além do necessário, sem perfilamento, sem marketing direcionado. Conteúdo adulto/jogos/bebidas: gate de idade antes do conteúdo.
- **Terceiros:** inventário de todo SDK/script externo (analytics, pixels, chat, mapas, fontes, CDNs, pagamento) com finalidade, dados que recebe, política do fornecedor e categoria de consentimento. Remova o que não tem finalidade clara. Fontes e scripts self-hosted quando possível (evita vazar IP para terceiros). Rode `supply-chain-risk-auditor` em dependências novas.

**Honestidade comercial**
- **Sem dark patterns:** sem "confirmshaming" ("Não, prefiro pagar mais"), sem pré-seleção de upsell, sem contagem regressiva falsa, sem escassez inventada ("3 pessoas vendo"), sem labirinto para cancelar (cancelar tão fácil quanto assinar), sem botão de recusa escondido, sem renovação automática sem aviso claro antes da cobrança, sem "trial" que cobra sem lembrete.
- **Sem taxas escondidas:** preço total (impostos, frete, taxas) visível antes do último passo; "a partir de" sempre com o valor real do plano mostrado; moeda e periodicidade explícitas.
- **Sem avaliações falsas:** depoimentos de pessoas reais com autorização; sem gerar reviews; avaliações agregadas correspondem a avaliações exibidas; divulgue incentivos. Sem logos de clientes sem permissão.
- **Sem alegações sem prova:** "#1", "o mais rápido", "aprovado por médicos", "X% de resultado" só com fonte verificável linkada. Garantias com termos. Nada de promessa de resultado em saúde/finanças sem base.
- **Dados da empresa:** razão social, CNPJ, endereço, e-mail e telefone/canal de atendimento no rodapé ou página de contato (Decreto 7.962/2013 para e-commerce). SAC com prazo de resposta.

**Acessibilidade (WCAG 2.2 AA — complementa `web-design-guidelines`)**
- `alt` descritivo em imagens informativas, `alt=""` em decorativas; sem texto em imagem sem equivalente.
- Contraste 4.5:1 (texto), 3:1 (texto grande e componentes de UI/foco). Nada transmitido só por cor.
- Navegação por teclado completa: ordem lógica, foco visível, sem armadilhas (modais fecham com Esc e devolvem o foco), skip link, menus e carrosséis operáveis, alvos ≥ 24×24 (ideal 44).
- Formulários com `label` associado, erros descritos em texto, `autocomplete` correto, sem limite de tempo sem aviso.
- Vídeo com legenda; áudio com transcrição; nada que pisque > 3×/s; `prefers-reduced-motion` respeitado.

**E-mails** (transacionais e marketing): link de **descadastro** em um clique em todo e-mail de marketing (e header `List-Unsubscribe`), endereço físico do remetente, remetente identificável, sem e-mail de marketing sem opt-in, SPF/DKIM/DMARC configurados.

**Imagens e mídia:** toda imagem de banco com licença registrada (`docs/licenses/images.md`: arquivo, fonte, licença, atribuição exigida); sem Google Images; ícones e fontes com licença compatível (OFL, MIT, CC); atribuição onde a licença exige; fotos de pessoas com cessão de imagem.

## 3. Auditoria (revisão)

Rode o checker, depois `references/checklist.md`. Reporte `[bloqueante|sugestão] página/arquivo — problema — correção`. **Bloqueante:** falta qualquer um dos quatro documentos legais em site que coleta dado ou vende; scripts de analytics/marketing antes do consentimento; checkbox pré-marcado; campo de dado sensível sem base; sem opção de exclusão de dados; dark pattern em checkout/cancelamento; taxa só no último passo; avaliação ou alegação sem fonte; e-mail de marketing sem unsubscribe; imagem sem alt em conteúdo; contraste < 4.5:1 em texto; fluxo essencial inoperável por teclado; público com menores sem verificação de idade. Texto legal faltando → gere o rascunho a partir do padrão acima e marque `needs-human` para revisão jurídica.
