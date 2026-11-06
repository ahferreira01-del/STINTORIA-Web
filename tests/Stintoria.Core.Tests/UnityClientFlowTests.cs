using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using Stintoria.ClientBridge;
using Stintoria.Core;
using Stintoria.Local;

internal static class UnityClientFlowTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[] {
        ("Cliente: checkpoint preserva carreiras v1/v2/v3 e arquivos oficiais sem converter JSON", () => {
            var c = New();
            for (var i = 1; i <= 3; i++) {
                if (i == 1) c.Prepare(i, Training.Descanso, Pace.Atacar);
                if (i == 2) c.PrepareStrategy(i, Training.Descanso, Plan());
                if (i == 3) c.PrepareTactical(i, Training.Descanso, Plan());
                c.RunNext(i);
                var official = CareerArchive.Encode(c);
                var bytes = ClientCheckpoint.Encode(c); var restored = ClientCheckpoint.Decode(bytes);
                Check(CareerArchive.Encode(restored) == official);
                Check(bytes.SequenceEqual(ClientCheckpoint.Encode(restored)));
                Check(CareerArchive.Encode(c) == official);
            }
        }),
        ("Cliente: checkpoint rejeita truncamento, checksum, versão e seed adulterada com checksum recalculado", () => {
            var c = New(); c.PrepareTactical(1, Training.Tecnico, Plan()); c.RunNext(1);
            var bytes = ClientCheckpoint.Encode(c);
            foreach (var invalid in new[] {Array.Empty<byte>(), bytes.Take(40).ToArray(), bytes.Take(bytes.Length - 1).ToArray(),
                new byte[ClientCheckpoint.MaxBytes + 1]})
                Throws<InvalidDataException>(() => ClientCheckpoint.Decode(invalid));
            var altered = (byte[])bytes.Clone(); altered[altered.Length - 1] ^= 1;
            Throws<InvalidDataException>(() => ClientCheckpoint.Decode(altered));
            var seedChanged = (byte[])bytes.Clone(); seedChanged[SeedOffset(bytes)] ^= 1; Rehash(seedChanged);
            Throws<InvalidDataException>(() => ClientCheckpoint.Decode(seedChanged));
            var version = (byte[])bytes.Clone(); version[4] ^= 1; Rehash(version);
            Throws<InvalidDataException>(() => ClientCheckpoint.Decode(version));
        }),
        ("Cliente: checkpoint retoma preparação pendente e não duplica treino/etapa", () => {
            var c = New(); c.PrepareTactical(1, Training.Tecnico, Plan());
            var bytes = ClientCheckpoint.Encode(c); var loaded = ClientCheckpoint.Decode(bytes);
            Check(loaded.Skill == 72 && loaded.TrainingCredits == 3 && loaded.Pending!.GetOpponentPlans().Count == 19);
            Throws<InvalidOperationException>(() => loaded.PrepareTactical(1, Training.Tecnico, Plan()));
            loaded.RunNext(1); c.RunNext(1);
            Check(ClientCheckpoint.Encode(loaded).SequenceEqual(ClientCheckpoint.Encode(c)));
            Throws<InvalidOperationException>(() => loaded.RunNext(1));
        }),
        ("Cliente: criação exige confirmação, valida piloto e nunca sobrescreve slot", () => {
            var store = new MemoryStore(); var app = new KartClientController(store);
            Throws<InvalidOperationException>(() => app.Create(1, 42, "Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02", false));
            Throws<ArgumentException>(() => app.Create(1, 42, "123", "Brasileira", CareerOrigin.SimRacer, "t02", true));
            Check(!app.HasCareer && !store.Exists(1));
            Create(app); var before = store.Read(1);
            Throws<IOException>(() => Create(app));
            Check(store.Read(1).SequenceEqual(before) && app.Overview().Profile.Name == "Ari Horizonte");
        }),
        ("Cliente: selecionar outro piloto preserva slots, idade e identidade própria", () => {
            var store = new MemoryStore(); var app = new KartClientController(store); Create(app);
            app.Create(2, 9, "Luz do Vale", "Portuguesa", CareerOrigin.Empresaria, "t01", true);
            Check(app.Overview().Profile.Name == "Luz do Vale" && app.ActiveSlot == 2);
            app.Load(1);
            Check(app.Overview().Profile.Name == "Ari Horizonte" && app.Overview().Profile.Age == 14 && app.ActiveSlot == 1);
            Check(app.InspectSlot(2).Profile.Nationality == "Portuguesa");
            Check(app.History().DriverStandings.Single(r => r.IsPlayerAssociated).Id == "jogador");
        }),
        ("Cliente: prévia/cancelamento não gravam nem consomem treino", () => {
            var store = new MemoryStore(); var app = new KartClientController(store); Create(app);
            var bytes = store.Read(1); var result = app.Preview(Training.Tecnico, Plan(), true);
            Check(result.Preparation!.Opponents.Count == 19 && result.Preparation.SkillApplied == 72);
            Check(app.Overview().Skill == 70 && app.Overview().Credits == 4 && !app.Overview().Prepared);
            Check(store.Read(1).SequenceEqual(bytes));
        }),
        ("Cliente: falha de escrita não avança memória nem altera último save", () => {
            var store = new MemoryStore(); var app = new KartClientController(store); Create(app);
            var initial = store.Read(1); store.Fail = true;
            Throws<IOException>(() => app.Prepare(Training.Tecnico, Plan(), true));
            Check(store.Read(1).SequenceEqual(initial) && !app.Overview().Prepared && app.Overview().Credits == 4);
            store.Fail = false; app.Prepare(Training.Tecnico, Plan(), true);
            var prepared = store.Read(1); store.Fail = true;
            Throws<IOException>(() => app.Run());
            Check(app.History().CompletedRounds == 0 && app.Overview().Prepared && store.Read(1).SequenceEqual(prepared));
        }),
        ("Cliente: prova, revisão e tabelas vêm dos mesmos resultados oficiais", () => {
            var store = new MemoryStore(); var app = new KartClientController(store); Create(app);
            app.Prepare(Training.Tecnico, Plan(), true); app.Run();
            var saved = store.Read(1); var official = app.History().GetRound(1);
            foreach (var sector in new[] {1, 7, 8, 13, 25, 29, 36}) {
                var frame = app.Review(1, sector);
                Check(ReferenceEquals(frame, app.Review(1, sector)));
                Check(frame.CompleteObservations && frame.Drivers.Count == 20);
                Check(frame.Drivers.Select(d => d.Position).SequenceEqual(Enumerable.Range(1, 20).Select(i => (int?)i)));
                foreach (var row in frame.Drivers) {
                    var data = app.Report(1, row.DriverId).Segments.SelectMany(s => s.Sectors).Take(sector).ToArray();
                    Check(row.ElapsedMs == data.Sum(s => s.DurationMs) && row.Wear == data.Last().WearAfter);
                }
            }
            var final = app.Review(1, 36);
            Check(final.Drivers.Select(d => d.DriverId).SequenceEqual(official.Classification.Select(d => d.DriverId)));
            Check(final.Drivers.Select(d => d.ElapsedMs).SequenceEqual(official.Classification.Select(d => (long?)d.ElapsedMs)));
            Check(saved.SequenceEqual(store.Read(1)));
            Throws<InvalidOperationException>(() => app.Run());
        }),
        ("Cliente: revisão imutável rejeita etapa futura e limites de setor", () => {
            var c = New(); var future = HistoryQuery.ForCareer(c).GetRound(1);
            Throws<InvalidOperationException>(() => RaceReviewQuery.ForRound(future, 1));
            c.Prepare(1, Training.Descanso, Pace.Equilibrado); c.RunNext(1);
            var round = HistoryQuery.ForCareer(c).GetRound(1);
            Throws<ArgumentOutOfRangeException>(() => RaceReviewQuery.ForRound(round, 0));
            Throws<ArgumentOutOfRangeException>(() => RaceReviewQuery.ForRound(round, 37));
            var frame = RaceReviewQuery.ForRound(round, 36);
            Throws<NotSupportedException>(() => ((IList)frame.Drivers).Clear());
            Check(typeof(RaceReviewDriver).GetProperties().All(p => p.SetMethod == null));
        }),
        ("Cliente: revisão sem evento não inventa tempo nem posição", () => {
            var c = New(); c.Prepare(1, Training.Descanso, Pace.Equilibrado); c.RunNext(1);
            var r = HistoryQuery.ForCareer(c).GetRound(1);
            var partial = new HistoryRound(InitialData.KartCalendar[0], r.Status, r.Preparation, r.Classification.ToArray(),
                r.DriverStandings.ToArray(), r.TeamStandings.ToArray(), r.Decisions.ToArray(),
                r.Events.Where(e => !(e.DriverId == "jogador" && e.Code == "setor" && e.Step == 1)).ToArray(),
                "jogador", r.Weather);
            var frame = RaceReviewQuery.ForRound(partial, 1);
            Check(!frame.CompleteObservations && frame.Drivers.All(d => d.Position == null));
            Check(frame.Drivers.Single(d => d.DriverId == "jogador").ElapsedMs == null);
        }),
        ("Cliente: oito provas com save local, retomada e arquivo idêntico ao fluxo contínuo", () => Folder(dir => {
            var disk = new DesktopCheckpointStore(dir); var app = new KartClientController(disk);
            var memory = new MemoryStore(); var uninterrupted = new KartClientController(memory);
            Create(app); Create(uninterrupted);
            for (var round = 1; round <= 8; round++) {
                var training = round <= 4 ? Training.Tecnico : Training.Descanso;
                app.Prepare(training, Plan(), true); uninterrupted.Prepare(training, Plan(), true);
                if (round == 4) { app = new KartClientController(disk); app.Load(1); Check(app.Overview().Prepared); }
                app.Run(); uninterrupted.Run();
                Check(disk.Read(1).SequenceEqual(memory.Read(1)));
            }
            Check(app.History().Finished && app.History().DriverStandings.Sum(r => r.Points) == 808);
            Check(app.History().TeamStandings.Sum(r => r.Points) == 808 && app.Overview().Credits == 0);
            Throws<InvalidOperationException>(() => app.Prepare(Training.Descanso, Plan(), false));
            var final = disk.Read(1); app.Save(); Check(final.SequenceEqual(disk.Read(1)));
            Console.WriteLine("CHECKPOINT CLIENTE SHA-256: " + Convert.ToHexString(SHA256.HashData(final)));
        })),
        ("Cliente: lock de disco e sessão desatualizada preservam dados e memória", () => Folder(dir => {
            var disk = new DesktopCheckpointStore(dir); var first = new KartClientController(disk); Create(first);
            var second = new KartClientController(disk); second.Load(1);
            var before = disk.Read(1);
            using (File.Open(Path.Combine(dir, "carreira-1.stint.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Throws<IOException>(() => first.Prepare(Training.Tecnico, Plan(), true));
            Check(disk.Read(1).SequenceEqual(before) && first.Overview().Credits == 4);
            first.Prepare(Training.Tecnico, Plan(), true); var prepared = disk.Read(1);
            Throws<IOException>(() => second.Prepare(Training.Tecnico, Plan(), true));
            Check(disk.Read(1).SequenceEqual(prepared) && !second.Overview().Prepared && second.Overview().Credits == 4);
            second.Load(1); second.Run(); Check(second.History().CompletedRounds == 1);
        })),
        ("Cliente: arquivo corrompido não é carregado nem substituído silenciosamente", () => Folder(dir => {
            var disk = new DesktopCheckpointStore(dir); var app = new KartClientController(disk); Create(app);
            File.WriteAllBytes(Path.Combine(dir, "carreira-2.stint"), new byte[] {1, 2, 3});
            Throws<InvalidDataException>(() => app.Load(2));
            Check(app.ActiveSlot == 1 && app.Overview().Profile.Name == "Ari Horizonte");
            Throws<IOException>(() => app.Create(2, 0, "Luz do Vale", "Brasileira", CareerOrigin.SimRacer, "t01", true));
            Check(File.ReadAllBytes(Path.Combine(dir, "carreira-2.stint")).SequenceEqual(new byte[] {1, 2, 3}));
            Throws<ArgumentOutOfRangeException>(() => disk.Exists(0));
            Throws<ArgumentOutOfRangeException>(() => disk.Exists(4));
        })),
        ("Cliente: seeds extremas mantêm determinismo e checkpoint sem dependência de UI", () => {
            foreach (var seed in new[] {0u, uint.MaxValue}) {
                var a = New(seed); var b = New(seed);
                foreach (var c in new[] {a, b}) { c.PrepareTactical(1, Training.Descanso, Plan()); c.RunNext(1); }
                Check(ClientCheckpoint.Encode(a).SequenceEqual(ClientCheckpoint.Encode(b)));
                Check(Json(HistoryQuery.ForCareer(a)) == Json(HistoryQuery.ForCareer(ClientCheckpoint.Decode(ClientCheckpoint.Encode(a)))));
            }
        })
    };
    private sealed class MemoryStore : IClientCheckpointStore
    {
        private readonly Dictionary<int, byte[]> bytes = new();
        internal bool Fail;
        public bool Exists(int slot) => bytes.ContainsKey(slot);
        public byte[] Read(int slot) => (byte[])bytes[slot].Clone();
        public void Write(int slot, byte[] value, byte[]? expected) {
            if (Fail || (expected == null ? Exists(slot) : !Exists(slot) || !bytes[slot].SequenceEqual(expected))) throw new IOException("Escrita rejeitada no teste.");
            bytes[slot] = (byte[])value.Clone();
        }
    }
    private static void Create(KartClientController app) => app.Create(1, 42, "Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02", true);
    private static CareerSession New(uint seed = 42) => new(seed, new CareerProfile("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02"));
    private static RaceStrategy Plan() => new(Pace.Conservar, Pace.Equilibrado, Pace.Atacar, 8, 29);
    private static int SeedOffset(byte[] bytes) {
        using var stream = new MemoryStream(bytes); using var r = new BinaryReader(stream);
        for (var i = 0; i < 9; i++) { var size = r.ReadInt32(); stream.Position += size; }
        return (int)stream.Position;
    }
    private static void Rehash(byte[] bytes) => SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes.AsSpan(bytes.Length - 32));
    private static void Folder(Action<string> action) {
        var dir = Path.Combine(Path.GetTempPath(), "stintoria-client-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir); try { action(dir); } finally { Directory.Delete(dir, true); }
    }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static void Check(bool condition) { if (!condition) throw new Exception("Invariante do cliente não atendida."); }
    private static void Throws<T>(Action action) where T : Exception {
        try { action(); } catch (T) { return; } throw new Exception("Esperava " + typeof(T).Name);
    }
}
