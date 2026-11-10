# SOLID como heurística (não como checklist cerimonial)

Use SOLID para diagnosticar um problema que você está vendo no código. Se o código é pequeno, claro e testável, não refatore "para ficar SOLID".

## S — Single Responsibility
**Pergunta:** quantos motivos diferentes (atores/áreas do negócio) fariam esta classe mudar?
- Sintomas: classe > ~300 linhas, nome com "Manager/Helper/Util", métodos que não compartilham campos, testes que precisam de 8 mocks.
- Correção: separar por motivo de mudança (cálculo de tarifa ≠ envio de e-mail ≠ persistência).
- Excesso: uma classe por método. Coesão também é um valor.

## O — Open/Closed
**Pergunta:** para adicionar uma variação nova, preciso editar um `switch` em vários lugares?
- Sintoma: `if (tipo == "pix") ... else if (tipo == "boleto")` repetido.
- Correção: polimorfismo/Strategy registrado num mapa ou via DI. Aplique **na segunda ou terceira variação real**, não na primeira.

## L — Liskov Substitution
**Pergunta:** algum subtipo lança "NotSupported", ignora parâmetro ou enfraquece garantia do contrato?
- Sintoma: `override` que faz `throw new NotImplemented`; checagem `if (x is SubtipoY)` no chamador.
- Correção: contratos menores (ver I) ou composição em vez de herança. Prefira composição por padrão.

## I — Interface Segregation
**Pergunta:** quem consome esta interface usa todos os métodos?
- Sintoma: repositório com 25 métodos e cada caso de uso usa 2; mocks gigantes nos testes.
- Correção: interfaces pequenas definidas pelo consumidor (`ReservaLeitura`, `ReservaEscrita`), especialmente em Go e TypeScript.

## D — Dependency Inversion
**Pergunta:** a regra de negócio instancia diretamente algo que faz I/O?
- Sintoma: `new HttpClient()`, `new SmtpClient()`, `DateTime.Now`, `Math.random()` dentro de caso de uso/domínio.
- Correção: depender de uma abstração (porta) injetada; relógio e gerador de IDs também são portas quando afetam regra testável.
- Excesso: interface para cada classe interna pura. DIP é sobre **fronteiras**, não sobre tudo.

## Princípios irmãos que valem tanto quanto
- **KISS / YAGNI:** a solução mais simples que satisfaz os critérios de aceite.
- **DRY com bom senso:** duplicação acidental (parecida hoje, motivos de mudança diferentes) é melhor que acoplamento errado. Regra de três antes de abstrair.
- **Lei de Demeter:** `pedido.cliente.endereco.cidade.nome` indica que falta um método no lugar certo.
- **Composição > herança.**
- **Fail fast:** valide na fronteira e nas invariantes; não propague estado inválido.
