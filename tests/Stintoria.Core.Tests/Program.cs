using System.Text.Json;
using Stintoria.Core;

if (args.Length == 1 && args[0] == "--stintoria-browser-proof")
    return BrowserProofScenario.RunCli();
if (args.Length == 1 && args[0] == "--stintoria-browser-race-command")
    return BrowserProofScenario.RunRaceCommandCli();
if (args.Length == 1 && args[0] == "--stintoria-browser-career-command")
    return BrowserCareerScenario.RunCli();
if (args.Length == 1 && args[0] == "--stintoria-browser-career-stage-one-fixture")
    return BrowserCareerScenario.RunFirstRoundFixtureCli();

// Executor mínimo: sem NuGet, rede ou framework externo; cada caso falha com exit code 1.
var tests = new List<(string Name, Action Body)> {
    ("RNG v1: vetor de referência", () => {
        var rng = new SeededRandom(42);
        Check(rng.NextUInt() == 1083814273u);
        Check(rng.NextUInt() == 378494188u);
        Check(rng.NextUInt() == 2479403867u);
    }),
    ("RNG: limite inválido rejeitado", () => Throws<ArgumentOutOfRangeException>(() => new SeededRandom(0).Next(0))),
    ("Dados: 20 pilotos, 10 equipes, vínculos válidos", () => KartRules.ValidateGrid(InitialData.Drivers, InitialData.Teams)),
    ("Dados: 3 pistas e 8 etapas referenciadas", () => {
        Check(InitialData.Circuits.Count == 3 && InitialData.KartCalendar.Count == 8);
        Check(InitialData.KartCalendar.Select(x => x.Number).SequenceEqual(Enumerable.Range(1, 8)));
        Check(InitialData.KartCalendar.All(r => InitialData.Circuits.Any(c => c.Id == r.CircuitId)));
    }),
    ("Categorias: contagens e motores implementados coerentes", () => {
        Check(InitialData.Categories.Select(c => c.Rounds).SequenceEqual(new[] {8, 10, 12, 14, 16, 20}));
        Check(InitialData.Categories.Select(c => c.Level).SequenceEqual(new[] {
            CareerLevel.Nacional, CareerLevel.Nacional, CareerLevel.Continental,
            CareerLevel.Internacional, CareerLevel.Internacional, CareerLevel.Mundial}));
        Check(InitialData.Categories.Count(c => c.Implemented) == 2);
        Check(InitialData.Categories.Single(c => c.Id == "kart").Implemented);
        Check(InitialData.Categories.Single(c => c.Id == "f4").Implemented);
        Check(InitialData.Categories.Where(c => c.Id != "kart" && c.Id != "f4")
            .All(c => !c.Implemented));
    }),
    ("Contratos: habilidade e texto inválidos rejeitados", () => {
        Throws<ArgumentOutOfRangeException>(() => new Driver("p", "Nome", "t", 101));
        Throws<ArgumentException>(() => new Team("", "Nome"));
        Throws<ArgumentOutOfRangeException>(() => new Category("a", "A", 0, CareerLevel.Nacional));
    }),
    ("Pista: setores inválidos rejeitados e cópia defensiva", () => {
        Throws<ArgumentException>(() => new Circuit("a", "A", 1000, 1000));
        Throws<ArgumentException>(() => new Circuit("a", "A", 0, 1000, 1000));
        var input = new[] {1000, 2000, 3000}; var circuit = new Circuit("a", "A", input);
        input[0] = 1; Check(circuit.SectorBaseMs[0] == 1000);
    }),
    ("Grid: pilotos duplicados rejeitados", () => {
        var drivers = InitialData.Drivers.ToArray(); drivers[1] = drivers[0];
        Throws<ArgumentException>(() => KartRules.ValidateGrid(drivers, InitialData.Teams));
    }),
    ("Grid: vínculo de equipe desconhecido rejeitado", () => {
        var drivers = InitialData.Drivers.ToArray(); drivers[0] = new Driver("p01", "Nome", "inexistente", 80);
        Throws<ArgumentException>(() => KartRules.ValidateGrid(drivers, InitialData.Teams));
    }),
    ("Grid: dois pilotos por equipe obrigatório", () => {
        var drivers = InitialData.Drivers.ToArray(); drivers[0] = new Driver("p01", "Nome", "t02", 80);
        Throws<ArgumentException>(() => KartRules.ValidateGrid(drivers, InitialData.Teams));
    }),
    ("Configuração: voltas e clima inválidos rejeitados", () => {
        Throws<ArgumentOutOfRangeException>(() => NewRace(laps: 0));
        Throws<ArgumentOutOfRangeException>(() => NewRace(weather: (Weather)99));
    }),
    ("Estado inicial: sem pontos, tempo ou desgaste", () => {
        var s = NewRace().State;
        Check(s.CompletedSectors == 0 && !s.Finished && s.Events.Count == 0);
        Check(s.Classification.All(d => d.ElapsedMs == 0 && d.TireWear == 0 && d.Points == 0));
    }),
    ("Passo: exatamente um setor e snapshot imutável", () => {
        var race = NewRace(); var before = race.State; var after = race.Advance();
        Check(before.CompletedSectors == 0 && before.Events.Count == 0);
        Check(before.Classification.All(d => d.ElapsedMs == 0));
        Check(after.CompletedSectors == 1 && after.Classification.All(d => d.ElapsedMs > 0));
    }),
    ("Comandos inválidos são atômicos, sem avançar RNG", () => {
        var race = NewRace();
        Throws<ArgumentException>(() => race.Advance(new Dictionary<string, Pace> {
            ["p01"] = Pace.Atacar, ["p02"] = (Pace)99 }));
        Throws<ArgumentException>(() => race.Advance(new Dictionary<string, Pace> { ["desconhecido"] = Pace.Atacar }));
        Check(Signature(race.Advance()) == Signature(NewRace().Advance()));
    }),
    ("Ritmo persiste até próximo comando", () => {
        var race = NewRace(); race.Advance(new Dictionary<string, Pace> {["p01"] = Pace.Conservar});
        Check(race.Advance().Classification.Single(d => d.DriverId == "p01").Pace == Pace.Conservar);
    }),
    ("Ritmos: desgaste conservar < equilibrado < atacar", () => {
        int Wear(Pace pace) => Finish(NewRace(laps: 1), pace).Classification.Single(d => d.DriverId == "p01").TireWear;
        Check(Wear(Pace.Conservar) == 3 && Wear(Pace.Equilibrado) == 6 && Wear(Pace.Atacar) == 9);
    }),
    ("Ritmos: atacar é mais rápido sem erro, mas desgasta mais", () => {
        // Mesmo RNG por piloto e setor. Só compara uma seed sem erro para p01.
        var conserve = NewRace().Advance(new Dictionary<string, Pace> {["p01"] = Pace.Conservar});
        var attack = NewRace().Advance(new Dictionary<string, Pace> {["p01"] = Pace.Atacar});
        Check(!attack.Events.Any(e => e.DriverId == "p01" && e.Code == "erro"));
        var a = attack.Classification.Single(d => d.DriverId == "p01");
        var c = conserve.Classification.Single(d => d.DriverId == "p01");
        Check(c.ElapsedMs - a.ElapsedMs == 650 && a.TireWear > c.TireWear);
    }),
    ("Chuva: penaliza tempo com mesma seed", () => {
        var dry = NewRace(seed: 42).Advance(); var wet = NewRace(seed: 42, weather: Weather.Chuva).Advance();
        Check(dry.Weather == Weather.Seco && wet.Weather == Weather.Chuva);
        Check(wet.Classification.Single(d => d.DriverId == "p01").ElapsedMs >
            dry.Classification.Single(d => d.DriverId == "p01").ElapsedMs);
    }),
    ("Determinismo: mesma seed e roteiro, inclusive eventos", () => {
        Check(Signature(Replay(123)) == Signature(Replay(123)));
        Check(Signature(Replay(123)) != Signature(Replay(124)));
    }),
    ("Determinismo: ordem do grid não altera resultado", () => {
        var reversed = new RaceSimulation(InitialData.Drivers.Reverse().ToArray(),
            InitialData.Teams.Reverse().ToArray(), InitialData.Circuits[0], 42);
        Check(Signature(Finish(reversed)) == Signature(Finish(NewRace())));
    }),
    ("Classificação: desempate ordinal estável", () => {
        Check(NewRace().State.Classification.Select(x => x.DriverId)
            .SequenceEqual(InitialData.Drivers.Select(x => x.Id).Order(StringComparer.Ordinal)));
    }),
    ("Final: 36 setores, pontos só na bandeirada, avanço proibido", () => {
        var race = NewRace();
        for (var i = 0; i < 35; i++) Check(race.Advance().Classification.All(x => x.Points == 0));
        var s = race.Advance();
        Check(s.Finished && s.CompletedLaps == 12 && s.CompletedSectors == 36);
        Check(s.Classification.Select(x => x.Points).SequenceEqual(new[] {25,18,15,12,10,8,6,4,2,1,0,0,0,0,0,0,0,0,0,0}));
        Throws<InvalidOperationException>(() => race.Advance());
    }),
    ("Pontos: posição inválida rejeitada", () => Throws<ArgumentOutOfRangeException>(() => KartRules.PointsFor(0))),
    ("Desgaste saturado em 100, sem estouro", () => {
        Check(Finish(NewRace(laps: 100), Pace.Atacar).Classification.Single(x => x.DriverId == "p01").TireWear == 100);
    }),
    ("Invariantes em 32 seeds e 3 pistas: tempos, ranking, eventos", () => {
        var codes = new HashSet<string>();
        foreach (var circuit in InitialData.Circuits)
        for (uint seed = 0; seed < 32; seed++) {
            var race = new RaceSimulation(InitialData.Drivers, InitialData.Teams, circuit, seed);
            var previous = race.State;
            while (!previous.Finished) {
                var next = race.Advance(new Dictionary<string, Pace> {["p01"] = Pace.Atacar});
                Check(next.Classification.Count == 20);
                Check(next.Classification.Select(d => d.Position).SequenceEqual(Enumerable.Range(1, 20)));
                Check(next.Classification.Select(d => d.ElapsedMs).SequenceEqual(next.Classification.Select(d => d.ElapsedMs).Order()));
                foreach (var d in next.Classification) {
                    Check(d.ElapsedMs > previous.Classification.Single(p => p.DriverId == d.DriverId).ElapsedMs);
                    Check(d.TireWear >= 0 && d.TireWear <= 100);
                }
                previous = next;
            }
            Check(previous.Events.Count(e => e.Code == "setor") == 720);
            foreach (var e in previous.Events) {
                Check(e.Step >= 1 && e.Step <= 36 && !string.IsNullOrWhiteSpace(e.Explanation));
                codes.Add(e.Code);
            }
        }
        Check(new[] {"setor", "clima", "ritmo", "erro", "posicao", "fim"}.All(codes.Contains));
    })
};

tests.AddRange(ChampionshipTests.Cases());
tests.AddRange(CareerTests.Cases());
tests.AddRange(HistoryTests.Cases());
tests.AddRange(StrategyTests.Cases());
tests.AddRange(SegmentReportTests.Cases());
tests.AddRange(AiTacticsTests.Cases());
tests.AddRange(ClientBridgeTests.Cases());
tests.AddRange(UnityClientFlowTests.Cases());
tests.AddRange(CareerMigrationTests.Cases());
tests.AddRange(ParticipatoryTests.Cases());
tests.AddRange(UnityParticipatoryTests.Cases());
tests.AddRange(BrowserPersistenceTests.Cases());
tests.AddRange(BrowserProofScenario.RaceFlowCases());
tests.AddRange(BrowserCareerScenario.TestCases());
tests.AddRange(CareerProgressionTests.Cases());
tests.AddRange(Formula4Tests.Cases());
tests.AddRange(RaceProgressTests.Cases());
tests.AddRange(SectorTimingTests.Cases());
if (args.Length == 2 && args[0] == "--stintoria-test-filter")
{
    var filter = args[1];
    tests = tests.Where(test => test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    if (tests.Count == 0)
    {
        Console.Error.WriteLine("Nenhum teste corresponde ao filtro informado.");
        return 2;
    }
}
tests.Add(("Prova local: ClientBridge corresponde ao núcleo após um setor e checkpoint",
    () => {
        var proof = BrowserProofScenario.Execute();
        Check(proof.Seed == 42 && proof.Command == "Setor 1: Atacar");
        Check(proof.CompletedSectors == 1 && !proof.Finished);
        Check(proof.Classification.Count == 20);
        Check(proof.Classification.Select(driver => driver.DriverId).Distinct().Count() == 20);
        Check(proof.Classification.Select(driver => driver.TeamId).Distinct().Count() == 10);
        Check(proof.Classification.Count(driver => driver.IsProtagonist) == 1);
        Check(proof.Classification.GroupBy(driver => driver.TeamId).All(group => group.Count() == 2));
        Check(proof.Classification.Select(driver => driver.Position)
            .SequenceEqual(Enumerable.Range(1, 20)));
        Check(proof.Events.Count > 0 && proof.EventCount == proof.Events.Count);
        Check(proof.Classification.All(driver => driver.Points == 0));
        Check(proof.ParityVerified && proof.CheckpointRoundTripVerified);
        Check(proof.ReferenceSha256 == proof.BridgeSha256);
        Check(proof.BridgeSha256 == proof.ResumedSha256);
    }));
var failures = 0;
foreach (var test in tests)
{
    try { test.Body(); Console.WriteLine("OK    " + test.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FALHA " + test.Name + ": " + ex.Message); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} testes aprovados.");
return failures == 0 ? 0 : 1;

static void Check(bool condition)
{ if (!condition) throw new Exception("Invariante não atendida."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Esperava " + typeof(T).Name);
}
static RaceSimulation NewRace(uint seed = 42, int laps = 12, Weather weather = Weather.Seco) =>
    new(InitialData.Drivers, InitialData.Teams, InitialData.Circuits[0], seed, laps, weather);
static RaceState Finish(RaceSimulation race, Pace pace = Pace.Equilibrado)
{
    while (!race.State.Finished) race.Advance(new Dictionary<string, Pace> { ["p01"] = pace });
    return race.State;
}
static string Signature(RaceState state) => JsonSerializer.Serialize(state);
static RaceState Replay(uint seed)
{
    var race = NewRace(seed);
    for (var i = 1; i <= 36; i++)
        race.Advance(new Dictionary<string, Pace> {["p01"] = i <= 12 ? Pace.Conservar : i <= 27 ? Pace.Equilibrado : Pace.Atacar});
    return race.State;
}
