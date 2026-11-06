using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stintoria.Core;
using Stintoria.Local;

internal static class ChampionshipTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[] {
        ("Campeonato vazio: tabelas completas, zeradas e ordenadas", () => {
            var c = new Championship(0);
            Check(c.Rounds.Count == 0 && c.NextRound == 1 && !c.Finished);
            Check(c.DriverStandings.Count == 20 && c.TeamStandings.Count == 10);
            Check(c.DriverStandings.All(r => r.Points == 0 && r.Wins == 0));
            Check(c.DriverStandings.Select(r => r.Id).SequenceEqual(InitialData.Drivers.Select(r => r.Id)));
        }),
        ("Campeonato completo: oito etapas, 808 pontos para pilotos e equipes", () => {
            var c = Complete(42);
            Check(c.Finished && c.Rounds.Count == 8);
            Check(c.Rounds.Select(r => r.Number).SequenceEqual(Enumerable.Range(1, 8)));
            Check(c.DriverStandings.Sum(r => r.Points) == 808);
            Check(c.TeamStandings.Sum(r => r.Points) == 808);
            Check(c.DriverStandings.Sum(r => r.Wins) == 8 && c.TeamStandings.Sum(r => r.Wins) == 8);
            foreach (var round in c.Rounds) {
                Check(round.Result.Finished && round.Result.Classification.Sum(r => r.Points) == 101);
                Check(round.Result.CircuitId == InitialData.KartCalendar[round.Number - 1].CircuitId);
                Check(round.Result.Events.Count(e => e.Code == "fim") == 1);
                Check(round.Result.Events.Count(e => e.Code == "setor") == 720);
            }
            foreach (var team in c.TeamStandings) {
                var members = InitialData.Drivers.Where(d => d.TeamId == team.Id).Select(d => d.Id).ToHashSet();
                Check(team.Points == c.DriverStandings.Where(d => members.Contains(d.Id)).Sum(d => d.Points));
                Check(team.Finishes.Sum() == 16);
            }
        }),
        ("Campeonato: seed zero e limite uint sem estouro", () => {
            foreach (var seed in new[] { 0u, uint.MaxValue }) {
                var c = Complete(seed);
                Check(c.SeedFor(1) == seed);
                Check(c.SeedFor(2) == unchecked(seed + 2654435761u));
                Check(c.Rounds.Select(r => r.Result.Seed).Distinct().Count() == 8);
                Throws<ArgumentOutOfRangeException>(() => c.SeedFor(0));
                Throws<ArgumentOutOfRangeException>(() => c.SeedFor(9));
            }
        }),
        ("Campeonato: etapa duplicada, salto e corrida após fim rejeitados", () => {
            var c = new Championship(42);
            Throws<InvalidOperationException>(() => c.RunNext(0));
            Throws<InvalidOperationException>(() => c.RunNext(2));
            Check(c.Rounds.Count == 0);
            c.RunNext(1);
            var before = ChampionshipArchive.Encode(c);
            Throws<InvalidOperationException>(() => c.RunNext(1));
            Check(before == ChampionshipArchive.Encode(c));
            while (!c.Finished) c.RunNext(c.NextRound);
            Throws<InvalidOperationException>(() => c.RunNext(9));
        }),
        ("Campeonato: decisões inválidas e duplicadas sem commit parcial", () => {
            var c = new Championship(1);
            var initial = ChampionshipArchive.Encode(c);
            var duplicate = new SectorDecision(1, "p01", Pace.Atacar);
            Throws<ArgumentException>(() => c.RunNext(1, new[] { duplicate, duplicate }));
            Throws<ArgumentException>(() => c.RunNext(1, new SectorDecision[] { null! }));
            Throws<ArgumentOutOfRangeException>(() => new SectorDecision(37, "p01", Pace.Atacar));
            Throws<ArgumentException>(() => new SectorDecision(1, "p99", Pace.Atacar));
            Throws<ArgumentException>(() => new SectorDecision(1, "p01", (Pace)9));
            Check(initial == ChampionshipArchive.Encode(c));
        }),
        ("Campeonato: resultado anterior e decisões não mudam por referência", () => {
            var c = new Championship(42);
            var input = new[] { new SectorDecision(1, "p01", Pace.Conservar) };
            var round = c.RunNext(1, input);
            var result = JsonSerializer.Serialize(round);
            input[0] = new SectorDecision(1, "p01", Pace.Atacar);
            c.RunNext(2);
            Check(result == JsonSerializer.Serialize(round));
            Throws<NotSupportedException>(() =>
                ((IList<DriverResult>)round.Result.Classification)[0] = round.Result.Classification[1]);
            Throws<NotSupportedException>(() => ((IList<int>)c.DriverStandings[0].Finishes)[0] = 999);
        }),
        ("Campeonato: ordem das decisões não altera replay/arquivo", () => {
            var a = new Championship(42); var b = new Championship(42);
            var commands = Decisions(1);
            a.RunNext(1, commands); b.RunNext(1, commands.Reverse());
            Check(ChampionshipArchive.Encode(a) == ChampionshipArchive.Encode(b));
        }),
        ("Desempates: pontos, vitórias, posições inferiores, recência e ID", () => {
            ChampionshipStanding Row(string id, int points, int place, int[] recent) {
                var counts = new int[20]; counts[place - 1] = 1;
                return new ChampionshipStanding(id, points, counts, recent);
            }
            Check(ChampionshipStandings.Compare(Row("b", 11, 20, new[] {20}), Row("a", 10, 1, new[] {1})) < 0);
            Check(ChampionshipStandings.Compare(Row("b", 10, 1, new[] {1}), Row("a", 10, 2, new[] {2})) < 0);
            Check(ChampionshipStandings.Compare(Row("b", 0, 19, new[] {19}), Row("a", 0, 20, new[] {20})) < 0);
            var counts = new int[20]; counts[0] = 1; counts[1] = 1;
            Check(ChampionshipStandings.Compare(new("b", 43, counts, new[] {1,2}),
                new("a", 43, counts, new[] {2,1})) < 0);
            Check(ChampionshipStandings.Compare(Row("a", 10, 1, new[] {1}), Row("b", 10, 1, new[] {1})) < 0);
            Check(ChampionshipStandings.Compare(Row("a", 10, 1, new[] {1}), Row("a", 10, 1, new[] {1})) == 0);
        }),
        ("Desempate de equipes: melhor e segundo piloto na etapa recente", () => {
            var counts = new int[20];
            Check(ChampionshipStandings.Compare(new("b", 10, counts, new[] {1, 4, 2, 3}),
                new("a", 10, counts, new[] {1, 5, 2, 3})) < 0);
            var c = Complete(11);
            foreach (var team in c.TeamStandings) {
                var members = InitialData.Drivers.Where(d => d.TeamId == team.Id).Select(d => d.Id).ToHashSet();
                Check(team.RecentPositions.Take(2).SequenceEqual(c.Rounds.Last().Result.Classification
                    .Where(d => members.Contains(d.DriverId)).Select(d => d.Position).Order()));
            }
        }),
        ("Retomada em todas as fronteiras 0..8: arquivo final idêntico", () => {
            var expected = ChampionshipArchive.Encode(Complete(77));
            for (var split = 0; split <= 8; split++) {
                var c = new Championship(77);
                for (var r = 1; r <= split; r++) c.RunNext(r, Decisions(r));
                c = ChampionshipArchive.Decode(ChampionshipArchive.Encode(c));
                while (!c.Finished) c.RunNext(c.NextRound, Decisions(c.NextRound));
                Check(expected == ChampionshipArchive.Encode(c));
            }
        }),
        ("Arquivo: determinismo e seeds diferentes", () => {
            Check(ChampionshipArchive.Encode(Complete(19)) == ChampionshipArchive.Encode(Complete(19)));
            Check(ChampionshipArchive.Encode(Complete(19)) != ChampionshipArchive.Encode(Complete(20)));
        }),
        ("Integridade: truncamento, checksum e campos duplicados rejeitados", () => {
            var file = ChampionshipArchive.Encode(new Championship(1));
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(file[..^3]));
            var envelope = JsonNode.Parse(file)!.AsObject();
            envelope["Sha256"] = "0000";
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(envelope.ToJsonString()));
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(file.Replace(
                "\"Format\":", "\"Format\":\"stintoria-save-v1\",\"Format\":", StringComparison.Ordinal)));
            var duplicatePayload = Rewrite(file, p => p["Seed"] = 4u);
            var root = JsonNode.Parse(duplicatePayload)!.AsObject();
            var payload = root["Payload"]!.GetValue<string>().Replace("\"Seed\":4", "\"Seed\":4,\"Seed\":4", StringComparison.Ordinal);
            root["Payload"] = payload; root["Sha256"] = Hash(payload);
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(root.ToJsonString()));
        }),
        ("Integridade: todas as versões obrigatórias e incompatíveis rejeitadas", () => {
            var file = ChampionshipArchive.Encode(new Championship(1));
            foreach (var key in new[] { "Championship", "Simulation", "Random", "SeedAlgorithm", "Data" }) {
                Throws<InvalidDataException>(() => ChampionshipArchive.Decode(Rewrite(file, p => p[key] = "futuro")));
                Throws<InvalidDataException>(() => ChampionshipArchive.Decode(Rewrite(file, p => p.Remove(key))));
            }
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(Rewrite(file, p => p["Pontos"] = 999)));
        }),
        ("Integridade: pontos e eventos adulterados mesmo com checksum recalculado", () => {
            var c = new Championship(5); c.RunNext(1, Decisions(1));
            var file = ChampionshipArchive.Encode(c);
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(Rewrite(file,
                p => p["Rounds"]![0]!["Result"]!["Classification"]![0]!["Points"] = 999)));
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(Rewrite(file,
                p => p["Rounds"]![0]!["Result"]!["Events"]![0]!["Explanation"] = "evento falso")));
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(Rewrite(file,
                p => p["Rounds"]![0]!["Result"]!["CircuitId"] = "porto")));
        }),
        ("Integridade: seed, decisões, etapas repetidas ou fora de sequência rejeitadas", () => {
            var c = new Championship(5); c.RunNext(1, Decisions(1));
            var file = ChampionshipArchive.Encode(c);
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(Rewrite(file, p => p["Seed"] = 6)));
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(Rewrite(file,
                p => p["Rounds"]![0]!["Decisions"]![0]!["Pace"] = "Atacar")));
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(Rewrite(file,
                p => p["Rounds"]![0]!["Number"] = 2)));
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(Rewrite(file, p => {
                var rounds = p["Rounds"]!.AsArray(); rounds.Add(rounds[0]!.DeepClone());
            })));
        }),
        ("Integridade: nulos, coleção excessiva e arquivo acima de 8 MiB", () => {
            var file = ChampionshipArchive.Encode(new Championship(0));
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode("null"));
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(Rewrite(file, p => p["Rounds"] = null)));
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(Rewrite(file, p =>
                p["Rounds"] = new JsonArray(Enumerable.Range(0, 9).Select(_ => (JsonNode?)new JsonObject()).ToArray()))));
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(new string(' ', ChampionshipArchive.MaxBytes + 1)));
        }),
        ("Disco: salvar, carregar e continuar sem mudar etapas antigas", () => WithFolder(folder => {
            var path = Path.Combine(folder, "kart.json");
            ChampionshipStore.Create(path, 42);
            var first = ChampionshipStore.RunRound(path, 1, Decisions(1));
            var prior = JsonSerializer.Serialize(first.Rounds[0]);
            var loaded = ChampionshipStore.Load(path);
            Check(loaded.Rounds.Count == 1 && JsonSerializer.Serialize(loaded.Rounds[0]) == prior);
            ChampionshipStore.RunRound(path, 2, Decisions(2));
            Check(JsonSerializer.Serialize(ChampionshipStore.Load(path).Rounds[0]) == prior);
        })),
        ("Disco: avanço duplicado e comando inválido preservam bytes", () => WithFolder(folder => {
            var path = Path.Combine(folder, "kart.json");
            ChampionshipStore.Create(path, 42);
            ChampionshipStore.RunRound(path, 1, Decisions(1));
            var original = File.ReadAllBytes(path);
            Throws<InvalidOperationException>(() => ChampionshipStore.RunRound(path, 1, Decisions(1)));
            Throws<InvalidOperationException>(() => ChampionshipStore.RunRound(path, 3, Decisions(3)));
            var d = new SectorDecision(1, "p01", Pace.Conservar);
            Throws<ArgumentException>(() => ChampionshipStore.RunRound(path, 2, new[] { d, d }));
            Check(original.SequenceEqual(File.ReadAllBytes(path)));
        })),
        ("Disco: bloqueio impede escritores concorrentes", () => WithFolder(folder => {
            var path = Path.Combine(folder, "kart.json");
            ChampionshipStore.Create(path, 2);
            var before = File.ReadAllBytes(path);
            using (var held = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Throws<IOException>(() => ChampionshipStore.RunRound(path, 1, Decisions(1)));
            Check(before.SequenceEqual(File.ReadAllBytes(path)));
            var successes = 0;
            Parallel.For(0, 2, _ => {
                try { ChampionshipStore.RunRound(path, 1, Decisions(1)); Interlocked.Increment(ref successes); }
                catch (IOException) { }
                catch (InvalidOperationException) { }
            });
            Check(successes == 1 && ChampionshipStore.Load(path).Rounds.Count == 1);
        })),
        ("Disco: temporário órfão não altera o último checkpoint", () => WithFolder(folder => {
            var path = Path.Combine(folder, "kart.json");
            ChampionshipStore.Create(path, 3);
            ChampionshipStore.RunRound(path, 1, Decisions(1));
            File.WriteAllText(path + ".interrompido.tmp", "gravação incompleta");
            Check(ChampionshipStore.Load(path).Rounds.Count == 1);
            ChampionshipStore.RunRound(path, 2, Decisions(2));
            Check(ChampionshipStore.Load(path).Rounds.Count == 2);
        })),
        ("Disco: exportar/importar e completar mantém replay idêntico", () => WithFolder(folder => {
            var source = Path.Combine(folder, "original.json");
            var export = Path.Combine(folder, "exportado.json");
            var imported = Path.Combine(folder, "retomado.json");
            ChampionshipStore.Create(source, 42);
            for (var r = 1; r <= 3; r++) ChampionshipStore.RunRound(source, r, Decisions(r));
            var original = File.ReadAllBytes(source);
            ChampionshipStore.Export(source, export);
            ChampionshipStore.Import(export, imported);
            Check(File.ReadAllBytes(imported).SequenceEqual(original));
            for (var r = 4; r <= 8; r++) ChampionshipStore.RunRound(imported, r, Decisions(r));
            Check(ChampionshipArchive.Encode(ChampionshipStore.Load(imported)) == ChampionshipArchive.Encode(Complete(42)));
            Check(original.SequenceEqual(File.ReadAllBytes(source)));
        })),
        ("Disco: destinos existentes nunca são sobrescritos por novo/importar/exportar", () => WithFolder(folder => {
            var path = Path.Combine(folder, "kart.json");
            ChampionshipStore.Create(path, 42);
            var original = File.ReadAllBytes(path);
            Throws<IOException>(() => ChampionshipStore.Create(path, 99));
            Throws<IOException>(() => ChampionshipStore.Import(path, path));
            Throws<IOException>(() => ChampionshipStore.Export(path, path));
            Check(original.SequenceEqual(File.ReadAllBytes(path)));
        })),
        ("Disco: importação corrompida não cria destino e save inválido não avança", () => WithFolder(folder => {
            var path = Path.Combine(folder, "corrompido.json"); var target = Path.Combine(folder, "novo.json");
            File.WriteAllText(path, "{incompleto");
            Throws<InvalidDataException>(() => ChampionshipStore.Import(path, target));
            Check(!File.Exists(target));
            Throws<InvalidDataException>(() => ChampionshipStore.RunRound(path, 1, Decisions(1)));
            Check(File.ReadAllText(path) == "{incompleto");
            Throws<FileNotFoundException>(() => ChampionshipStore.Load(Path.Combine(folder, "inexistente.json")));
        }))
    };

    private static SectorDecision[] Decisions(int round) => new[] {
        new SectorDecision(1, "p01", round % 2 == 1 ? Pace.Conservar : Pace.Atacar),
        new SectorDecision(13, "p01", Pace.Equilibrado),
        new SectorDecision(28, "p01", Pace.Atacar)
    };
    private static Championship Complete(uint seed)
    {
        var c = new Championship(seed);
        while (!c.Finished) c.RunNext(c.NextRound, Decisions(c.NextRound));
        return c;
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string Rewrite(string text, Action<JsonObject> mutate)
    {
        var envelope = JsonNode.Parse(text)!.AsObject();
        var payload = JsonNode.Parse(envelope["Payload"]!.GetValue<string>())!.AsObject();
        mutate(payload);
        var serialized = payload.ToJsonString();
        envelope["Payload"] = serialized; envelope["Sha256"] = Hash(serialized);
        return envelope.ToJsonString();
    }
    private static void WithFolder(Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "stintoria-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try { action(path); } finally { Directory.Delete(path, true); }
    }
    private static void Check(bool condition)
    { if (!condition) throw new Exception("Invariante de campeonato não atendida."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Esperava " + typeof(T).Name);
    }
}
