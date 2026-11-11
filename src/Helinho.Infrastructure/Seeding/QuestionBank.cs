using Helinho.Domain;

namespace Helinho.Infrastructure.Seeding;

/// <summary>Banco sintético de questões: 3 por tipo e faixa (60 aprovadas) e 5 rascunhos. Conteúdo adequado a crianças.</summary>
public static class QuestionBank
{
    public sealed record Item(QuizType Type, AgeBand Band, string Statement, string Explanation, string Correct, string[] Wrong, bool Approved = true);

    private const AgeBand B1 = AgeBand.Age6To7;
    private const AgeBand B2 = AgeBand.Age8To10;
    private const AgeBand B3 = AgeBand.Age11To12;
    private const AgeBand B4 = AgeBand.Age13To14;

    private static Item Q(QuizType t, AgeBand b, string s, string e, string c, params string[] w) => new(t, b, s, e, c, w);

    private static Item Draft(QuizType t, AgeBand b, string s, string e, string c, params string[] w) => new(t, b, s, e, c, w, false);

    public static IReadOnlyList<Item> All { get; } =
    [
        // Ambiental
        Q(QuizType.Ambiental, B1, "De que cor é a lixeira de papel?", "A lixeira azul recebe papel e papelão.", "Azul", "Verde", "Amarela", "Vermelha"),
        Q(QuizType.Ambiental, B1, "O que fazer com a torneira enquanto escovamos os dentes?", "Fechar a torneira evita desperdiçar água.", "Fechar a torneira", "Deixar aberta", "Encher a pia", "Brincar com a água"),
        Q(QuizType.Ambiental, B1, "Qual destes animais vive nas florestas do Brasil?", "A onça-pintada vive em florestas e áreas alagadas brasileiras.", "Onça-pintada", "Pinguim", "Camelo", "Urso-polar"),
        Q(QuizType.Ambiental, B2, "De que cor é a lixeira de vidro?", "A lixeira verde recebe vidro.", "Verde", "Azul", "Amarela", "Preta"),
        Q(QuizType.Ambiental, B2, "Como se chama transformar material usado em produto novo?", "Reciclar poupa recursos da natureza.", "Reciclagem", "Poluição", "Queimada", "Desmatamento"),
        Q(QuizType.Ambiental, B2, "O que as plantas usam para fazer a fotossíntese?", "A luz do sol é a fonte de energia da fotossíntese.", "Luz do sol", "Barulho", "Areia seca", "Plástico"),
        Q(QuizType.Ambiental, B3, "Qual gás as plantas absorvem do ar na fotossíntese?", "As plantas absorvem gás carbônico e liberam oxigênio.", "Gás carbônico", "Hélio", "Neônio", "Argônio"),
        Q(QuizType.Ambiental, B3, "Qual é um exemplo de energia renovável?", "A luz do sol se renova todos os dias.", "Energia solar", "Carvão mineral", "Petróleo", "Gás natural"),
        Q(QuizType.Ambiental, B3, "Qual é a maior floresta tropical do mundo?", "A Amazônia é a maior floresta tropical do planeta.", "Amazônia", "Pampa", "Caatinga", "Pantanal"),
        Q(QuizType.Ambiental, B4, "Qual é a principal causa do aquecimento global atual?", "A queima de combustíveis fósseis lança gases que retêm calor.", "Queima de combustíveis fósseis", "Excesso de chuva", "Ventos fortes", "Fases da Lua"),
        Q(QuizType.Ambiental, B4, "O que significa sustentabilidade?", "É usar os recursos hoje sem faltar para as próximas gerações.", "Usar recursos sem prejudicar o futuro", "Gastar tudo rápido", "Só plantar árvores", "Só reciclar"),
        Q(QuizType.Ambiental, B4, "Qual bioma é só brasileiro e tem clima semiárido?", "A Caatinga existe apenas no Brasil.", "Caatinga", "Amazônia", "Pampa", "Mata Atlântica"),
        // Conhecimento
        Q(QuizType.Conhecimento, B1, "Quantos dias tem uma semana?", "Uma semana tem sete dias.", "7", "5", "10", "12"),
        Q(QuizType.Conhecimento, B1, "Quanto é 2 + 3?", "Dois mais três é igual a cinco.", "5", "4", "6", "7"),
        Q(QuizType.Conhecimento, B1, "Qual é a primeira letra do alfabeto?", "O alfabeto começa na letra A.", "A", "B", "Z", "M"),
        Q(QuizType.Conhecimento, B2, "Quanto é 7 × 8?", "Sete vezes oito é igual a 56.", "56", "54", "49", "64"),
        Q(QuizType.Conhecimento, B2, "Qual é a capital do Brasil?", "Brasília é a capital federal.", "Brasília", "Rio de Janeiro", "São Paulo", "Salvador"),
        Q(QuizType.Conhecimento, B2, "Quantos lados tem um hexágono?", "Hexa significa seis.", "6", "5", "8", "7"),
        Q(QuizType.Conhecimento, B3, "Qual planeta é conhecido como planeta vermelho?", "Marte parece avermelhado por causa do óxido de ferro.", "Marte", "Vênus", "Júpiter", "Saturno"),
        Q(QuizType.Conhecimento, B3, "Quanto é 3/4 de 20?", "20 dividido por 4 é 5, e 5 vezes 3 é 15.", "15", "12", "16", "10"),
        Q(QuizType.Conhecimento, B3, "Qual é o maior oceano do mundo?", "O Pacífico é o maior e o mais profundo.", "Pacífico", "Atlântico", "Índico", "Ártico"),
        Q(QuizType.Conhecimento, B4, "Qual é a raiz quadrada de 144?", "12 vezes 12 é igual a 144.", "12", "14", "11", "16"),
        Q(QuizType.Conhecimento, B4, "Quem escreveu o romance Dom Casmurro?", "A obra é de Machado de Assis, publicada em 1899.", "Machado de Assis", "José de Alencar", "Monteiro Lobato", "Clarice Lispector"),
        Q(QuizType.Conhecimento, B4, "Qual é a fórmula química da água?", "Cada molécula de água tem dois hidrogênios e um oxigênio.", "H2O", "CO2", "O2", "NaCl"),
        // Esportivo
        Q(QuizType.Esportivo, B1, "Qual esporte se joga com os pés, uma bola e dois gols?", "No futebol os jogadores chutam a bola para marcar gols.", "Futebol", "Natação", "Judô", "Ciclismo"),
        Q(QuizType.Esportivo, B1, "O que é bom fazer antes de se exercitar?", "Alongar prepara o corpo e ajuda a evitar machucados.", "Alongar", "Dormir", "Comer muito doce", "Ficar parado"),
        Q(QuizType.Esportivo, B1, "O que devemos beber durante o esporte?", "A água hidrata o corpo.", "Água", "Café", "Energético", "Refrigerante"),
        Q(QuizType.Esportivo, B2, "Quantos jogadores de cada time ficam em quadra no vôlei?", "Cada time tem seis jogadores em quadra.", "6", "5", "7", "11"),
        Q(QuizType.Esportivo, B2, "Em qual esporte se arremessa a bola em uma cesta?", "No basquete a bola vai para a cesta.", "Basquete", "Tênis", "Natação", "Atletismo"),
        Q(QuizType.Esportivo, B2, "Qual país sediou os Jogos Olímpicos de 2016?", "O Rio de Janeiro foi a sede em 2016.", "Brasil", "Japão", "China", "França"),
        Q(QuizType.Esportivo, B3, "Quantos jogadores de cada time ficam em campo no futebol?", "Cada equipe joga com onze atletas.", "11", "9", "10", "12"),
        Q(QuizType.Esportivo, B3, "Em qual esporte existe a prova dos 100 metros rasos?", "A prova dos 100 metros pertence ao atletismo.", "Atletismo", "Natação", "Ginástica", "Judô"),
        Q(QuizType.Esportivo, B3, "O que significa fair play?", "Fair play é jogar limpo e respeitar os outros.", "Jogo limpo e respeito", "Ganhar de qualquer jeito", "Jogar sozinho", "Jogar à noite"),
        Q(QuizType.Esportivo, B4, "De quantos em quantos anos acontecem os Jogos Olímpicos de verão?", "Os Jogos de verão ocorrem a cada quatro anos.", "4 anos", "2 anos", "3 anos", "5 anos"),
        Q(QuizType.Esportivo, B4, "Qual é a distância aproximada de uma maratona?", "A maratona tem cerca de 42 quilômetros.", "42 km", "21 km", "10 km", "100 km"),
        Q(QuizType.Esportivo, B4, "Qual esporte paralímpico usa bola com guizo e atletas de olhos vendados?", "Esse esporte é o goalball.", "Goalball", "Bocha", "Judô", "Basquete em cadeira de rodas"),
        // Cultural
        Q(QuizType.Cultural, B1, "Qual destes é um ritmo brasileiro?", "O samba nasceu no Brasil.", "Samba", "Flamenco", "Haka", "Polca"),
        Q(QuizType.Cultural, B1, "Qual é a língua mais falada no Brasil?", "No Brasil falamos português.", "Português", "Espanhol", "Inglês", "Francês"),
        Q(QuizType.Cultural, B1, "Qual festa tem fantasias e desfiles no começo do ano?", "O Carnaval tem blocos, fantasias e desfiles.", "Carnaval", "Natal", "Páscoa", "Dia das Bruxas"),
        Q(QuizType.Cultural, B2, "Qual personagem do folclore tem uma perna só e gorro vermelho?", "O Saci-Pererê é um personagem do folclore brasileiro.", "Saci-Pererê", "Curupira", "Boitatá", "Iara"),
        Q(QuizType.Cultural, B2, "Qual é um prato típico da Bahia?", "O acarajé é um bolinho típico baiano.", "Acarajé", "Pão de queijo", "Barreado", "Tacacá"),
        Q(QuizType.Cultural, B2, "Quem já vivia no Brasil antes de os portugueses chegarem?", "Os povos indígenas habitavam o território havia milhares de anos.", "Povos indígenas", "Vikings", "Romanos", "Astecas"),
        Q(QuizType.Cultural, B3, "Quem pintou a Mona Lisa?", "A obra é de Leonardo da Vinci.", "Leonardo da Vinci", "Picasso", "Van Gogh", "Monet"),
        Q(QuizType.Cultural, B3, "Qual artista brasileira pintou o Abaporu?", "O Abaporu é de Tarsila do Amaral, de 1928.", "Tarsila do Amaral", "Frida Kahlo", "Anita Malfatti", "Djanira"),
        Q(QuizType.Cultural, B3, "Qual dança é típica das festas juninas?", "A quadrilha é dançada nas festas de junho.", "Quadrilha", "Balé", "Tango", "Break"),
        Q(QuizType.Cultural, B4, "Qual movimento marcou a Semana de Arte Moderna de 1922?", "A Semana de 22 foi o marco do Modernismo no Brasil.", "Modernismo", "Barroco", "Romantismo", "Realismo"),
        Q(QuizType.Cultural, B4, "Qual instrumento é símbolo da capoeira?", "O berimbau comanda o ritmo da roda de capoeira.", "Berimbau", "Violino", "Piano", "Gaita"),
        Q(QuizType.Cultural, B4, "Quem escreveu o romance O Guarani?", "A obra é de José de Alencar.", "José de Alencar", "Graciliano Ramos", "Cecília Meireles", "Carlos Drummond de Andrade"),
        // Diversidade
        Q(QuizType.Diversidade, B1, "Como todas as crianças merecem ser tratadas?", "Todas merecem respeito.", "Com respeito", "Com zombaria", "Com indiferença", "Com gritos"),
        Q(QuizType.Diversidade, B1, "Um colega usa cadeira de rodas. O que podemos fazer?", "Convidar o colega para brincar inclui todo mundo.", "Convidar para brincar", "Ignorar", "Rir", "Esconder a cadeira"),
        Q(QuizType.Diversidade, B1, "Pessoas diferentes podem ser amigas?", "As diferenças tornam a amizade mais rica.", "Sim, claro", "Não", "Só se forem iguais", "Só os adultos"),
        Q(QuizType.Diversidade, B2, "O que é inclusão?", "Incluir é garantir que todos participem.", "Fazer todos participarem", "Deixar alguém de fora", "Escolher só os amigos", "Separar por turma"),
        Q(QuizType.Diversidade, B2, "Como se chama o sistema de escrita em pontos usado por pessoas cegas?", "O braille é lido com as pontas dos dedos.", "Braille", "Libras", "Morse", "Cuneiforme"),
        Q(QuizType.Diversidade, B2, "O que é Libras?", "Libras é a Língua Brasileira de Sinais, usada pela comunidade surda.", "Língua Brasileira de Sinais", "Livro de receitas", "Liga de esportes", "Lista de sinais de trânsito"),
        Q(QuizType.Diversidade, B3, "O que é bullying?", "Bullying é humilhar ou agredir alguém de forma repetida.", "Humilhar alguém repetidamente", "Brincar entre amigos", "Elogiar um colega", "Jogar em equipe"),
        Q(QuizType.Diversidade, B3, "O que ajuda quem sofre bullying?", "Apoiar e contar a um adulto de confiança ajuda a parar a situação.", "Apoiar e avisar um adulto", "Rir junto", "Ficar em silêncio", "Repetir a ofensa"),
        Q(QuizType.Diversidade, B3, "Por que escolas acessíveis são importantes?", "A acessibilidade permite que todos estudem e circulem com autonomia.", "Para todos estudarem juntos", "Só para enfeitar", "Para ter menos alunos", "Para gastar mais"),
        Q(QuizType.Diversidade, B4, "O que é empatia?", "Empatia é se colocar no lugar do outro.", "Se colocar no lugar do outro", "Concordar sempre", "Evitar conflitos", "Sentir pena de todos"),
        Q(QuizType.Diversidade, B4, "O que é preconceito?", "É um julgamento formado sem conhecer a pessoa.", "Julgar sem conhecer", "Respeitar as diferenças", "Ter curiosidade", "Ouvir com atenção"),
        Q(QuizType.Diversidade, B4, "Qual lei garante direitos às crianças e aos adolescentes?", "O ECA é o Estatuto da Criança e do Adolescente.", "ECA", "Código de Trânsito", "Estatuto do Torcedor", "Código Florestal"),
        // Rascunhos (não chegam ao jogo)
        Draft(QuizType.Ambiental, B2, "Qual é o destino correto das pilhas usadas?", "Pilhas vão para pontos de coleta específicos.", "Pontos de coleta", "Lixo comum", "Rio", "Quintal"),
        Draft(QuizType.Conhecimento, B3, "Quantos continentes existem?", "A divisão mais usada no Brasil tem seis continentes.", "6", "4", "8", "9"),
        Draft(QuizType.Esportivo, B4, "Qual esporte combina natação, ciclismo e corrida?", "Essa prova é o triatlo.", "Triatlo", "Pentatlo", "Biatlo", "Remo"),
        Draft(QuizType.Cultural, B1, "Qual instrumento tem teclas brancas e pretas?", "O piano tem teclas brancas e pretas.", "Piano", "Flauta", "Tambor", "Violão"),
        Draft(QuizType.Diversidade, B2, "Qual símbolo indica vaga para pessoa com deficiência?", "É o símbolo internacional de acessibilidade.", "Símbolo de acessibilidade", "Seta azul", "Estrela", "Coração"),
    ];
}
