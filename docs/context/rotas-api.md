# Contrato de API: Universo do Helinho

Documento de referência para a integração entre o jogo (Unity) e a API.

* Cliente: Unity 6000.6.0f1, Android (iOS depois), HTTP via UnityWebRequest.  
* Serialização: JsonUtility, o serializador nativo da Unity. Sem bibliotecas externas. Isso impõe as regras de formato da seção 1, que valem para todas as rotas.  
* Transporte: HTTPS obrigatório. O Android bloqueia tráfego em texto puro desde o 9\.  
* Autenticação: token no header Authorization em toda rota exceto POST /login.

---

## 1\. Regras de formato, válidas para todas as rotas

O JsonUtility é rápido e não aumenta o APK, mas é limitado. As oito regras abaixo não são preferência de estilo, são o que ele consegue ler.  
1.1. Objeto na raiz, nunca array solto. \[{...}, {...}\] no topo do JSON não desserializa. Todo array vai dentro de um campo nomeado.  
1.2. Sem chave dinâmica. Nada de {"ambiental": {...}, "conhecimento": {...}}. Dicionário não existe para o serializador da Unity. Use lista de objetos com o identificador como campo.  
1.3. Os nomes dos campos batem exatamente com os campos da classe em C\#, inclusive maiúscula e minúscula. Não há atributo para renomear chave. O contrato é snake\_case e as classes do jogo também serão.  
1.4. Campo ausente não é distinguível de zero. Se a chave não vem, o campo fica com o valor padrão do tipo. Sempre que a ausência de um bloco significar alguma coisa, mande um booleano explícito junto, ou responda 204 sem corpo.  
1.5. Data e hora como texto ISO 8601 ou timestamp Unix. O serializador não lê DateTime. O jogo converte.  
1.6. Enum como texto no payload. O JsonUtility serializa enum como número e erraria a leitura de uma string. Mande texto; o jogo recebe como string e mapeia.  
1.7. Uma forma por rota, sem polimorfismo. Nada de um campo type que muda o resto do objeto conforme o valor.  
1.8. Sem array de array e sem lista dentro de lista direto. Aninhar é objeto dentro de array.  
---

## 2\. Princípios do contrato

Valores crus, o jogo formata. A API manda current: 3, total: 10; o app escreve "Continuar 3/10". Redação e idioma ficam no jogo, e trocar uma palavra não exige deploy do backend.  
Cor nunca vem no payload. Cinco componentes recebem cor; todas derivam do tipo de quiz, que é a fonte única.  
Modelos de domínio compartilhados, não um DTO por tela. É por isso que /profile serve a Home e o Perfil, em vez de existirem duas rotas quase iguais.  
Fixo no jogo, não vira rota: os seis personagens, os nomes dos lugares do mapa e o texto da dica da Flora.

### Os cinco tipos de quiz

Valores possíveis de quiz\_type: ambiental, conhecimento, esportivo, cultural, diversidade.  
Cada tipo pertence a um personagem, pelos três ícones de tema que o card dele traz no guia de produto do cliente:

| personagem | tema no guia | quiz\_type |
| :---- | :---- | :---- |
| Flora | natureza, sustentabilidade, consciência | ambiental |
| Byte | tecnologia, inovação, acessibilidade | conhecimento |
| Luna | música, arte, expressão | cultural |
| Max | esportes, saúde, cooperação | esportivo |
| Lia | neurodiversidade, empatia, respeito | diversidade |

O quiz da Lia é sobre diversidade e inclusão.  
Helinho é o anfitrião da praça central e não tem quiz, então são cinco tipos para seis personagens.  
---

## 3\. Rotas

### 3.1. Login

#### POST /login

Envia a credencial. É a única rota sem Authorization.  
{  
 "token": "eyJhbGciOiJIUzI1NiIs...",  
 "expires\_at": "2026-10-22T18:40:00Z",  
 "user\_id": "4812"  
}  
Sem perfil junto, de propósito: o perfil é consultado de novo na tela de Perfil, e duas fontes para o mesmo dado divergem com o tempo.  
---

### 3.2. Home

#### GET /profile

Serve a Home e o Perfil.  
{  
 "display\_name": "Léo Martins",  
 "avatar": "leo-01",  
 "level": 7,  
 "level\_name": "Explorador",  
 "current\_xp": 320,  
 "next\_level\_xp": 500  
}

* next\_level\_xp é o limiar do próximo nível, não o que falta. O jogo calcula os 180 e enche a barra. O nome foi escolhido de propósito: as duas leituras fecham a conta, e um desencontro só apareceria no olho, com a barra enchendo errado.  
* level\_name vem nas duas telas, embora a Home hoje só mostre o número. É para a API já cobrir uma mudança de layout sem virar rota nova.

#### GET /live-event

{  
 "has\_event": true,  
 "id": "festival-conhecimento-2026",  
 "title": "Festival do Conhecimento",  
 "subtitle": "XP em dobro com o Byte",  
 "quiz\_type": "conhecimento",  
 "ends\_at": "2026-10-24T23:59:00Z"  
}  
Sem evento:  
{ "has\_event": false }

* O has\_event existe por causa da regra 1.4: sem ele o jogo não distingue "não tem evento" de "deu erro e veio vazio". 204 sem corpo também resolve; escolha uma.  
* O "TERMINA EM 2h" da faixa sai do ends\_at, não vem pronto.  
* Um evento aponta para um quiz\_type, não vários. Ele não cria categoria nova, só destaca a existente enquanto durar.

#### GET /quizzes/progress

{  
 "progress": \[  
   { "quiz\_type": "ambiental",    "current": 3,  "total": 10, "state": "in\_progress" },  
   { "quiz\_type": "conhecimento", "current": 12, "total": 20, "state": "in\_progress" },  
   { "quiz\_type": "cultural",     "current": 0,  "total": 10, "state": "not\_started" },  
   { "quiz\_type": "esportivo",    "current": 0,  "total": 10, "state": "new" },  
   { "quiz\_type": "diversidade",  "current": 0,  "total": 10, "state": "locked" }  
 \]  
}  
Embrulhado em progress por causa da regra 1.1.  
---

### 3.3. Quiz

#### GET /quiz/random?quiz\_type=ambiental

Precisa aceitar o tipo de quiz como parâmetro. Tocar num personagem do mapa abre o quiz daquele personagem, então não pode ser sorteio entre todas as categorias. Este é o único ponto do contrato que mudou de requisito depois de escrito.  
{  
 "question\_id": "q-8841",  
 "quiz\_type": "ambiental",  
 "statement": "O que ajuda a evitar que o plástico chegue ao mar?",  
 "options": \[  
   { "id": "a", "label": "Jogar embalagens no mar" },  
   { "id": "b", "label": "Usar sacola reutilizável" },  
   { "id": "c", "label": "Deixar a torneira aberta" },  
   { "id": "d", "label": "Queimar o lixo no quintal" }  
 \],  
 "position": 3,  
 "total": 10,  
 "time\_limit": 30  
}

#### POST /quiz/{question\_id}/answer

Envia:  
{ "option\_id": "b" }  
Responde:  
{  
 "is\_correct": true,  
 "correct\_option\_id": "b",  
 "correct\_answer\_text": "Usar sacola reutilizável",  
 "explanation": "Sacola reutilizável evita que o plástico chegue ao mar.",  
 "xp\_gained": 10,  
 "streak\_count": 2,  
 "current\_xp": 330,  
 "next\_level\_xp": 500,  
 "has\_next": true  
}  
Esta resposta preenche a tela de feedback inteira, por isso ela é maior que as outras. O acerto mostra o XP ganho e a sequência; o erro mostra qual era a resposta certa, e é daí que vem correct\_answer\_text.  
---

### 3.4. Missões

#### GET /quests/daily

{  
 "renews\_at": "2026-10-23T03:00:00Z",  
 "quests": \[  
   { "id": "q1", "title": "Responder 5 perguntas", "current": 3, "total": 5,  "reward\_xp": 30, "quiz\_type": null },  
   { "id": "q2", "title": "Acertar 3 seguidas",    "current": 1, "total": 3,  "reward\_xp": 50, "quiz\_type": null },  
   { "id": "q3", "title": "Jogar no Ateliê da Luna","current": 0, "total": 1, "reward\_xp": 40, "quiz\_type": "cultural" }  
 \]  
}

* Concluídas e total saem da contagem da lista, não vêm como campo.  
* quiz\_type vazio significa missão genérica, sem personagem atrás. Como a regra 1.4 impede distinguir ausente de vazio, mande string vazia "" em vez de null.  
* A lista é de tamanho variável. A interface atual assume três cartões; adaptar com "Ver mais" ou paginação é trabalho reconhecido do lado do jogo.

#### GET /quests/daily-bonus

{  
 "reward\_xp": 100,  
 "required\_quests": 3  
}  
---

### 3.5. Perfil

#### GET /profile

A mesma de 3.2.

#### GET /user/stats

{  
 "performance": \[  
   { "quiz\_type": "conhecimento", "percentage": 0.78 },  
   { "quiz\_type": "ambiental",    "percentage": 0.92 },  
   { "quiz\_type": "esportivo",    "percentage": 0.54 },  
   { "quiz\_type": "diversidade",  "percentage": 0.35 },  
   { "quiz\_type": "cultural",     "percentage": 0.20 }  
 \],  
 "answered\_questions": 184,  
 "correct\_average\_questions": 0.71  
}  
correct\_average\_questions vem da API, não é calculado no jogo. O fator de cálculo pode mudar, e o cliente precisa do mesmo número no dashboard dele. Repare que a média simples das categorias não fecha com esse valor, e está certo: são contas diferentes.

#### GET /user/badges

Paginável. São 182 conquistas no catálogo.  
{  
 "badges": \[  
   { "id": "primeiro-passo", "title": "Primeiro passo", "unlocked": true,  "visual": "..." },  
   { "id": "guardiao-vila",  "title": "Guardião da Vila","unlocked": false, "visual": "..." }  
 \],  
 "page": 1,  
 "total\_pages": 8  
}

#### GET /user/inventory

O que o aluno escolheu na customização.  
{  
 "selected": \[  
   { "id": "cabelo-03", "name": "Cacheado", "type": "cabelo" },  
   { "id": "roupa-01",  "name": "Explorador", "type": "roupa" }  
 \]  
}  
O type é necessário para saber em qual aba marcar a seleção.

#### POST /user/inventory

Envia a seleção no mesmo formato do GET.  
---

## 4\. Lacunas que bloqueiam a autenticação

Precisam ser fechadas antes de começar a integração de login.  
4.1. Renovação de sessão. Existe POST /login, mas não existe definição do que acontece quando o token vence. Decidir: quanto tempo o token vale, e se existe refresh token ou se o app refaz o login. Se existir refresh, falta a rota.  
4.2. POST /logout que invalide o token no servidor. Não existe no contrato. Sem ela o logout é só local: o app esquece o token, mas ele continua válido até vencer. Em tablet compartilhado de escola isso significa que um token copiado dá acesso à conta do aluno anterior.  
4.3. Como a API sinaliza token vencido, de forma distinguível de credencial errada e de falta de permissão. Se os três voltarem como 401 sem corpo, o app não sabe se renova sozinho ou se pede a senha de novo. Um código no corpo resolve.  
---

## 5\. Lacunas que não bloqueiam a autenticação

5.1. Registro do token de push. Sem rota que receba o token do aparelho, o backend não tem para onde direcionar a notificação. Push está no escopo. Modelo sugerido: user\_id, token, platform, registered\_at.  
5.2. Quem controla a sessão de 10 perguntas. A tela mostra "3 de 10". Se cada chamada devolve pergunta solta, alguém precisa saber que é a terceira: ou o servidor mantém a sessão, ou o jogo conta e reabrir o app zera o progresso. Preferência do jogo: servidor.  
5.3. Catálogo de customização. O /user/inventory cobre o que o aluno escolheu, não de onde saem as opções: 6 cabelos, 5 tons de pele, 6 roupas, 6 rostos e 6 acessórios. Ou vira rota própria, ou fica fixo no jogo como os personagens. Como não há economia nem item bloqueado, fixo é defensável.  
---

