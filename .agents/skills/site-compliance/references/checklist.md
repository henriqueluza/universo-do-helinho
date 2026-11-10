# Checklist — qualidade não estética do site

Marque OK · FALHA · N/A, com página/arquivo e correção. Itens com ★ são bloqueantes no `compliance-reviewer`.

## Documentos legais
- [ ] ★ Política de privacidade publicada, linkada no rodapé, no cadastro e no checkout; data de atualização
- [ ]   Privacidade cobre: controlador, dados coletados, finalidade e base legal por dado, terceiros nomeados, transferência internacional, retenção, direitos do titular e como exercê-los, DPO/encarregado, cookies
- [ ] ★ Termos de serviço publicados e aceitos explicitamente no cadastro (checkbox não pré-marcado)
- [ ] ★ Política de reembolso (prazo, condições, processo; arrependimento 7 dias CDC art. 49 se venda online)
- [ ] ★ Política de cookies com tabela (nome, fornecedor, finalidade, duração, categoria)
- [ ]   Linguagem clara, em português para público BR; versão por idioma se site multilíngue

## Consentimento de cookies
- [ ] ★ Banner aparece antes de qualquer cookie/script não essencial; "Rejeitar" com mesmo destaque de "Aceitar"
- [ ] ★ Categorias opcionais desmarcadas por padrão; scripts de analytics/marketing só injetados após aceite da categoria
- [ ]   "Preferências de cookies" acessível a qualquer momento (rodapé); escolha registrada com data
- [ ]   Banner não cobre o conteúdo principal em mobile (SEO: interstitial intrusivo) e é operável por teclado

## Formulários e consentimento
- [ ] ★ Checkbox de consentimento por finalidade, nunca pré-marcado, com link para a política
- [ ]   Newsletter/marketing separado do aceite dos termos (opt-in, não opt-out)
- [ ] ★ Só campos necessários; cada campo com finalidade justificada; sem CPF/telefone/data de nascimento "por via das dúvidas"
- [ ] ★ Dados sensíveis (saúde, biometria, origem, religião, orientação, menores) só com base legal e consentimento destacado
- [ ]   Erros de validação em texto, acessíveis; `autocomplete` correto; sem CAPTCHA inacessível

## Dados e direitos do titular
- [ ] ★ Opção de exclusão de conta e dados (self-service ou canal documentado), prazo ≤ 15 dias, cascata para processadores
- [ ]   Exportação de dados (portabilidade) disponível ou documentada
- [ ]   Retenção definida por tipo de dado; logs e backups com prazo
- [ ]   Registro de consentimento (o quê, quando, versão do texto)

## Menores
- [ ] ★ Se público pode incluir < 18: verificação de idade e consentimento parental (LGPD art. 14); sem perfilamento ou marketing direcionado
- [ ] ★ Conteúdo restrito (adulto, bebidas, apostas): gate de idade antes do conteúdo

## Terceiros (SDKs, scripts, pixels)
- [ ] ★ Inventário completo: fornecedor, finalidade, dados enviados, política, categoria de consentimento, quem aprovou
- [ ]   Nada sem finalidade clara; fontes/CDN self-hosted quando possível; `supply-chain-risk-auditor` em dependências novas
- [ ]   Pixels de redes sociais e remarketing só após consentimento de marketing
- [ ]   Contratos de processamento (DPA) com processadores que recebem dados pessoais

## Honestidade comercial
- [ ] ★ Sem dark patterns: confirmshaming, pré-seleção de upsell, urgência/escassez falsa, cancelamento labiríntico, recusa escondida, renovação sem aviso, trial que cobra sem lembrete
- [ ] ★ Preço total (impostos, frete, taxas) visível antes do último passo; sem taxa surpresa; "a partir de" acompanhado do valor real
- [ ] ★ Sem avaliações/depoimentos inventados; pessoas reais com autorização; incentivos divulgados; agregados coerentes com o exibido
- [ ] ★ Alegações ("#1", "o mais rápido", "%") com fonte verificável linkada; nada de promessa de resultado sem base
- [ ]   Logos de clientes/parceiros com permissão; selos reais e linkados
- [ ]   Cancelar tão fácil quanto assinar (mesmo canal, ≤ 3 cliques)

## Dados da empresa
- [ ] ★ Razão social, CNPJ, endereço, e-mail e canal de atendimento no rodapé ou contato (Decreto 7.962/2013 em e-commerce)
- [ ]   SAC com prazo de resposta; horário; política de troca se produto físico

## Acessibilidade (WCAG 2.2 AA)
- [ ] ★ `alt` em toda imagem informativa; `alt=""` em decorativas; texto em imagem com equivalente
- [ ] ★ Contraste ≥ 4.5:1 (texto) e ≥ 3:1 (texto grande, UI, indicador de foco); informação não só por cor
- [ ] ★ Fluxos essenciais (navegar, cadastrar, comprar, cancelar) completos por teclado; foco visível; sem armadilha; Esc fecha modal e devolve foco
- [ ]   Skip link; landmarks (`header/nav/main/footer`); `lang`; ordem de foco lógica; alvos ≥ 24×24 (ideal 44)
- [ ]   `label` em todo campo; erros em texto; sem limite de tempo sem aviso
- [ ]   Vídeo com legenda; áudio com transcrição; nada pisca > 3×/s; `prefers-reduced-motion`
- [ ]   Testado com leitor de tela em uma rota principal (VoiceOver/NVDA) e com zoom 200%

## E-mails
- [ ] ★ Descadastro em um clique em todo e-mail de marketing (link visível + `List-Unsubscribe`/`List-Unsubscribe-Post`)
- [ ]   Endereço físico e remetente identificável; sem marketing sem opt-in; transacional sem conteúdo promocional disfarçado
- [ ]   SPF, DKIM, DMARC configurados; texto alternativo em imagens; dark mode legível

## Imagens, fontes e mídia
- [ ] ★ Toda imagem de banco com licença registrada em `docs/licenses/images.md` (arquivo, fonte, licença, atribuição)
- [ ]   Fontes e ícones com licença compatível (OFL, MIT, CC); atribuição exigida presente
- [ ]   Fotos de pessoas com cessão de imagem; sem imagens de Google Images/Pinterest
- [ ]   Imagens das galerias de referência não copiadas para o repositório

## Segurança mínima do site público (complementa `owasp-security`)
- [ ]   HTTPS + HSTS; cabeçalhos CSP, X-Content-Type-Options, Referrer-Policy, Permissions-Policy
- [ ]   Formulários com proteção anti-abuso (rate limit, honeypot) sem bloquear acessibilidade
- [ ]   Senhas: política mínima, hashing adequado, recuperação segura; 2FA onde há dado sensível ou pagamento
- [ ]   Página de segurança/`security.txt` com canal para reportar vulnerabilidades
