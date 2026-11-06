using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stintoria.Core;

namespace Stintoria.Local;

/// <summary>Formato local verificável por replay. Não é assinatura/autenticação contra fraude deliberada.</summary>
public static class ChampionshipArchive
{
    public const int MaxBytes = 8 * 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static string Encode(Championship championship)
    {
        var payload = new Payload
        {
            Championship = Championship.Version, Simulation = RaceState.SimulationVersion,
            Random = SeededRandom.Version, SeedAlgorithm = Championship.SeedVersion, Data = Championship.DataVersion,
            Seed = championship.Seed,
            Rounds = championship.Rounds.Select(r => new RoundData
            {
                Number = r.Number,
                Decisions = r.Decisions.Select(d => new DecisionData
                    { Sector = d.Sector, DriverId = d.DriverId, Pace = d.Pace }).ToArray(),
                Result = JsonSerializer.SerializeToElement(r.Result, Json)
            }).ToArray()
        };
        var text = JsonSerializer.Serialize(payload, Json);
        return JsonSerializer.Serialize(new Envelope { Format = "stintoria-save-v1", Payload = text, Sha256 = Hash(text) }, Json);
    }

    public static Championship Decode(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw Invalid();
        try
        {
            RejectDuplicates(text);
            var envelope = JsonSerializer.Deserialize<Envelope>(text, Json) ?? throw Invalid();
            if (envelope.Format != "stintoria-save-v1" || envelope.Payload == null ||
                envelope.Sha256 != Hash(envelope.Payload)) throw Invalid();
            RejectDuplicates(envelope.Payload);
            var payload = JsonSerializer.Deserialize<Payload>(envelope.Payload, Json) ?? throw Invalid();
            if (payload.Championship != Championship.Version || payload.Simulation != RaceState.SimulationVersion ||
                payload.Random != SeededRandom.Version || payload.SeedAlgorithm != Championship.SeedVersion ||
                payload.Data != Championship.DataVersion || payload.Rounds == null || payload.Rounds.Length > 8)
                throw Invalid();
            var championship = new Championship(payload.Seed);
            foreach (var round in payload.Rounds)
            {
                if (round == null || round.Decisions == null || round.Decisions.Length > 720)
                    throw Invalid();
                var decisions = round.Decisions.Select(d => d == null ? throw Invalid() :
                    new SectorDecision(d.Sector, d.DriverId ?? "", d.Pace)).ToArray();
                var replay = championship.RunNext(round.Number, decisions);
                // O histórico salvo é evidência, nunca a fonte dos pontos. Confere TODOS os campos/eventos.
                if (!Equivalent(JsonSerializer.SerializeToElement(replay.Result, Json), round.Result))
                    throw Invalid();
            }
            return championship;
        }
        catch (Exception ex) when (ex is JsonException || ex is ArgumentException ||
            ex is InvalidOperationException || ex is OverflowException)
        { throw Invalid(); }
    }

    private static InvalidDataException Invalid() =>
        new("Salvamento inválido, corrompido ou incompatível. Nenhum resultado foi carregado.");

    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static void RejectDuplicates(string text)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64 });
        Visit(doc.RootElement);
        static void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw Invalid();
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Visit(item);
        }
    }

    internal static bool Equivalent(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        if (a.ValueKind == JsonValueKind.Object)
        {
            var left = a.EnumerateObject().ToArray(); var right = b.EnumerateObject().ToArray();
            return left.Length == right.Length && left.All(p => b.TryGetProperty(p.Name, out var v) && Equivalent(p.Value, v));
        }
        if (a.ValueKind == JsonValueKind.Array)
        {
            var left = a.EnumerateArray().ToArray(); var right = b.EnumerateArray().ToArray();
            return left.Length == right.Length && left.Zip(right).All(pair => Equivalent(pair.First, pair.Second));
        }
        return a.GetRawText() == b.GetRawText();
    }

    private sealed class Envelope
    {
        public required string Format { get; init; }
        public required string Payload { get; init; }
        public required string Sha256 { get; init; }
        public Envelope() { }
    }
    private sealed class Payload
    {
        public required string Championship { get; init; }
        public required string Simulation { get; init; }
        public required string Random { get; init; }
        public required string SeedAlgorithm { get; init; }
        public required string Data { get; init; }
        public required uint Seed { get; init; }
        public required RoundData[] Rounds { get; init; }
    }
    private sealed class RoundData
    {
        public required int Number { get; init; }
        public required DecisionData[] Decisions { get; init; }
        public required JsonElement Result { get; init; }
    }
    private sealed class DecisionData
    {
        public required int Sector { get; init; }
        public required string DriverId { get; init; }
        public required Pace Pace { get; init; }
    }
}
