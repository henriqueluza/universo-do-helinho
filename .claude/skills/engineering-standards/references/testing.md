# Testes — o que testar e onde

## Pirâmide
| Nível | O que cobre | Infra real? | Proporção |
|---|---|---|---|
| Unitário de domínio | invariantes, cálculos, máquinas de estado, value objects | não | maioria |
| Unitário de caso de uso | orquestração, com portas substituídas por fakes em memória | não | muitos |
| Integração | adapters reais: repositório contra banco real (Testcontainers), cliente HTTP contra servidor fake | sim | alguns por adapter |
| Contrato/API | endpoint → resposta, status, validação, autorização | app inteira, banco de teste | 1+ por endpoint |
| E2E | fluxos críticos de negócio pela UI (login, checkout, pagamento) | tudo | poucos |

## Regras
1. Cada critério de aceite da Issue tem pelo menos um teste que falharia sem a mudança.
2. Bug corrigido = teste que reproduz o bug primeiro.
3. Prefira **fakes** (implementação em memória da porta) a mocks com `verify` de chamadas. Mock de chamada acopla o teste à implementação.
4. Não mocke o que você não possui (SDK de terceiro): mocke a sua porta, e teste o adapter em integração.
5. Teste comportamento, não detalhes: nada de testar método privado ou contar chamadas internas.
6. Determinismo: relógio, aleatoriedade e IDs injetados. Nenhum `sleep` em teste.
7. Dados de teste via builders/fábricas; nenhum dado pessoal real ou segredo em fixture.
8. Nome do teste descreve regra: `confirmar_reserva_sem_pagamento_falha`.
9. Remover ou pular teste para o CI passar é proibido (regra do repositório).

## Autorização e segurança também são testadas
Para Issues `security:sensitive`: teste explícito de acesso negado (usuário sem permissão, recurso de outro tenant), entrada maliciosa na validação e idempotência quando houver dinheiro.

## Cobertura
Cobertura é sinal, não meta. Domínio e casos de uso devem ficar perto de 100% naturalmente; não persiga cobertura em código de wiring/configuração.
