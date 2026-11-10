### **1\. Quais plataformas vamos entregar e qual versão do Unity está sendo usada? Há alguma restrição de bibliotecas, serialização, IL2CPP ou WebGL?**

Unity 6000.6.0f1, URP 17.6.0 com o 2D Renderer. A interface inteira é UI Toolkit, não UGUI.

Plataformas: Android é a principal e já está configurada. iOS está no plano, ainda não configurado. Chromebook roda o próprio build Android via ARC, não é target separado.

Android hoje: IL2CPP, minSdk 26, targetSdk 36, .NET Standard 2.1, strip engine code ligado. HTTP pelo UnityWebRequest, que é nativo. Precisa ser HTTPS, o Android bloqueia texto puro por padrão desde o 9\.

BIBLIOTECAS: nenhuma adicional, logo a desserialização é com o JsonUtility padrão da Unity. Ele é rápido e não aumenta o APK, mas é limitado, e esses limites viram requisito de formato no backend. Seguem algumas convenções a serem seguidas pelo backend para que não haja problemas de desserialização:

1. Objeto na raiz de toda resposta, nunca array solto. O JsonUtility não desserializa um array no topo do JSON. As outras já estão com o array dentro de um campo nomeado, então essas estão certas.  
2. Sem chave dinâmica. Nada de {"ambiental": {...}, "conhecimento": {...}}. Dicionário não existe para o serializador da Unity, as chaves têm que ser nomes de campo fixos. Lista de objetos com um campo quiz\_type dentro resolve, e é o que o contrato já faz.  
3. Os nomes dos campos têm que bater exatamente com os campos da classe em C\#, inclusive maiúscula e minúscula. O JsonUtility não tem atributo para renomear chave. Como a convenção está em snake\_case, o Unity deixa as classes do jogo em snake\_case também.  
4. Campo ausente não é distinguível de zero. Se a chave não vem no JSON, o campo fica com o valor padrão do tipo, e o jogo não tem como saber se veio zero ou se não veio nada. Precisa de um booleano explícito no payload, tipo has\_event, ou a rota responder 204 sem corpo. Qualquer um dos dois serve.  
5. Data e hora como texto ISO 8601 ou timestamp Unix. O serializador não lê DateTime, então manda string e o jogo converte. Afeta ends\_at do evento, renews\_at das missões diárias e a expiração do token.  
6. Enum como texto no payload. Backend deve enviar string, que do lado do Unity entra como string e será mapeado manualmente. Vale para quiz\_type e para o state do progresso.  
7. Uma forma por rota, sem polimorfismo. Nada de um campo "type" que muda o resto do objeto conforme o valor. O serializador não resolve isso.

OBS: chave que chega no JSON e não existe na classe do jogo é ignorada sem erro. Então o backend pode adicionar campo em qualquer rota sem quebrar build antigo. O contrário não vale, remover ou renomear campo quebra.

### **2\. Já tem uma build disponível para testar a integração?**

Não, build com integração não existe, é necessário o ambiente para consumir as APIs.

Existe build do app, hoje na 0.1.9 em APK Android, e sai outra nos próximos dias com o mapa novo. Mas ela não serve para teste de integração: não há nenhuma camada de rede no projeto ainda, nem cliente HTTP, nem serviço, nem desserialização. Todos os dados das telas são fixos, escritos direto no layout. Nome do jogador, XP, perguntas, alternativas, missões e estatísticas são exemplos escritos à mão, e o login aceita qualquer credencial porque não consulta nada.

Para eu produzir uma build de integração eu preciso de três coisas:

1. A URL base do ambiente de pé, que é o que está preso na liberação da Railway.  
2. Uma credencial de teste válida nesse ambiente.

### **3\. Quem vai implementar no Unity o login, a renovação da sessão, o logout e o armazenamento seguro da sessão? Eu ou você?**

Implementado do lado do Aplicativo Android (Unity): a chamada de login, a guarda da sessão, o logout e o armazenamento seguro. Isso é tudo do aplicativo. 

Preciso destas definições do lado do backend para a sessão de usuário:

1. Quanto tempo o token vale.  
2. Se existe refresh token ou se o app refaz o login quando vence. Se existir refresh, preciso da rota e do que ela devolve.  
3. O que a API responde quando o token está vencido, e de um jeito que eu consiga distinguir dos outros casos. Se credencial errada, token vencido e falta de permissão voltarem todos como 401 sem corpo, o app não sabe se manda a criança digitar a senha de novo ou se renova sozinho. Um código no corpo da resposta resolve.  
4. Se o logout precisa de chamada no servidor para invalidar o token, ou se é só local. Isso importa mais do que parece: se for só local, o token continua válido até vencer, e num tablet compartilhado de escola a criança seguinte entra com a sessão da anterior se alguém guardar aquele token. Se você invalidar no servidor, isso fecha.

Sobre o armazenamento seguro, é do lado do aplicativo Android (Unity). Não vai em PlayerPrefs, que é texto puro no dispositivo. Vai no Keystore do Android via JNI, que o projeto já tem habilitado, e no Keychain quando chegar a vez do iOS.

### **4\. Como o jogo deverá lidar com aparelho compartilhado e troca de aluno?**

Do lado técnico a regra é: logout invalida o token no servidor, e o token de sessão fica guardado criptografado no aparelho, Keystore no Android e Keychain no iOS.

A troca de aluno fica assim: a criança sai, o app chama o logout, você invalida o token, o app apaga o token e qualquer dado de perfil em cache, e a próxima criança entra do zero. Nada da sessão anterior sobra no aparelho nem segue valendo no servidor.

Para isso eu preciso de uma rota de logout que invalide o token do usuário.

### **5\. Quais dados o jogo precisa receber após o login? Se possível, manda um exemplo.**

Estou enviando em conjunto a estas respostas um documento com as rotas de API e exemplos de resposta de cada uma.

### **6\. Você já tem algum contrato de API proposto antes de começarmos a autenticação?**

Tenho. Montado tela a tela a partir do que cada componente realmente renderiza   
Segue no documento de rotas (`rotas-api.md`, nesta pasta). *Link de compartilhamento removido.*

### **7\. Qual sua disponibilidade para implementar e testar essa integração? A demonstração de 25/10 ainda é viável do seu lado, considerando que a homologação está pendente?**

Não é viável, visto que não temos ambiente de teste ainda. Não há como implementar corretamente sem poder testar as chamadas de API. Do lado do Unity estou deixando essas respostas mockadas. Quando tivermos o ambiente de pé consigo integrar todas as rotas em 3 dias.

### **8\. Para preparar as notificações futuras: quais serão os identificadores dos aplicativos e quem cuidará da configuração Firebase e, se houver iOS, APNs?**

ANDROID  
com.rocks.universodohelinho, que é o que está no projeto hoje.

A divisão que eu proponho:  
Eu integro o SDK do Firebase no jogo, peço a permissão de notificação, obtenho o token do aparelho e mando para vocês.

