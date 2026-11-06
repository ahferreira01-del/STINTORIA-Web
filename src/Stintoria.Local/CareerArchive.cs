using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stintoria.Core;

namespace Stintoria.Local;

public static class CareerArchive
{
    public static string Encode(CareerSession career)
    {
        var payload = new Payload
        {
            Version = CareerSession.Version, Championship = Championship.Version,
            Simulation = RaceState.SimulationVersion, Random = SeededRandom.Version,
            Data = Championship.DataVersion, SeedAlgorithm = Championship.SeedVersion,
            Seed = career.Seed,
            Profile = new ProfileData { Name = career.Profile.Name, Nationality = career.Profile.Nationality,
                Origin = career.Profile.Origin, TeamId = career.Profile.TeamId },
            Rounds = career.Rounds.Select(r => ChoiceFor(r.Preparation, r.GetLiveDecisions())).ToArray(),
            Pending = career.Pending == null ? null : ChoiceFor(career.Pending),
            Active = ActiveFor(career),
            Snapshot = SnapshotFor(career)
        };
        var text = JsonSerializer.Serialize(payload, ChampionshipArchive.Json);
        var hasStrategy = payload.Rounds.Any(r => r.Strategy != null) || payload.Pending?.Strategy != null;
        var hasTactics = payload.Rounds.Any(r => r.Tactics != null) || payload.Pending?.Tactics != null;
        var live = payload.Active != null || payload.Rounds.Any(r => r.Live != null);
        return JsonSerializer.Serialize(new Envelope { Format = live ? "stintoria-carreira-v4" : hasTactics ? "stintoria-carreira-v3" :
            hasStrategy ? "stintoria-carreira-v2" : "stintoria-carreira-v1", Payload = text,
            Sha256 = ChampionshipArchive.Hash(text) }, ChampionshipArchive.Json);
    }

    public static CareerSession Decode(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > ChampionshipArchive.MaxBytes) throw Invalid();
        try
        {
            ChampionshipArchive.RejectDuplicates(text);
            var envelope = JsonSerializer.Deserialize<Envelope>(text, ChampionshipArchive.Json) ?? throw Invalid();
            if ((envelope.Format != "stintoria-carreira-v1" && envelope.Format != "stintoria-carreira-v2" &&
                envelope.Format != "stintoria-carreira-v3" && envelope.Format != "stintoria-carreira-v4") || envelope.Payload == null ||
                envelope.Sha256 != ChampionshipArchive.Hash(envelope.Payload)) throw Invalid();
            ChampionshipArchive.RejectDuplicates(envelope.Payload);
            var p = JsonSerializer.Deserialize<Payload>(envelope.Payload, ChampionshipArchive.Json) ?? throw Invalid();
            if (p.Version != CareerSession.Version || p.Championship != Championship.Version ||
                p.Simulation != RaceState.SimulationVersion || p.Random != SeededRandom.Version ||
                p.Data != Championship.DataVersion || p.SeedAlgorithm != Championship.SeedVersion ||
                p.Profile == null || p.Rounds == null || p.Rounds.Length > 8) throw Invalid();
            var hasStrategy = p.Rounds.Any(r => r?.Strategy != null) || p.Pending?.Strategy != null;
            var hasTactics = p.Rounds.Any(r => r?.Tactics != null) || p.Pending?.Tactics != null;
            var live = p.Active != null || p.Rounds.Any(r => r?.Live != null);
            var expectedFormat = live ? "stintoria-carreira-v4" : hasTactics ? "stintoria-carreira-v3" : hasStrategy ? "stintoria-carreira-v2" : "stintoria-carreira-v1";
            if (envelope.Format != expectedFormat) throw Invalid();
            var profile = new CareerProfile(p.Profile.Name, p.Profile.Nationality, p.Profile.Origin, p.Profile.TeamId);
            var career = new CareerSession(p.Seed, profile);
            foreach (var choice in p.Rounds)
            {
                if (choice == null) throw Invalid();
                Apply(career, choice);
                if (choice.Live == null) career.RunNext(choice.Number);
                else Replay(career, 36, choice.Live);
            }
            if (p.Pending != null) {
                if (p.Pending.Live != null) throw Invalid();
                Apply(career, p.Pending);
            }
            if (p.Active != null) {
                if (p.Active.Sector < 0 || p.Active.Sector >= 36) throw Invalid();
                Replay(career, p.Active.Sector, p.Active.Decisions);
                if (career.GetActiveRace()!.RandomState != p.Active.RandomState) throw Invalid();
            }
            if (!ChampionshipArchive.Equivalent(SnapshotFor(career), p.Snapshot))
                throw Invalid();
            return career;
        }
        catch (Exception ex) when (ex is JsonException || ex is ArgumentException ||
            ex is InvalidOperationException || ex is OverflowException)
        { throw Invalid(); }
    }

    private static InvalidDataException Invalid() =>
        new("Carreira inválida, corrompida ou incompatível. Nenhum progresso foi carregado.");

    private static JsonElement SnapshotFor(CareerSession career)
    {
        if (career.GetActiveRace() != null || career.Rounds.Any(r => r.GetLiveDecisions() != null))
            return JsonSerializer.SerializeToElement(new { Career = career,
                Choices = career.Rounds.Select(r => ChoiceFor(r.Preparation, r.GetLiveDecisions())).ToArray(),
                Active = ActiveFor(career), State = career.GetActiveRace()?.State }, ChampionshipArchive.Json);
        var choices = career.Rounds.Select(r => ChoiceFor(r.Preparation)).ToArray();
        var pending = career.Pending == null ? null : ChoiceFor(career.Pending);
        if (!choices.Any(c => c.Strategy != null) && pending?.Strategy == null)
            return JsonSerializer.SerializeToElement(career, ChampionshipArchive.Json);
        // v2 inclui os planos no snapshot conferido pelo replay, inclusive antes da bandeirada.
        return JsonSerializer.SerializeToElement(new { Career = career, Choices = choices, Pending = pending },
            ChampionshipArchive.Json);
    }

    private static ActiveData? ActiveFor(CareerSession c)
    {
        var race = c.GetActiveRace();
        return race == null ? null : new ActiveData { Sector = race.State.CompletedSectors,
            RandomState = race.RandomState, Decisions = race.Decisions.Select(d => new LiveData { Sector = d.Sector, Pace = d.Pace }).ToArray() };
    }

    private static void Replay(CareerSession career, int sectors, LiveData[] choices)
    {
        if (choices == null || choices.Length > 36 ||
            choices.Any(d => d == null || d.Sector < 1 || d.Sector > sectors) ||
            choices.Select(d => d.Sector).Distinct().Count() != choices.Length ||
            !choices.Select(d => d.Sector).SequenceEqual(choices.Select(d => d.Sector).OrderBy(x => x))) throw Invalid();
        career.StartRace(career.NextRound);
        for (var sector = 1; sector <= sectors; sector++) {
            var decision = choices.SingleOrDefault(d => d.Sector == sector);
            career.AdvanceRace(career.NextRound, sector, 1, decision?.Pace);
        }
    }

    private static Choice ChoiceFor(CareerPreparation p, IReadOnlyList<SectorDecision>? live = null)
    {
        var strategy = p.GetStrategy();
        return new Choice { Number = p.Round, Training = p.Training, Pace = p.Pace,
            Strategy = strategy == null ? null : new StrategyData {
                Version = RaceStrategy.Version, MiddleStartSector = strategy.MiddleStartSector,
                FinalStartSector = strategy.FinalStartSector, Beginning = strategy.Beginning,
                Middle = strategy.Middle, Final = strategy.Final },
            Live = live?.Select(d => new LiveData { Sector = d.Sector, Pace = d.Pace }).ToArray(),
            Tactics = p.GetOpponentPlans().Count == 0 ? null : new TacticsData {
                Version = AiTactics.Version,
                Plans = JsonSerializer.SerializeToElement(p.GetOpponentPlans(), ChampionshipArchive.Json) } };
    }

    private static void Apply(CareerSession career, Choice choice)
    {
        if (choice.Strategy == null) {
            if (choice.Tactics != null) throw Invalid();
            career.Prepare(choice.Number, choice.Training, choice.Pace); return;
        }
        var plan = choice.Strategy;
        if (plan.Version != RaceStrategy.Version || choice.Pace != plan.Beginning) throw Invalid();
        var strategy = new RaceStrategy(plan.Beginning, plan.Middle, plan.Final, plan.MiddleStartSector, plan.FinalStartSector);
        if (choice.Tactics == null) career.PrepareStrategy(choice.Number, choice.Training, strategy);
        else {
            if (choice.Tactics.Version != AiTactics.Version) throw Invalid();
            var preparation = career.PrepareTactical(choice.Number, choice.Training, strategy);
            if (!ChampionshipArchive.Equivalent(
                JsonSerializer.SerializeToElement(preparation.GetOpponentPlans(), ChampionshipArchive.Json), choice.Tactics.Plans))
                throw Invalid();
        }
    }

    private sealed class Envelope
    {
        public required string Format { get; init; }
        public required string Payload { get; init; }
        public required string Sha256 { get; init; }
    }
    private sealed class Payload
    {
        public required string Version { get; init; }
        public required string Championship { get; init; }
        public required string Simulation { get; init; }
        public required string Random { get; init; }
        public required string Data { get; init; }
        public required string SeedAlgorithm { get; init; }
        public required uint Seed { get; init; }
        public required ProfileData Profile { get; init; }
        public required Choice[] Rounds { get; init; }
        public required Choice? Pending { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ActiveData? Active { get; init; }
        public required JsonElement Snapshot { get; init; }
    }
    private sealed class ProfileData
    {
        public required string Name { get; init; }
        public required string Nationality { get; init; }
        public required CareerOrigin Origin { get; init; }
        public required string TeamId { get; init; }
    }
    private sealed class Choice
    {
        public required int Number { get; init; }
        public required Training Training { get; init; }
        public required Pace Pace { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public StrategyData? Strategy { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TacticsData? Tactics { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LiveData[]? Live { get; init; }
    }
    private sealed class LiveData
    {
        public required int Sector { get; init; }
        public required Pace Pace { get; init; }
    }
    private sealed class ActiveData
    {
        public required int Sector { get; init; }
        public required uint RandomState { get; init; }
        public required LiveData[] Decisions { get; init; }
    }
    private sealed class TacticsData
    {
        public required string Version { get; init; }
        public required JsonElement Plans { get; init; }
    }
    private sealed class StrategyData
    {
        public required string Version { get; init; }
        public required int MiddleStartSector { get; init; }
        public required int FinalStartSector { get; init; }
        public required Pace Beginning { get; init; }
        public required Pace Middle { get; init; }
        public required Pace Final { get; init; }
    }
}
