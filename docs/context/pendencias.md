# Pendências de negócio

Perguntas que o Henrique leva ao Marcos (contratante). Enquanto não houver resposta, o sistema segue a coluna **Proposta em uso**, marcada `PROPOSTA` no código e no ADR. Nenhuma delas trava o desenvolvimento: todas são configuração, validação ou texto. O Henrique aceitou o ADR-0003 em 22/11/2026 com essas propostas; elas continuam `PROPOSTA` até a resposta do Marcos.

**Regras deste arquivo:** nenhum dado pessoal, e-mail real, domínio real ou segredo. Os códigos C04, C05, C09, C13, C14, C15, C20 e A01 são itens do Q&A com a contratante, mantido pelo Henrique fora do repositório. Quando uma pergunta for respondida, registre a resposta e a data na última coluna em vez de apagar a linha.

## Sprint 3 — e-mails de acesso (ADR-0003)

| # | Pergunta | Por que importa | Proposta em uso (`PROPOSTA`) | Q&A | Resposta e data |
|---|---|---|---|---|---|
| M1 | O e-mail do responsável é obrigatório no cadastro do aluno? | A lista de campos obrigatórios confirmada não o inclui. Sem ele, não há para quem enviar o acesso do aluno | Obrigatório no formulário de cadastro e de edição; os alunos já cadastrados ficam sem o dado até serem editados (ADR-0003 §9) | C05, C09 | |
| M2 | O mesmo e-mail pode ser o responsável de mais de um aluno? | Irmãos. Se for único, o segundo irmão não pode ser cadastrado | Pode repetir. Por isso o e-mail do responsável **não** serve para o aluno entrar no jogo; o aluno entra com matrícula ou usuário (ADR-0003 §9) | C05 | |
| M3 | O que fazer quando o responsável não tem e-mail? | Hoje não existe outro caminho para o aluno receber uma senha | Sem caminho alternativo nesta sprint. Se houver esses casos, a saída é o administrador definir uma senha provisória, o que precisa ser decidido (ADR-0003 §11.4) | C05, A01 | |
| M4 | Qual é o endereço do remetente, e com que nome ele aparece? | Sem remetente verificado a Brevo não envia | Endereço de "não responda" no domínio institucional e o nome "Universo do Hélinho · FUNDHAS"; os dois são configuração, não código | C15 | |
| M5 | Qual é o domínio institucional e quem administra o DNS dele? | A Brevo pede registros no DNS para autenticar o domínio (verificação, DKIM e DMARC). Alguém da FUNDHAS precisa criá-los | Nenhuma. Sem isso, o envio real não é ligado; a homologação segue com o envio fake | C15 | |
| M6 | Quem define a senha do aluno: o responsável, por um link enviado ao e-mail dele, ou a unidade? | Decide o conteúdo do e-mail de acesso e se a equipe chega a conhecer a senha da criança | O responsável define, pelo link. Nenhuma senha vai por e-mail nem passa pela equipe (ADR-0003 §11.4) | C05, A01 | |
| M7 | Por quanto tempo valem os links? | Prazo curto protege a conta; prazo longo evita reenvio | Convite de administrador e acesso do aluno: 72 horas. Redefinição de senha: 1 hora. Cada link só funciona uma vez (ADR-0003 §10) | — | |
| M8 | Existe regra institucional de retenção para registros de envio de e-mail? | LGPD: guardar só pelo tempo necessário. Há dois lugares: o nosso sistema e a conta da Brevo, que por padrão guarda os registros sem prazo (endereço do responsável, assunto e eventos) | O sistema não guarda o endereço nem o texto dos e-mails; guarda só que o envio aconteceu e apaga esse registro 30 dias depois. Na Brevo, a conta é configurada para apagar os registros em 1 mês (o mínimo que ela aceita) e não guardar cópia do conteúdo (ADR-0003 §5.6) | C14 | |
| M9 | Quantos alunos serão liberados no maior dia? | O plano gratuito envia 300 e-mails por dia (conferido em 19/11/2026), o que dá cerca de 150 alunos liberados por dia. A autorização para um plano pago é a M14 | Plano gratuito na homologação, com o sistema segurando o excedente para o dia seguinte; plano pago antes da primeira carga real (ADR-0003 §15) | C04, C13 | |
| M10 | O que vai no rodapé dos e-mails: nome oficial da instituição, endereço e canal de contato? | Todo e-mail precisa identificar quem envia e dar um contato | Nome "FUNDHAS" e um marcador de contato a preencher (ADR-0003 §8.3) | C15 | |
| M11 | Ao marcar a aprovação do responsável, o e-mail de acesso deve sair sozinho, ou só quando o administrador clicar em "Enviar acesso"? Quem registra essa aprovação? | Envio automático evita esquecimento; envio manual evita e-mail antes da hora | Sai sozinho quando a aprovação é marcada, e o botão continua existindo para reenviar (ADR-0003 §11.4) | A01 | |
| M12 | A Brevo é aceita como fornecedor para tratar o e-mail do responsável e o primeiro nome do aluno? | Os dados passam por servidores fora do Brasil (Brevo e Railway). Antes de dado real, isso pede contrato de tratamento de dados e registro | Só dado sintético até a resposta | C14, C20 | |
| M13 | A regra de senha dos alunos pode ser diferente da dos administradores? | A regra atual (6 caracteres, com maiúscula, minúscula, número e símbolo) é difícil para crianças de 6 a 14 anos, mesmo com o responsável definindo. A recomendação de segurança atual é outra: senha mais longa e sem exigir tipos de caractere | A mesma regra de hoje para todos, sem mudança nesta sprint (ADR-0003 §11.3, D14) | — | |
| M14 | Há autorização para um plano pago da Brevo? | Três limites do plano gratuito, conferidos em 19/11/2026: (1) **cota** de 300 e-mails por dia; (2) **selo "Sent with Brevo"** em todo e-mail, sem opção de tirar; (3) **os links são trocados por endereços de rastreamento da Brevo**, então o botão do e-mail de acesso não aponta para o domínio da FUNDHAS. O Starter (a partir de US$ 9 por mês) resolve a cota; o selo sai com um complemento de US$ 12 por mês ou nos planos acima; a troca dos links, pelo que se sabe, só é desligada no plano Enterprise, sob pedido e com preço sob consulta (informação de fórum, de 2024, a confirmar com o suporte da Brevo) | Plano gratuito na homologação, aceitando o selo e os links trocados, com as proteções do ADR-0003 §8.4. O e-mail não manda a pessoa conferir o link antes de clicar; manda conferir o endereço da página depois de aberta e diz que ninguém pede senha por e-mail | C04, C13, C20 | |

## O que o Henrique precisa ter em mãos para ligar o envio real

Não são perguntas de regra, e sim acessos (C15 e C20). O passo a passo fica em `docs/operations/brevo-runbook.md`, que é outra Issue.

- Acesso à conta Brevo da contratante, ou autorização para criá-la.
- O contato de quem cria registros no DNS do domínio institucional (M5).
- O endereço do remetente (M4).
- Uma caixa de e-mail que alguém lê, como dona da conta Brevo: é para lá que a Brevo avisa quando desativa uma chave sem uso ou bloqueia um endereço de origem (ADR-0003 §13.1).
