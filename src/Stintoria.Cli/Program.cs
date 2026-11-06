using System.Globalization;
using System.Text;
using System.Text.Json;
using Stintoria.Core;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
Console.OutputEncoding = new UTF8Encoding(false);
try
{
    if (args.FirstOrDefault() == "campeonato")
        return ChampionshipCli.Run(args.Skip(1).ToArray());
    if (args.FirstOrDefault() == "carreira")
        return CareerCli.Run(args.Skip(1).ToArray());
    if (args.Contains("--ajuda"))
    {
        Console.WriteLine("STINTORIA 0.1 — protótipo de terminal\n" +
            "Opções: --seed UINT --pista vale|porto|serra --ritmo conservar|equilibrado|atacar\n" +
            "--clima seco|chuva --comandos arquivo.json --json\n" +
            "Campeonato de oito etapas: campeonato --ajuda\n" +
            "Carreira interativa: carreira --ajuda\n" +
            "Comandos: [{\"setor\":1,\"piloto\":\"p01\",\"ritmo\":\"atacar\"}]. Setores 1..36.\n" +
            "Sem --comandos, p01 usa --ritmo. Demais pilotos começam equilibrados.");
        return 0;
    }
    var known = new HashSet<string> { "--seed", "--pista", "--ritmo", "--clima", "--comandos", "--json" };
    var options = new Dictionary<string, string>();
    for (var i = 0; i < args.Length; i++)
    {
        if (!known.Contains(args[i]) || options.ContainsKey(args[i])) throw new ArgumentException("Opção desconhecida ou repetida.");
        var key = args[i];
        if (key == "--json") { options.Add(key, "true"); continue; }
        if (++i >= args.Length) throw new ArgumentException("Falta valor para " + key);
        options.Add(key, args[i]);
    }
    string Get(string key, string fallback) => options.GetValueOrDefault(key, fallback);
    if (!uint.TryParse(Get("--seed", "42"), NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
        throw new ArgumentException("Seed deve ser um inteiro entre 0 e 4294967295.");
    var circuit = InitialData.Circuits.SingleOrDefault(x => x.Id == Get("--pista", "vale"))
        ?? throw new ArgumentException("Pista desconhecida.");
    var initialPace = ParsePace(Get("--ritmo", "equilibrado"));
    var weather = Get("--clima", "seco") switch {
        "seco" => Weather.Seco, "chuva" => Weather.Chuva, _ => throw new ArgumentException("Clima inválido.")
    };
    var commands = options.TryGetValue("--comandos", out var file)
        ? JsonSerializer.Deserialize<List<ReplayCommand>>(File.ReadAllText(file),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling =
                System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new ArgumentException("Arquivo de comandos vazio.")
        : new List<ReplayCommand>();
    var schedule = new Dictionary<int, Dictionary<string, Pace>>();
    foreach (var cmd in commands)
    {
        if (cmd == null || cmd.Setor < 1 || cmd.Setor > KartRules.DefaultLaps * 3 ||
            !InitialData.Drivers.Any(d => d.Id == cmd.Piloto))
            throw new ArgumentException("Setor/piloto inválido no replay.");
        if (!schedule.TryGetValue(cmd.Setor, out var batch))
            schedule.Add(cmd.Setor, batch = new Dictionary<string, Pace>());
        if (!batch.TryAdd(cmd.Piloto!, ParsePace(cmd.Ritmo ?? "")))
            throw new ArgumentException("Comando duplicado para piloto no mesmo setor.");
    }
    var race = new RaceSimulation(InitialData.Drivers, InitialData.Teams, circuit, seed,
        initialWeather: weather);
    for (var step = 1; step <= KartRules.DefaultLaps * 3; step++)
    {
        var batch = schedule.GetValueOrDefault(step, new Dictionary<string, Pace>());
        if (step == 1 && !batch.ContainsKey("p01")) batch.Add("p01", initialPace);
        race.Advance(batch);
    }
    var state = race.State;
    if (options.ContainsKey("--json"))
        Console.WriteLine(JsonSerializer.Serialize(new { sim = RaceState.SimulationVersion,
            rng = SeededRandom.Version, state.Seed, state.CircuitId, state.TotalLaps,
            state.CompletedSectors, state.Finished, clima = state.Weather.ToString(),
            classificacao = state.Classification, eventos = state.Events
        }, new JsonSerializerOptions { WriteIndented = true, Converters = {
            new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
    else
    {
        Console.WriteLine($"STINTORIA 0.1 | {circuit.Name} | seed {seed} | {SeededRandom.Version}");
        Console.WriteLine($"Kart fictício • {state.TotalLaps} voltas • tempos em ms • clima final: {state.Weather}");
        foreach (var result in state.Classification)
            Console.WriteLine($"{result.Position,2}. {InitialData.Drivers.Single(d => d.Id == result.DriverId).Name,-22}" +
                $" {result.ElapsedMs,8} ms | pneus {result.TireWear,3}% | {result.Points,2} pts");
        Console.WriteLine($"Eventos explicativos: {state.Events.Count}. Use --json para consultar todos.");
    }
    return 0;
}
catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException ||
    ex is IOException || ex is UnauthorizedAccessException || ex is JsonException ||
    ex is InvalidOperationException || ex is InvalidDataException || ex is System.Text.DecoderFallbackException ||
    ex is OperationCanceledException)
{
    var message = ex switch {
        JsonException => "JSON inválido ou campos não reconhecidos.",
        InvalidDataException => ex.Message,
        FileNotFoundException => "Arquivo não encontrado. Confira o caminho informado.",
        DirectoryNotFoundException => "Diretório não encontrado. Confira o caminho informado.",
        UnauthorizedAccessException => "Sem permissão para acessar ou gravar nesse caminho.",
        System.Text.DecoderFallbackException => "O arquivo não contém texto UTF-8 válido.",
        IOException => "Não foi possível acessar ou gravar o arquivo. Confira o caminho, as permissões, " +
            "se o destino já existe ou se há outra instância em uso. Nenhum sucesso foi confirmado.",
        ArgumentOutOfRangeException => "Valor fora dos limites permitidos.",
        FormatException => "Valor numérico ou formato inválido.",
        OverflowException => "Valor numérico excede o limite permitido.",
        _ => ex.Message
    };
    Console.Error.WriteLine("Erro: " + message + " Consulte --ajuda.");
    return 1;
}

static Pace ParsePace(string text) => text switch {
    "conservar" => Pace.Conservar, "equilibrado" => Pace.Equilibrado, "atacar" => Pace.Atacar,
    _ => throw new ArgumentException("Ritmo inválido.")
};

internal sealed class ReplayCommand
{
    public int Setor { get; set; }
    public string? Piloto { get; set; }
    public string? Ritmo { get; set; }
}
