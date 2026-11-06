using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stintoria.Core;
using Stintoria.Local;

internal static class ChampionshipCli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args.SequenceEqual(new[] { "--ajuda" }))
        {
            Console.WriteLine(
                "STINTORIA — Campeonato Kart (oito etapas)\n" +
                "campeonato novo --arquivo saves/kart.json [--seed 42]\n" +
                "campeonato correr --arquivo saves/kart.json --etapa 1 [--ritmo atacar] [--comandos roteiro.json]\n" +
                "campeonato tabela --arquivo saves/kart.json\n" +
                "campeonato etapa --arquivo saves/kart.json --numero 1 [--eventos]\n" +
                "campeonato exportar --arquivo saves/kart.json --destino exports/kart.json\n" +
                "campeonato importar --origem exports/kart.json --arquivo saves/retomado.json\n" +
                "Ritmos: conservar/equilibrado/atacar, para p01. Roteiro: formato de comandos da corrida avulsa.\n" +
                "Uma corrida por comando; repetir com --etapa 2 até 8. Etapas anteriores não podem ser refeitas.\n" +
                "Salvamento automático após cada etapa completa; retomada entre etapas.\n" +
                "Importar/exportar não sobrescreve destinos existentes.");
            return 0;
        }
        var action = args[0];
        var allowed = action switch {
            "novo" => new[] { "--arquivo", "--seed" },
            "correr" => new[] { "--arquivo", "--etapa", "--ritmo", "--comandos" },
            "tabela" => new[] { "--arquivo" },
            "etapa" => new[] { "--arquivo", "--numero", "--eventos" },
            "exportar" => new[] { "--arquivo", "--destino" },
            "importar" => new[] { "--arquivo", "--origem" },
            _ => throw new ArgumentException("Ação desconhecida. Use campeonato --ajuda.")
        };
        var options = new Dictionary<string, string>();
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i];
            if (!allowed.Contains(key) || options.ContainsKey(key))
                throw new ArgumentException("Opção desconhecida ou repetida.");
            if (key == "--eventos") { options.Add(key, "true"); continue; }
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Falta valor para " + key + ".");
            options.Add(key, args[i]);
        }
        string Required(string key) => options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Informe " + key + ".");
        int Number(string key) => int.TryParse(Required(key), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number : throw new ArgumentException("Número inválido em " + key + ".");
        var path = Required("--arquivo");
        Championship championship;
        switch (action)
        {
            case "novo":
                if (!uint.TryParse(options.GetValueOrDefault("--seed", "42"), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var seed))
                    throw new ArgumentException("Seed deve estar entre 0 e 4294967295.");
                championship = ChampionshipStore.Create(path, seed);
                Console.WriteLine("Campeonato criado. Você comanda Caio Valença (p01), da Aurora Kart.");
                break;
            case "correr":
                var decisions = ReadDecisions(options.GetValueOrDefault("--comandos"));
                if (!decisions.Any(d => d.Sector == 1 && d.DriverId == "p01"))
                    decisions.Add(new SectorDecision(1, "p01", ParsePace(options.GetValueOrDefault("--ritmo", "equilibrado"))));
                else if (options.ContainsKey("--ritmo"))
                    throw new ArgumentException("Use o roteiro ou --ritmo para p01 no setor 1, não ambos.");
                championship = ChampionshipStore.RunRound(path, Number("--etapa"), decisions);
                ShowRound(championship.Rounds.Last(), false);
                Console.WriteLine("Etapa gravada com segurança. Resultados anteriores preservados.");
                break;
            case "tabela":
                championship = ChampionshipStore.Load(path);
                break;
            case "etapa":
                championship = ChampionshipStore.Load(path);
                var round = championship.Rounds.SingleOrDefault(r => r.Number == Number("--numero"))
                    ?? throw new ArgumentException("Etapa ainda não disputada ou inexistente.");
                ShowRound(round, options.ContainsKey("--eventos"));
                return 0;
            case "exportar":
                ChampionshipStore.Export(path, Required("--destino"));
                Console.WriteLine("Campeonato exportado e validado; origem preservada.");
                return 0;
            default:
                championship = ChampionshipStore.Import(Required("--origem"), path);
                Console.WriteLine("Campeonato importado e conferido por replay.");
                break;
        }
        ShowTables(championship);
        return 0;
    }

    private static List<SectorDecision> ReadDecisions(string? path)
    {
        if (path == null) return new List<SectorDecision>();
        if (new FileInfo(path).Length > 256 * 1024) throw new ArgumentException("Roteiro excede 256 KiB.");
        var entries = JsonSerializer.Deserialize<List<ReplayCommand>>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new ArgumentException("Roteiro vazio.");
        return entries.Select(c => c == null ? throw new ArgumentException("Comando nulo.") :
            new SectorDecision(c.Setor, c.Piloto ?? "", ParsePace(c.Ritmo ?? ""))).ToList();
    }

    private static Pace ParsePace(string text) => text switch {
        "conservar" => Pace.Conservar, "equilibrado" => Pace.Equilibrado, "atacar" => Pace.Atacar,
        _ => throw new ArgumentException("Ritmo inválido. Use conservar, equilibrado ou atacar.")
    };

    private static void ShowRound(ChampionshipRound round, bool showEvents)
    {
        var result = round.Result;
        Console.WriteLine($"Etapa {round.Number}/8 — {InitialData.Circuits.Single(c => c.Id == result.CircuitId).Name}" +
            $" | seed {result.Seed} | clima final: {result.Weather}");
        foreach (var driver in result.Classification)
            Console.WriteLine($"{driver.Position,2}. {InitialData.Drivers.Single(d => d.Id == driver.DriverId).Name,-22}" +
                $" {driver.ElapsedMs,8} ms | {driver.Points,2} pontos");
        Console.WriteLine($"{result.Events.Count} eventos registrados. Consulte com etapa --numero {round.Number} --eventos.");
        if (showEvents)
            foreach (var e in result.Events)
                Console.WriteLine($"Setor {e.Step,2} | {(e.DriverId == "" ? "direção de prova" : e.DriverId)} | {e.Explanation}");
    }

    private static void ShowTables(Championship championship)
    {
        Console.WriteLine($"\nCampeonato Kart: {championship.Rounds.Count}/8 etapas | seed {championship.Seed}");
        Console.WriteLine("PILOTOS — pontos | vitórias");
        var position = 0;
        foreach (var row in championship.DriverStandings)
            Console.WriteLine($"{++position,2}. {InitialData.Drivers.Single(d => d.Id == row.Id).Name,-22} {row.Points,3} | {row.Wins}");
        Console.WriteLine("EQUIPES — pontos | vitórias dos pilotos");
        position = 0;
        foreach (var row in championship.TeamStandings)
            Console.WriteLine($"{++position,2}. {InitialData.Teams.Single(t => t.Id == row.Id).Name,-22} {row.Points,3} | {row.Wins}");
        Console.WriteLine("Desempate: pontos; quantidade de 1ºs, 2ºs…20ºs; posições nas etapas mais recentes; ID.");
        Console.WriteLine(championship.Finished
            ? "Campeonato encerrado. Campeões: " +
                InitialData.Drivers.Single(d => d.Id == championship.DriverStandings[0].Id).Name + " / " +
                InitialData.Teams.Single(t => t.Id == championship.TeamStandings[0].Id).Name + "."
            : $"Próxima etapa: {championship.NextRound}/8.");
    }
}
