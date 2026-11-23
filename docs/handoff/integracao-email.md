# Integração da onda de e-mail (ADR-0003, onda 2)

Seis pilhas feitas em paralelo sobre `base-email` foram juntadas, costuradas e provadas de ponta a ponta. Este arquivo diz o que existe, em que ordem levar tudo para a branch padrão, onde vai dar conflito e o que ficou por fazer.

## O que existe (tudo no remoto, **nenhum PR aberto por esta integração**)

| Branch | Base | Conteúdo |
|---|---|---|
| `email-integrada` | `base-email` + branch padrão de 22/11 | junção por merge, nesta ordem: trabalhador (#228), tratador (#270–#272), redefinição (#278–#280), convite (#266, #268, #285), acesso do aluno (#267), webhook (#277, #265) e e-mail repetido (#184, PR #276) |
| `issue-291-costura-email` | `email-integrada` | costura: aviso de troca no tratador, limpeza do endereço anterior, id da mensagem do webhook (`291.md`) |
| `issue-292-email-troca-senha-aluno` | #291 | texto de troca de senha no e-mail de acesso do aluno (`292.md`) |
| `issue-293-email-ponta-a-ponta` | #292 | teste de ponta a ponta dos quatro fluxos (`293.md`) e este arquivo |

`email-integrada` é uma branch de prova: **não é para virar PR**. Cada pilha entra pela sua própria branch, na ordem abaixo. As três branches novas (#291, #292, #293) viram PR depois que as pilhas de que dependem estiverem na branch padrão; até lá o diff delas contra a branch padrão inclui a onda inteira.

## Ordem de entrega recomendada

O que depende de quê:

```
PR #120 (Data Protection)
  └─ base-120-atualizada: fila QueueJobs (#223, PR #237) + GuardianEmail (#235, PR #245)
       ├─ trabalhador #228 (partes 1 a 5)
       ├─ tratador #270 → #271 → #272
       ├─ webhook #277 → #265
       ├─ redefinição #278 → #279 → #280        (precisa do PR #276 antes ou depois: ver conflitos)
       ├─ convite #266 → #268 → #285            (idem)
       └─ acesso do aluno #267 (partes 1 a 5)
PR #276 (#184, e-mail repetido): só depende da branch padrão
PR #283 (#281, caixa de saída de desenvolvimento): só depende da branch padrão
costura #291 → texto #292 → ponta a ponta #293: dependem de tudo acima, menos do PR #283
```

Sequência sugerida, um PR por vez:

1. **PR #120** (Data Protection). Hoje está em conflito com a branch padrão, e a branch dele tem dois commits de diagnóstico marcados como TEMPORÁRIO (`21cab0f`, `19a3ec7`) e duas correções (`cf39854`, `b13c4d6`) que **não estão** em `base-email`. Resolver isso é da Issue dele.
2. **Fila e `GuardianEmail`** (`base-120-atualizada`): os PRs #237 e #245 foram mesclados nessa branch, não na branch padrão. Abrir um PR dela para a branch padrão logo depois do #120. As três migrações têm de chegar nesta ordem: Data Protection, `AddQueueJobs`, `AddStudentGuardianEmail`.
3. **PR #276** (#184). Pode entrar a qualquer momento; entrar **antes** do convite e da redefinição é o caminho com menos retrabalho, porque é a implementação de referência da recusa de e-mail já usado.
4. **Trabalhador** #228, partes 1 a 5 em sequência.
5. **Tratador** #270, #271, #272.
6. **Webhook** #277, #265 (pode ir em paralelo com 4 e 5: só precisa da tabela da fila).
7. **Fluxos**, em qualquer ordem entre si: redefinição (#278, #279, #280), convite (#266, #268, #285), acesso do aluno (#267, partes 1 a 5).
8. **Costura** #291, depois **texto** #292, depois **ponta a ponta** #293.
9. **PR #283** (caixa de saída): independente; junta sem conflito com a branch integrada (conferido localmente).

Por que trabalhador e tratador antes dos fluxos: os fluxos só enfileiram. Se um fluxo chegar antes, a página funciona, mas o e-mail fica `Pending` até vencer (convite e aviso em 24 horas, redefinição em 30 minutos), e ninguém recebe nada. Com o trabalhador e o tratador já na branch padrão e nenhum fluxo, nada acontece: não há trabalho na fila.

Antes de #291 entrar, o aviso de troca do e-mail do responsável falha com `template_missing` e `PreviousGuardianEmail` fica preso na coluna. Se a parte 4 do #267 for para produção antes da #291, cada troca de e-mail deixa um endereço guardado sem prazo. **Levar #291 logo depois do #267.**

## Conflitos e como resolver

A resolução de cada um está feita em `email-integrada`; `git show --remerge-diff <commit>` mostra o que foi decidido.

### 1. `src/Helinho.Web/Composition/Areas.cs` (uma linha por pilha: ficam todas)
Conflito textual entre trabalhador, redefinição e webhook (o tratador juntou sozinho). Merges `21d2791`, `42820c2`, `820fc09`. Estado final:

```csharp
services.AddHospedagem().AddEmail().AddIdentidade().AddApiHome().AddApiPerfil().AddApiQuiz()
    .AddPainelAlunos().AddPainelConteudo().AddPainelEngajamento().AddGestao().AddFila();
services.AddRedefinicaoDeSenha();
services.AddIa();
services.AddEmailTratador();
services.AddBrevoWebhook();
```

e, em `MapAreas`, `app.MapBrevoWebhook();` depois da cadeia de `Map*`. Convite e acesso do aluno não tocam este arquivo (registram dentro de `Gestao.cs` e `PainelAlunos.cs`).

### 2. `tests/Helinho.UnitTests/EmailTemplateRendererTests.cs` (redefinição × acesso do aluno)
Juntou sozinho aqui. Se der conflito no PR: a contagem é **12** arquivos de modelo depois do #267 (**14** depois da #292) e a lista de marcadores tem `data`.

### 3. PR #276 (#184) × convite (#266): as duas fatias recusam e-mail já usado
Merge `b0b4020`. Ficou **uma** implementação, a da #184:

- `CriarAdministradorHandler`: fica `EmailInUseAsync` e o status `CriarAdministradorStatus.EmailInUse`; sai o `PanelEmailInUseAsync ? ["DuplicateEmail"] : …` do convite.
- `IAdminAccountGateway` e `AdminAccountGateway`: sai `PanelEmailInUseAsync` (o Git junta os dois métodos sem acusar conflito no adaptador: apagar à mão). O resumo de `CreateAdminAsync` fica o do convite ("sem senha").
- `CriarAdministradorHandlerTests`: o teste da #184 usa o construtor antigo de `AdminAccount` (4 argumentos) e **não compila** depois do convite; ficou um teste só para a recusa, com as asserções das duas fatias (status `EmailInUse`, nada criado, nada na fila, sem commit). Saem `PanelEmails` e `PanelEmailInUseAsync` do gateway falso.
- `EmailRepetidoTests` (da #184): `new NewAdminAccount(nome, email, unidade)` sem o quarto argumento (a conta nasce sem senha). **Não é conflito textual, é erro de compilação.**
- `Novo.cshtml.cs`: junta sozinho; a mensagem no campo continua "Já existe um usuário com este e-mail.".

**Diferença de comportamento a saber:** a checagem da #184 olha **qualquer conta** (`db.Users`), e a do convite olhava só contas do painel (D11 do ADR: "e-mail único entre contas do painel"). Com a da #184, um administrador não pode ser criado com o e-mail de uma conta de aluno. É mais restritivo e cobre a D11; as contas de aluno do seed têm e-mail.

### 4. PR #276 (#184) × redefinição (#279): `/recuperar-senha`
Merge `b0b4020`.

- `Pages/Conta/RecuperarSenha.cshtml.cs`: conflito textual. Fica a versão da redefinição (a página chama `PedirRedefinicaoHandler`, que enfileira). O e-mail repetido já é tratado em `PasswordResetAccounts.FindSingleAsync` (conta ambígua vale como desconhecida), então a correção da #184 nessa página deixa de ser necessária.
- `EmailRepetidoTests.Recuperar_senha_com_e_mail_repetido_da_a_resposta_neutra_e_nao_envia_nada`: o teste conferia o envio direto por `IEmailSender`; passou a conferir a fila (trabalho `email.password-reset` para a conta de e-mail único, nenhum para as repetidas). **Falha em tempo de execução, não de compilação**, se ficar como está na #184.
- `Entrar.cshtml.cs` e `LoginIdentityGateway.cs` (`FindByUniqueEmailAsync`): juntam sozinhos e continuam valendo.

Se o PR #276 entrar primeiro, os itens 3 e 4 são resolvidos nas branches do convite e da redefinição (merge da branch padrão nelas). Se entrar depois, são resolvidos no próprio PR #276.

### 5. Costura (#291) × tratador e acesso do aluno
Não há conflito: #291 nasceu sobre a junção. Ela edita `EmailJobHandler.cs`, `IEmailSubjects.cs`, `EmailSubjects.cs` e `EmailJobsExtensions.cs` (tratador) e usa `PreviousGuardianEmailCleaner` e `GuardianEmailChangedEmail` (acesso do aluno). Só pode virar PR depois de #272 e #267 parte 5.

## Resultado das verificações nesta sessão

- **Build:** `dotnet build --no-restore -warnaserror` e `dotnet format --verify-no-changes` limpos em `email-integrada` e nas três branches.
- **Testes, pelo script de trava, classe a classe** (ver a tabela no fim). A suíte completa **não rodou** (orientação da onda).
- **Ponta a ponta (#293):** convite, redefinição, acesso do aluno e troca do e-mail do responsável: os quatro passam.
- **`security-scan.sh`** (Semgrep e gitleaks): limpo.

### Verificação visual (22/11/2026)
Feita com a branch integrada mais o PR #283 juntado **só localmente** (branch descartada, nunca enviada), para ter a caixa de saída e chegar ao link válido. Aplicação com `--no-launch-profile`, `Development`, `Email__Provider=Fake`, Postgres em contêiner próprio; Chromium do cache do Playwright (o MCP não tem Chrome). 96 capturas em `~/Trabalho/Universo-do-Helinho/capturas-recreio/email/` (fora do repositório), em 1440 e 375 px, tema claro e escuro:

- Administradores: formulário vazio e com erros, lista depois de criar, convites enviados, reenviar (aviso "aguarde 2 minutos"), confirmação de cancelar, lista depois de cancelar, conta ativa depois de o convidado definir a senha.
- `/conta/definir-senha`: válido, com erros, sucesso, link inválido.
- `/recuperar-senha`: formulário, erro de validação, resposta neutra.
- Ficha do aluno, painel "Acesso ao jogo": sem aprovação, na fila, enviado, reenviado depois da troca do e-mail, conta com senha ("Enviar troca de senha").

Conferido por script em cada tela: rolagem horizontal da página (nenhuma), contraste AA de todo texto sobre o fundo que herda (nenhum abaixo do mínimo; texto sobre degradê ou imagem foi conferido no olho, pela captura) e contorno de foco em cada parada do Tab. **Nenhum defeito corrigido: não apareceu defeito real nas telas de e-mail.**

Não capturado:
- **Ficha sem e-mail do responsável:** o formulário exige o campo, e todos os alunos do seed já têm endereço. O texto do estado é coberto por `AcessoAlunoTelaTests`.
- **"Já existe um usuário com este e-mail" em Administradores/Novo:** a captura caiu no erro da unidade (a unidade escolhida já tinha administrador, e essa conferência vem antes). A mensagem é coberta por `EmailRepetidoTests` e `PanelAdministradoresTests`.
- **Envio que falhou e "limite atingido" na ficha:** dependem de forçar o estado no banco; cobertos por `AcessoAlunoTelaTests`.
- E-mails em cliente de e-mail de verdade (só o HTML na caixa de saída).

Observações (nenhuma foi mexida; decidir em Issue própria):
1. **`/conta/definir-senha` fala com quem é do painel.** O responsável pelo aluno cai na mesma página: no sucesso o botão é "Entrar no painel", e no link inválido é "Pedir novo link" (leva a `/recuperar-senha`, que ignora conta de aluno em silêncio) e "fale com a coordenação da sua unidade". Para o responsável o caminho certo é pedir outro envio na unidade e entrar pelo jogo. É encontro de duas fatias (página #240/#252 e acesso do aluno #267); pede decisão de texto, e a página não pode revelar o tipo da conta num link inválido.
2. **Limite de taxa responde 429 sem corpo** em `/recuperar-senha` (5 por 15 minutos por IP) e em `/conta/definir-senha`: o navegador mostra a página de erro dele ("Esta página não está funcionando"). Apareceu durante as capturas.
3. **Em 375 px a tabela de Administradores rola dentro do `.table-wrap`:** Situação e as ações (Reenviar, Cancelar) ficam fora da primeira dobra horizontal. É o padrão de tabela do tema; a página não rola.
4. **A confirmação de cancelar convite dá foco inicial ao botão que apaga a conta.** Enter confirma na hora.
5. O script acusou falta de foco visível no campo de data da ficha do aluno; conferido à parte, o contorno de 3 px aparece (falso positivo do script ao andar pelos subcampos do dia, mês e ano).
6. O painel "Acesso ao jogo" fica abaixo da dobra e entra com a animação `.reveal` do tema ao rolar; em captura de página inteira sem rolar ele sai invisível. Não é defeito.

## O que ficou por fazer

- **Abrir os PRs** na ordem acima (nenhum foi aberto) e, em cada um, rodar `static-analysis` e `insecure-defaults` no diff.
- **Suíte completa de integração:** só no CI.
- **PR #120:** conflito com a branch padrão e commits temporários de diagnóstico.
- **Texto de `/conta/definir-senha` para o responsável** (observação 1) e **429 com página própria** (observação 2): sem Issue.
- **"E-mail não entregue" nas telas** (item 12 do plano): o webhook grava `Feedback`, e a costura garante que ele acha o trabalho; nenhuma tela mostra ainda (convite e ficha do aluno dizem "Enviado").
- **Teste de `AcessoAlunoTelaTests` que dependia da ordem** (da #267 parte 2): corrigido na #291; ao abrir o PR do #267, vale levar a mesma correção para a branch dele (ver `291.md`).
- **`EmailFilaTests`** (branch `prova-272-email-com-trabalhador`): não foi trazido; `EmailCosturaTests` e `EmailPontaAPontaTests` usam o mesmo processador real.
- **Índice único de e-mail** (#274, continuação da #184): continua em aberto; hoje a unicidade é por trava e conferência no caso de uso.
- **Verificação dos e-mails em cliente de e-mail real** e dos estados não capturados acima.
- **Rótulos das Issues #291, #292 e #293:** estão `status:in-progress`; trocar quando os PRs abrirem.

## Classes de teste rodadas (trava `testar.sh`, uma por vez)

Preenchido no fim da sessão; ver a seção seguinte.
