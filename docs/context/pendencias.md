# Pendências de negócio

Perguntas que o Henrique leva ao Marcos (contratante). Enquanto não houver resposta, o sistema segue a coluna **Proposta em uso**, marcada `PROPOSTA` no código e no ADR. Nenhuma delas trava o desenvolvimento: todas são configuração, validação ou texto.

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
| M8 | Existe regra institucional de retenção para registros de envio de e-mail? | LGPD: guardar só pelo tempo necessário | O sistema não guarda o endereço nem o texto dos e-mails. Guarda só que o envio aconteceu, e apaga esse registro 30 dias depois (ADR-0003 §5.6) | C14 | |
| M9 | Quantos alunos serão liberados no maior dia? Há autorização para um plano pago da Brevo? | O plano gratuito envia 300 e-mails por dia (conferido em 19/11/2026), o que dá cerca de 150 alunos liberados por dia. O primeiro plano pago custa a partir de US$ 9 por mês | Plano gratuito na homologação, com o sistema segurando o excedente para o dia seguinte; plano pago antes da primeira carga real (ADR-0003 §15) | C04, C13 | |
| M10 | O que vai no rodapé dos e-mails: nome oficial da instituição, endereço e canal de contato? | Todo e-mail precisa identificar quem envia e dar um contato | Nome "FUNDHAS" e um marcador de contato a preencher (ADR-0003 §8.3) | C15 | |
| M11 | Ao marcar a aprovação do responsável, o e-mail de acesso deve sair sozinho, ou só quando o administrador clicar em "Enviar acesso"? Quem registra essa aprovação? | Envio automático evita esquecimento; envio manual evita e-mail antes da hora | Sai sozinho quando a aprovação é marcada, e o botão continua existindo para reenviar (ADR-0003 §11.4) | A01 | |
| M12 | A Brevo é aceita como fornecedor para tratar o e-mail do responsável e o primeiro nome do aluno? | Os dados passam por servidores fora do Brasil (Brevo e Railway). Antes de dado real, isso pede contrato de tratamento de dados e registro | Só dado sintético até a resposta | C14, C20 | |
| M13 | A regra de senha dos alunos pode ser mais simples que a dos administradores? | A regra atual (maiúscula, minúscula, número e símbolo) é difícil para crianças de 6 a 14 anos, mesmo com o responsável definindo | A mesma regra para todos | — | |

## O que o Henrique precisa ter em mãos para ligar o envio real

Não são perguntas de regra, e sim acessos (C15 e C20). O passo a passo fica em `docs/operations/brevo-runbook.md`, que é outra Issue.

- Acesso à conta Brevo da contratante, ou autorização para criá-la.
- O contato de quem cria registros no DNS do domínio institucional (M5).
- O endereço do remetente (M4).
