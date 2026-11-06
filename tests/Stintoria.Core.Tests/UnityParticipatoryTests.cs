using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stintoria.ClientBridge;
using Stintoria.Core;
using Stintoria.Local;

internal static class UnityParticipatoryTests
{
    private static RaceStrategy Plan() => new(Pace.Conservar, Pace.Atacar, Pace.Equilibrado, 8, 29);
    private static CareerSession New(uint seed = 42, bool tactical = true)
    {
        var c = new CareerSession(seed, new CareerProfile("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02"));
        if (tactical) c.PrepareTactical(1, Training.Tecnico, Plan());
        else c.Prepare(1, Training.Descanso, Pace.Equilibrado);
        return c;
    }
    private static KartClientController Create(MemoryStore store, uint seed = 42)
    {
        var app = new KartClientController(store);
        app.Create(1, seed, "Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02", true);
        app.Prepare(Training.Tecnico, Plan(), true);
        return app;
    }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static void Check(bool value) { if (!value) throw new Exception("Invariante Unity participativa não atendida."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is IOException ||
            e is InvalidDataException || e is NotSupportedException) { return; }
        throw new Exception("Esperava rejeição da operação.");
    }
    private static string Magic(byte[] bytes)
    {
        using var r = new BinaryReader(new MemoryStream(bytes));
        return Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
    }
    private static void Finish(KartClientController app)
    {
        while (app.ActiveRace() is { } r) app.AdvanceRace(r.Round, r.NextSector);
    }
    private static void Rehash(byte[] bytes) => SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes.AsSpan(bytes.Length - 32));
    private static int ActiveOffset(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var r = new BinaryReader(stream);
        void Text() { var size = r.ReadInt32(); stream.Position += size; }
        for (var i = 0; i < 9; i++) Text();
        r.ReadUInt32(); Text(); Text(); r.ReadInt32(); Text();
        Check(r.ReadInt32() == 0 && r.ReadBoolean()); // nenhuma prova completa, preparação pendente
        r.ReadInt32(); r.ReadInt32();
        if (r.ReadBoolean()) stream.Position += 20;
        r.ReadBoolean();
        Check(r.ReadBoolean());
        return (int)stream.Position; // setor, PRNG, contagem, pares, digest
    }
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[] {
        ("Unity participativa: v1 permanece canônico para carreiras antigas e automáticas", () => {
            foreach (var tactical in new[] { false, true }) {
                var c = New(42, tactical);
                foreach (var completed in new[] { false, true }) {
                    if (completed) c.RunNext(1);
                    var bytes = ClientCheckpoint.Encode(c);
                    Check(Magic(bytes) == "STINTORIA-CLIENTE-1");
                    Check(bytes.SequenceEqual(ClientCheckpoint.EncodeLegacy(c)));
                    Check(bytes.SequenceEqual(ClientCheckpoint.Encode(ClientCheckpoint.Decode(bytes))));
                    Check(CareerArchive.Encode(c) == CareerArchive.Encode(ClientCheckpoint.Decode(bytes)));
                }
            }
        }),
        ("Unity participativa: v2 inclui largada zero e preserva preparo/PRNG", () => {
            var c = New(); c.StartRace(1); var bytes = ClientCheckpoint.Encode(c);
            Check(Magic(bytes) == "STINTORIA-CLIENTE-2");
            var loaded = ClientCheckpoint.Decode(bytes);
            Check(loaded.GetActiveRace()!.RandomState == 42 && loaded.Rounds.Count == 0);
            Check(loaded.GetActiveRace()!.State.CompletedSectors == 0 && loaded.Pending!.GetOpponentPlans().Count == 19);
            Check(CareerArchive.Encode(c) == CareerArchive.Encode(loaded));
            Reject(() => ClientCheckpoint.EncodeLegacy(c));
        }),
        ("Unity participativa: checkpoint retoma todas as fronteiras 0..35 com seeds extremas", () => {
            foreach (var seed in new uint[] { 0, 42, uint.MaxValue }) {
                var c = New(seed); c.StartRace(1);
                for (var sector = 1; sector <= 36; sector++) {
                    var bytes = ClientCheckpoint.Encode(c); var loaded = ClientCheckpoint.Decode(bytes);
                    Check(bytes.SequenceEqual(ClientCheckpoint.Encode(loaded)));
                    Check(CareerArchive.Encode(c) == CareerArchive.Encode(loaded));
                    var pace = sector % 4 == 0 ? Pace.Atacar : (Pace?)null;
                    c.AdvanceRace(1, sector, 1, pace); loaded.AdvanceRace(1, sector, 1, pace);
                    Check(ClientCheckpoint.Encode(c).SequenceEqual(ClientCheckpoint.Encode(loaded)));
                }
            }
        }),
        ("Unity participativa: consultas não calculam futuro, snapshot isolado e imutável", () => {
            var store = new MemoryStore(); var app = Create(store); app.StartRace(1);
            var before = store.Read(1); var zero = app.ActiveRace()!;
            for (var i = 0; i < 3; i++) {
                Check(app.ActiveRace()!.State.Events.Count == 0);
                Check(app.History().CompletedRounds == 0);
                Check(app.ActiveRace()!.NextLap == 1 && app.ActiveRace()!.NextCircuitSector == 1);
            }
            Check(before.SequenceEqual(store.Read(1)));
            Reject(() => ((IList<Driver>)zero.Drivers).Add(zero.Drivers[0]));
            app.AdvanceRace(1, 1, 3, Pace.Atacar);
            Check(zero.State.CompletedSectors == 0 && zero.Decisions.Count == 0);
            var snapshot = app.ActiveRace()!;
            Check(snapshot.NextSector == 4 && snapshot.NextLap == 2 && snapshot.NextCircuitSector == 1);
            Check(snapshot.State.Events.All(e => e.Step <= 3) && app.History().CompletedRounds == 0);
            Check(snapshot.State.Classification.All(d => d.Points == 0));
        }),
        ("Unity participativa: fluxo controller fecha/carrega slot e termina sem antecipação", () => {
            var aStore = new MemoryStore(); var a = Create(aStore); a.StartRace(1);
            a.AdvanceRace(1, 1, 3, Pace.Atacar); var checkpoint = aStore.Read(1);
            var bStore = new MemoryStore(); bStore.Write(1, checkpoint, null);
            var b = new KartClientController(bStore); b.Load(1);
            Check(b.ActiveRace()!.State.CompletedSectors == 3);
            a.Save(); b.Save(); Check(aStore.Read(1).SequenceEqual(bStore.Read(1)));
            foreach (var app in new[] { a, b }) {
                app.AdvanceRace(1, 4, 2, Pace.Conservar); Finish(app);
            }
            Check(a.ActiveRace() == null && b.ActiveRace() == null);
            Check(aStore.Read(1).SequenceEqual(bStore.Read(1)));
            Check(a.History().CompletedRounds == 1 && Json(a.History()) == Json(b.History()));
            Console.WriteLine("UNITY PARTICIPATIVA: checkpoint final SHA-256 " + Convert.ToHexString(SHA256.HashData(aStore.Read(1))));
        }),
        ("Unity participativa: comandos inválidos e repetidos preservam bytes e memória", () => {
            var store = new MemoryStore(); var app = Create(store); app.StartRace(1); app.AdvanceRace(1, 1);
            var bytes = store.Read(1); var state = Json(app.ActiveRace());
            foreach (var action in new Action[] {
                () => app.StartRace(1), () => app.Run(), () => app.AdvanceRace(1, 1),
                () => app.AdvanceRace(2, 2), () => app.AdvanceRace(1, 2, 4),
                () => app.AdvanceRace(1, 2, 1, (Pace)99),
                () => app.Prepare(Training.Tecnico, Plan(), true),
                () => app.Preview(Training.Tecnico, Plan(), true) }) {
                Reject(action); Check(bytes.SequenceEqual(store.Read(1)) && state == Json(app.ActiveRace()));
            }
        }),
        ("Unity participativa: falha de escrita em largada/avanço não adota estado não salvo", () => {
            var store = new MemoryStore(); var app = Create(store);
            var pending = store.Read(1); store.Fail = true; Reject(() => app.StartRace(1));
            Check(app.ActiveRace() == null && pending.SequenceEqual(store.Read(1)));
            store.Fail = false; app.StartRace(1); var before = store.Read(1); var state = Json(app.ActiveRace());
            store.Fail = true; Reject(() => app.AdvanceRace(1, 1, 3, Pace.Atacar));
            Check(before.SequenceEqual(store.Read(1)) && state == Json(app.ActiveRace()));
            store.Fail = false; app.AdvanceRace(1, 1, 3, Pace.Atacar);
            Check(app.ActiveRace()!.State.CompletedSectors == 3);
        }),
        ("Unity participativa: falha na bandeirada não duplica pontos e permite repetir ordem", () => {
            var store = new MemoryStore(); var app = Create(store); app.StartRace(1);
            for (var sector = 1; sector <= 35; sector++) app.AdvanceRace(1, sector);
            var before = store.Read(1); store.Fail = true; Reject(() => app.AdvanceRace(1, 36));
            Check(app.History().CompletedRounds == 0 && app.ActiveRace()!.State.CompletedSectors == 35);
            Check(before.SequenceEqual(store.Read(1)));
            store.Fail = false; app.AdvanceRace(1, 36);
            var finished = store.Read(1); Reject(() => app.AdvanceRace(1, 36)); Reject(() => app.StartRace(1));
            Check(app.History().CompletedRounds == 1 && finished.SequenceEqual(store.Read(1)));
        }),
        ("Unity participativa: conflito entre duas instâncias não avança setor desatualizado", () => {
            var store = new MemoryStore(); var a = Create(store); a.StartRace(1);
            var b = new KartClientController(store); b.Load(1); a.AdvanceRace(1, 1);
            var current = store.Read(1); Reject(() => b.AdvanceRace(1, 1));
            Check(b.ActiveRace()!.State.CompletedSectors == 0 && current.SequenceEqual(store.Read(1)));
            b.Load(1); Reject(() => b.AdvanceRace(1, 1)); b.AdvanceRace(1, 2);
            Check(b.ActiveRace()!.State.CompletedSectors == 2);
        }),
        ("Unity participativa: ordens valem no setor correto e rivais usam mesmas regras", () => {
            var store = new MemoryStore(); var app = Create(store); app.StartRace(1);
            var core = New(); core.StartRace(1);
            for (var sector = 1; sector <= 36; sector++) {
                var pace = sector == 8 || sector == 29 ? Pace.Conservar : (Pace?)null;
                app.AdvanceRace(1, sector, 1, pace); core.AdvanceRace(1, sector, 1, pace);
                Check(CareerArchive.Encode(core) == CareerArchive.Encode(ClientCheckpoint.Decode(store.Read(1))));
            }
            var report = app.Report(1, "jogador");
            var observed = report.Segments.SelectMany(s => s.Sectors).ToArray();
            Check(observed[7].ChosenPace == Pace.Conservar && observed[7].ObservedPace == Pace.Conservar);
            Check(observed.All(s => s.DurationMs.HasValue) && report.Available);
            Check(app.History().GetRound(1).Decisions.Any(d => d.Sector == 8 && d.DriverId == "jogador" && d.Pace == Pace.Conservar));
        }),
        ("Unity participativa: modo automático legado continua disponível e igual ao núcleo", () => {
            var store = new MemoryStore(); var app = Create(store); app.Run();
            var core = New(); core.RunNext(1);
            Check(Magic(store.Read(1)) == "STINTORIA-CLIENTE-1" && app.ActiveRace() == null);
            Check(CareerArchive.Encode(core) == CareerArchive.Encode(ClientCheckpoint.Decode(store.Read(1))));
        }),
        ("Unity participativa: oito provas mistas com checkpoints preservam campeonato e histórico", () => {
            var store = new MemoryStore(); var app = Create(store);
            for (var round = 1; round <= 8; round++) {
                if (round > 1) app.Prepare(Training.Descanso, Plan(), true);
                if (round % 2 == 0) app.Run();
                else {
                    app.StartRace(round); app.AdvanceRace(round, 1, 3, Pace.Atacar);
                    var reopened = new KartClientController(store); reopened.Load(1); app = reopened; Finish(app);
                }
                Check(app.History().CompletedRounds == round);
                Check(store.Read(1).SequenceEqual(ClientCheckpoint.Encode(ClientCheckpoint.Decode(store.Read(1)))));
            }
            Check(app.Overview().Finished && app.History().Finished);
            Reject(() => app.StartRace(9));
            var official = ClientCheckpoint.Decode(store.Read(1));
            Check(official.DriverStandings.Sum(d => d.Points) == official.Rounds.Sum(r => r.Race.Result.Classification.Sum(d => d.Points)));
            Check(official.TeamStandings.Sum(d => d.Points) == official.DriverStandings.Sum(d => d.Points));
        }),
        ("Unity participativa: v2 rejeita truncamento, checksum e versão incompatível", () => {
            var c = New(); c.StartRace(1); c.AdvanceRace(1, 1); var bytes = ClientCheckpoint.Encode(c);
            Reject(() => ClientCheckpoint.Decode(bytes[..^1])); Reject(() => ClientCheckpoint.Decode(new byte[ClientCheckpoint.MaxBytes + 1]));
            var corrupt = (byte[])bytes.Clone(); corrupt[^40] ^= 1; Reject(() => ClientCheckpoint.Decode(corrupt));
            corrupt = (byte[])bytes.Clone(); corrupt[4 + "STINTORIA-CLIENTE-".Length] = (byte)'9'; Rehash(corrupt);
            Reject(() => ClientCheckpoint.Decode(corrupt));
        }),
        ("Unity participativa: rehash não oculta adulteração de PRNG, setor, ledger ou snapshot", () => {
            var c = New(); c.StartRace(1); c.AdvanceRace(1, 1, 3, Pace.Atacar);
            var bytes = ClientCheckpoint.Encode(c); var offset = ActiveOffset(bytes);
            foreach (var index in new[] { offset, offset + 4, offset + 8, offset + 12, offset + 16, offset + 20, bytes.Length - 40 }) {
                var changed = (byte[])bytes.Clone(); changed[index] ^= 1; Rehash(changed);
                Reject(() => ClientCheckpoint.Decode(changed));
            }
        }),
        ("Unity participativa disco: slots reais, troca de piloto e retomada exata", () => Folder(dir => {
            var store = new DesktopCheckpointStore(dir); var app = new KartClientController(store);
            app.Create(1, 42, "Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02", true);
            app.Prepare(Training.Tecnico, Plan(), true); app.StartRace(1); app.AdvanceRace(1, 1, 3, Pace.Atacar);
            var state = Json(app.ActiveRace()); var bytes = store.Read(1);
            var reopened = new KartClientController(new DesktopCheckpointStore(dir)); reopened.Load(1);
            Check(Json(reopened.ActiveRace()) == state && bytes.SequenceEqual(store.Read(1)));
            reopened.Create(2, 0, "Lia Horizonte", "Brasileira", CareerOrigin.FamiliaHumilde, "t01", true);
            reopened.Load(1); Check(Json(reopened.ActiveRace()) == state);
            Finish(reopened); Check(reopened.History().CompletedRounds == 1);
        })),
        ("Unity participativa: migração explícita do JSON v4 preserva origem e permite continuar", () => Folder(dir => {
            var c = New(); c.StartRace(1); c.AdvanceRace(1, 1, 3, Pace.Atacar);
            var source = Path.Combine(dir, "terminal.json"); File.WriteAllText(source, CareerArchive.Encode(c));
            var original = File.ReadAllBytes(source); var destination = Path.Combine(dir, "slots");
            var migration = new TerminalCareerMigration(); var preview = migration.Preview(source, destination, 1);
            migration.Confirm(preview, true);
            var app = new KartClientController(new DesktopCheckpointStore(destination)); app.Load(1);
            Check(CareerArchive.Encode(c) == CareerArchive.Encode(ClientCheckpoint.Decode(new DesktopCheckpointStore(destination).Read(1))));
            Check(original.SequenceEqual(File.ReadAllBytes(source)) && app.ActiveRace()!.State.CompletedSectors == 3);
            app.AdvanceRace(1, 4, 1, Pace.Conservar); c.AdvanceRace(1, 4, 1, Pace.Conservar);
            Check(Json(app.ActiveRace()!.State) == Json(c.GetActiveRace()!.State));
        })),
    };
    private sealed class MemoryStore : IClientCheckpointStore
    {
        private readonly Dictionary<int, byte[]> saves = new();
        internal bool Fail;
        public bool Exists(int slot) => saves.ContainsKey(slot);
        public byte[] Read(int slot) => (byte[])saves[slot].Clone();
        public void Write(int slot, byte[] bytes, byte[]? expected)
        {
            if (Fail || (expected == null ? Exists(slot) : !Exists(slot) || !Read(slot).SequenceEqual(expected)))
                throw new IOException("Falha/conflito de escrita injetado.");
            saves[slot] = (byte[])bytes.Clone();
        }
    }
    private static void Folder(Action<string> action)
    {
        var dir = Path.Combine(Path.GetTempPath(), "stintoria-unity-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir); try { action(dir); } finally { Directory.Delete(dir, true); }
    }
}
