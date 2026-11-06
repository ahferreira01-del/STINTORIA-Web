using System.Globalization;
using Stintoria.Core;
using Stintoria.Local;

internal static class CareerCli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args.SequenceEqual(new[] { "--ajuda" }))
        {
            Console.WriteLine("STINTORIA — carreira Kart interativa\n" +
                "carreira nova --arquivo saves/carreira.json [--seed 42]\n" +
                "carreira continuar --arquivo saves/carreira.json\n" +
                "carreira exportar --arquivo saves/carreira.json --destino exports/carreira.json\n" +
                "carreira importar --origem exports/carreira.json --arquivo saves/copia.json\n" +
                "carreira importar-unity --arquivo saves/carreira.json --slots pasta/slots --slot 1\n" +
                "Criação: piloto fictício de 14 anos, nacionalidade, origem, equipe e confirmação da vaga.\n" +
                "Menu: preparar, correr uma etapa, tabelas, último resultado, salvar e sair, histórico completo, plano de três trechos.\n" +
                "Opção 7: trechos configuráveis, previsão qualitativa e confirmação antes de salvar.\n" +
                "Opção 8: mesmo plano do jogador com planos táticos de IA registrados antes da largada.\n" +
                "Treino técnico: 1 dos 4 créditos da temporada, +2 habilidade permanente. Descanso: gratuito.\n" +
                "Opção 9: corrida participativa, checkpoint após cada comando, entre setores (não dentro deles).\n" +
                "Saves antigos de campeonato não são convertidos; continuam nos comandos campeonato.");
            return 0;
        }
        var action = args[0];
        var allowed = action switch {
            "nova" => new[] { "--arquivo", "--seed" },
            "continuar" => new[] { "--arquivo" },
            "exportar" => new[] { "--arquivo", "--destino" },
            "importar" => new[] { "--origem", "--arquivo" },
            "importar-unity" => new[] { "--arquivo", "--slots", "--slot" },
            _ => throw new ArgumentException("Ação desconhecida. Use carreira --ajuda.")
        };
        var options = new Dictionary<string, string>();
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i];
            if (!allowed.Contains(key) || options.ContainsKey(key) || ++i >= args.Length ||
                args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Opção desconhecida, repetida ou sem valor.");
            options.Add(key, args[i]);
        }
        string Required(string key) => options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Informe " + key + ".");
        var path = Required("--arquivo");
        if (action == "importar-unity")
            return CareerMigrationCli.Run(path, Required("--slots"), Required("--slot"));
        if (action == "exportar")
        {
            CareerStore.Export(path, Required("--destino"));
            Console.WriteLine("Carreira exportada e validada. Origem preservada.");
            return 0;
        }
        if (action == "importar")
        {
            CareerStore.Import(Required("--origem"), path);
            Console.WriteLine("Carreira importada e validada. Use carreira continuar para jogar.");
            return 0;
        }
        CareerSession career;
        if (action == "nova")
        {
            if (!uint.TryParse(options.GetValueOrDefault("--seed", "42"), NumberStyles.None,
                CultureInfo.InvariantCulture, out var seed))
                throw new ArgumentException("Seed deve estar entre 0 e 4294967295.");
            if (File.Exists(path)) throw new ArgumentException("Destino já existe; nenhuma carreira será substituída.");
            Console.WriteLine("Crie um piloto fictício de 14 anos. Todas as origens começam com os mesmos recursos.");
            var name = Text("Nome fictício: ", input =>
                new CareerProfile(input, "Brasileira", CareerOrigin.FamiliaHumilde, "t01").Name);
            var nationality = Text("Nacionalidade: ", input =>
                new CareerProfile(name, input, CareerOrigin.FamiliaHumilde, "t01").Nationality);
            Console.WriteLine("Origem: 1 família humilde | 2 ligada ao esporte | 3 empresária | 4 sim racer");
            var origin = (CareerOrigin)(Choice("Origem: ", 1, 4) - 1);
            Console.WriteLine("Equipes elegíveis — vaga inicial explicitamente identificada:");
            for (var i = 0; i < CareerProfile.EligibleTeams.Count; i++)
            {
                var t = CareerProfile.EligibleTeams[i];
                var displaced = InitialData.Drivers.First(d => d.TeamId == t.Id);
                Console.WriteLine($"{i + 1}. {t.Name}: vaga de {displaced.Name} ({displaced.Id})");
            }
            var team = CareerProfile.EligibleTeams[Choice("Equipe: ", 1, CareerProfile.EligibleTeams.Count) - 1];
            var profile = new CareerProfile(name, nationality, origin, team.Id);
            var prior = InitialData.Drivers.Single(d => d.Id == profile.DisplacedDriverId);
            Console.WriteLine($"{name} terá ID jogador e ocupará a vaga de {prior.Name} ({prior.Id}) na {team.Name}." +
                "\nIsso vale somente nesta NOVA carreira: 20 pilotos, dois por equipe. Saves anteriores não mudam.");
            var confirmation = Text("Confirmar entrada? (s/n): ", input =>
                input.Trim().ToLowerInvariant() is "s" or "n" ? input.Trim().ToLowerInvariant()
                    : throw new ArgumentException("Responda s ou n."));
            if (confirmation == "n") { Console.WriteLine("Criação cancelada; nenhum save foi criado."); return 0; }
            career = CareerStore.Create(path, seed, profile);
            Console.WriteLine("Carreira criada e salva.");
        }
        else career = CareerStore.Load(path);
        Menu(path, career);
        return 0;
    }

    private static void Menu(string path, CareerSession career)
    {
        while (true)
        {
            CareerDisplay.Status(career);
            Console.WriteLine("1 Preparar etapa | 2 Correr etapa preparada | 3 Classificações | 4 Último resultado | 5 Salvar e sair | 6 Histórico completo | 7 Preparar plano de três trechos | 8 Plano com IA tática | 9 Corrida participativa");
            Console.Write("Escolha: ");
            var line = Console.ReadLine();
            if (line == null || line.Trim() == "5")
            { Console.WriteLine("Progresso já salvo. Até a próxima!"); return; }
            if (!int.TryParse(line, out var choice) || choice < 1 || choice > 9)
            { Console.WriteLine("Opção inválida. Escolha de 1 a 9."); continue; }
            try
            {
                if (choice == 1)
                {
                    if (career.Finished || career.Pending != null)
                        throw new InvalidOperationException(career.Finished ? "Campeonato encerrado." : "Etapa já preparada; não é possível treinar duas vezes.");
                    Console.WriteLine($"Treinamento: 1 descansar (grátis) | 2 técnico (1 crédito, +2 habilidade). Disponíveis: {career.TrainingCredits}." +
                        "\nEscolha definitiva para esta etapa, salva automaticamente.");
                    var training = (Training)(Choice("Treino: ", 1, 2) - 1);
                    Console.WriteLine("Ritmo: 1 conservar | 2 equilibrado | 3 atacar (mais desgaste e risco).");
                    var pace = (Pace)(Choice("Ritmo: ", 1, 3) - 1);
                    career = CareerStore.Prepare(path, career.NextRound, training, pace);
                    Console.WriteLine("Preparação salva. Você pode fechar e retomar antes de correr.");
                }
                else if (choice == 2)
                {
                    career = CareerStore.RunRound(path, career.NextRound);
                    Console.WriteLine("Etapa concluída e salva.");
                    CareerDisplay.LastResult(career);
                    CareerDisplay.Tables(career);
                }
                else if (choice == 3) CareerDisplay.Tables(career);
                else if (choice == 6) HistoryDisplay.Browse(HistoryQuery.ForCareer(career));
                else if (choice == 7) career = PreparePlan(path, career);
                else if (choice == 8) career = PreparePlan(path, career, true);
                else if (choice == 9) career = ParticipatoryCli.Play(path, career);
                else CareerDisplay.LastResult(career);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                Console.WriteLine("Decisão rejeitada: " + ex.Message);
                career = CareerStore.Load(path);
            }
        }
    }

    private static CareerSession PreparePlan(string path, CareerSession career, bool tactical = false)
    {
        if (career.Finished || career.Pending != null)
            throw new InvalidOperationException(career.Finished ? "Campeonato encerrado." :
                "Etapa já preparada; não é possível substituir o plano ou treinar duas vezes.");
        Console.WriteLine($"Treinamento: 1 descansar (grátis) | 2 técnico (1 crédito, +2 habilidade). Disponíveis: {career.TrainingCredits}.");
        var training = (Training)(Choice("Treino do plano: ", 1, 2) - 1);
        if (training == Training.Tecnico && career.TrainingCredits == 0)
            throw new InvalidOperationException("Créditos de treino esgotados. Escolha descansar.");
        Console.WriteLine("Corrida: 36 setores (12 voltas × 3). Início começa no setor 1." +
            "\nConfigure o primeiro setor do meio e do final; nenhum trecho pode ficar vazio.");
        var middle = Boundary("Primeiro setor do meio", 2, 35, 13);
        var final = Boundary("Primeiro setor do final", middle + 1, 36, Math.Max(25, middle + 1));
        Console.WriteLine("Ritmo por trecho: 1 CONSERVAR | 2 EQUILIBRADO | 3 ATACAR.");
        var beginningPace = (Pace)(Choice("Ritmo do início: ", 1, 3) - 1);
        var middlePace = (Pace)(Choice("Ritmo do meio: ", 1, 3) - 1);
        var finalPace = (Pace)(Choice("Ritmo do final: ", 1, 3) - 1);
        var plan = new RaceStrategy(beginningPace, middlePace, finalPace, middle, final);
        StrategyDisplay.Show(plan);
        if (tactical) Console.WriteLine("IA tática: 19 rivais recebem planos por habilidade e condição inicial seca." +
            " Sem acesso ao clima futuro, bônus ou vitória garantida. Formato de save v3 após confirmar.");
        Console.WriteLine($"Treinamento a confirmar: {(training == Training.Tecnico ? "técnico, custa 1 crédito" : "descanso, gratuito")}." +
            " Confirmar grava a preparação; ela não poderá ser revisada nesta etapa.");
        var confirmation = Text("Confirmar plano? (s/n): ", input =>
            input.Trim().ToLowerInvariant() is "s" or "n" ? input.Trim().ToLowerInvariant() :
                throw new ArgumentException("Responda s ou n."));
        if (confirmation == "n")
        { Console.WriteLine("Plano cancelado. Nenhum crédito gasto ou escolha gravada."); return career; }
        var saved = tactical ? CareerStore.PrepareTactical(path, career.NextRound, training, plan) :
            CareerStore.PrepareStrategy(path, career.NextRound, training, plan);
        Console.WriteLine("Plano confirmado e salvo. Você pode fechar e retomar antes de correr.");
        return saved;
    }

    private static int Boundary(string name, int min, int max, int defaultValue)
        => int.Parse(Text($"{name} ({min}..{max}, Enter = {defaultValue}): ", input =>
            string.IsNullOrWhiteSpace(input) ? defaultValue.ToString(CultureInfo.InvariantCulture) :
            int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
                ? input : throw new ArgumentException($"Escolha um setor de {min} a {max}.")), CultureInfo.InvariantCulture);

    private static int Choice(string prompt, int min, int max)
        => int.Parse(Text(prompt, input => int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
            value >= min && value <= max ? input : throw new ArgumentException($"Escolha um número de {min} a {max}.")),
            CultureInfo.InvariantCulture);

    private static string Text(string prompt, Func<string, string> validate)
    {
        while (true)
        {
            Console.Write(prompt);
            var line = Console.ReadLine() ?? throw new OperationCanceledException(
                "Entrada encerrada antes da confirmação. Nenhuma escolha incompleta foi gravada.");
            try { return validate(line); }
            catch (ArgumentException ex) { Console.WriteLine("Entrada inválida: " + ex.Message); }
        }
    }
}
