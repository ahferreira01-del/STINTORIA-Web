using System.Security.Cryptography;
using System.Text.Json;
using Stintoria.ClientBridge;
using Stintoria.Core;
using Stintoria.Local;

internal static class BrowserPersistenceTests
{
    private static RaceStrategy Plan() => new(Pace.Conservar, Pace.Atacar, Pace.Equilibrado, 8, 29);
    private static CareerSession New(bool live = false, uint seed = 42)
    {
        var c = new CareerSession(seed, new CareerProfile("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02"));
        c.PrepareTactical(1, Training.Descanso, Plan());
        if (live) { c.StartRace(1); c.AdvanceRace(1, 1, 3, Pace.Atacar); }
        return c;
    }
    private static async Task<(MemoryTransport Transport, BrowserCheckpointStore Store, KartClientController App)> Ready(bool live = true)
    {
        var transport = new MemoryTransport(); var store = new BrowserCheckpointStore(transport);
        await store.RefreshAsync(); var app = new KartClientController(store);
        await app.ImportAsync(1, ClientCheckpoint.Encode(New(live)), true);
        return (transport, store, app);
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Invariante de persistência Web não atendida."); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is BrowserStorageException || e is IOException || e is InvalidDataException ||
            e is InvalidOperationException || e is ArgumentException || e is OverflowException) { return; }
        throw new Exception("Operação deveria ter sido rejeitada.");
    }
    private static string Json<T>(T data) => JsonSerializer.Serialize(data);
    private static bool Same(byte[]? a, byte[]? b) => a == null ? b == null : b != null && a.SequenceEqual(b);
    private static bool Same(BrowserSlotPair a, BrowserSlotPair b) => a.EncodedA == b.EncodedA && a.EncodedB == b.EncodedB;
    private static byte[] Generation(byte[] source, long generation)
    {
        var copy = (byte[])source.Clone(); using var stream = new MemoryStream(copy);
        using var r = new BinaryReader(stream); r.ReadString();
        using var w = new BinaryWriter(stream); w.Write(generation);
        SHA256.HashData(copy.AsSpan(0, copy.Length - 32)).CopyTo(copy.AsSpan(copy.Length - 32));
        return copy;
    }
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string Name, Func<Task> Body)[] {
        ("Web: exige leitura inicial e nunca usa escrita síncrona", async () => {
            var store = new BrowserCheckpointStore(new MemoryTransport());
            await Reject(() => { store.Exists(1); return Task.CompletedTask; });
            await Reject(() => store.WriteAsync(1, ClientCheckpoint.Encode(New()), null));
            await store.RefreshAsync();
            await Reject(() => { store.Write(1, ClientCheckpoint.Encode(New()), null); return Task.CompletedTask; });
            Check(!store.Exists(1));
        }),
        ("Web: roundtrip v1/v2 conserva bytes portáteis e JSON autoritativo", async () => {
            foreach (var live in new[] { false, true }) {
                var c = New(live); var bytes = ClientCheckpoint.Encode(c);
                Check(ClientCheckpoint.GetFormatVersion(bytes) == (live ? 2 : 1));
                var transport = new MemoryTransport(); var store = new BrowserCheckpointStore(transport);
                await store.RefreshAsync(); await store.WriteAsync(1, bytes, null);
                var reopened = new BrowserCheckpointStore(transport); await reopened.RefreshAsync();
                Check(bytes.SequenceEqual(reopened.Read(1)));
                Check(CareerArchive.Encode(c) == CareerArchive.Encode(ClientCheckpoint.Decode(reopened.Read(1))));
            }
        }),
        ("Web: criação só é adotada depois da confirmação, ações concorrentes bloqueadas", async () => {
            var transport = new MemoryTransport { Gate = new TaskCompletionSource<bool>() };
            var store = new BrowserCheckpointStore(transport); await store.RefreshAsync();
            var app = new KartClientController(store);
            var pending = app.CreateAsync(1, 42, "Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02", true);
            Check(!pending.IsCompleted && app.IsSaving && !app.HasCareer && store.State == BrowserSaveState.Writing);
            await Reject(() => app.CreateAsync(2, 42, "Lia Horizonte", "Brasileira", CareerOrigin.SimRacer, "t01", true));
            transport.Gate.SetResult(true); await pending;
            Check(app.HasCareer && !app.IsSaving && store.State == BrowserSaveState.Confirmed && app.ActiveSlot == 1);
        }),
        ("Web: avanço pendente não confirma setor nem muda exportação", async () => {
            var x = await Ready(); var previous = x.App.ExportCheckpoint();
            x.Transport.Gate = new TaskCompletionSource<bool>();
            var pending = x.App.AdvanceRaceAsync(1, 4, 2, Pace.Conservar);
            Check(!pending.IsCompleted && x.App.ActiveRace()!.State.CompletedSectors == 3);
            Check(previous.SequenceEqual(x.App.ExportCheckpoint()) && x.Store.State == BrowserSaveState.Writing);
            await Reject(() => x.App.SaveAsync());
            x.Transport.Gate.SetResult(true); await pending;
            Check(x.App.ActiveRace()!.State.CompletedSectors == 5);
            await Reject(() => x.App.AdvanceRaceAsync(1, 4));
        }),
        ("Web: A/B alterna e recupera última cópia válida sem reescrever corrupção", async () => {
            var x = await Ready(); var old = x.App.ExportCheckpoint();
            await x.App.AdvanceRaceAsync(1, 4); var pair = x.Transport.Pairs[0];
            Check(pair.A != null && pair.B != null);
            var corrupt = pair.B!; corrupt[^1] ^= 1;
            x.Transport.Pairs[0] = new BrowserSlotPair(pair.A, corrupt);
            await x.App.LoadAsync(1);
            Check(old.SequenceEqual(x.App.ExportCheckpoint()) && x.Store.State == BrowserSaveState.Recovered);
            Check(x.Store.Message.Contains("ausente") && Same(x.Transport.Pairs[0].B, corrupt));
            await x.App.AdvanceRaceAsync(1, 4);
            Check(x.App.ActiveRace()!.State.CompletedSectors == 4 && x.Store.State == BrowserSaveState.Confirmed);
        }),
        ("Web: duas cópias corrompidas permanecem ocupadas e não são apagadas", async () => {
            var x = await Ready();
            x.Transport.Pairs[0] = new BrowserSlotPair(new byte[] { 1 }, new byte[] { 2 });
            await x.Store.RefreshAsync(); var pair = x.Transport.Pairs[0];
            Check(x.Store.Exists(1));
            await Reject(() => x.App.LoadAsync(1));
            await Reject(() => x.App.ImportAsync(1, ClientCheckpoint.Encode(New()), true));
            Check(Same(pair, x.Transport.Pairs[0]) && x.Store.ExportRawBank(1, true)!.SequenceEqual(new byte[] { 1 }));
        }),
        ("Web: mesma geração com identidades diferentes não escolhe silenciosamente", async () => {
            var a = await Ready(); var b = await Ready();
            var other = new BrowserCheckpointStore(b.Transport);
            await other.RefreshAsync(); await other.WriteAsync(2, ClientCheckpoint.Encode(New(true, 99)), null);
            a.Transport.Pairs[0] = new BrowserSlotPair(a.Transport.Pairs[0].A, b.Transport.Pairs[1].A);
            await a.Store.RefreshAsync();
            await Reject(() => a.App.LoadAsync(1)); Check(a.Store.Exists(1));
        }),
        ("Web: Base64 inválido é ocupado, permite recuperar banco válido e mantém token CAS", async () => {
            var x = await Ready(); var raw = "inválido|;%";
            var encoded = BrowserSlotPair.FromEncoded(Convert.ToBase64String(x.Transport.Pairs[0].A!), raw);
            Check(encoded.EncodedB == raw && encoded.B != null);
            x.Transport.Pairs[0] = encoded; await x.App.LoadAsync(1);
            Check(x.App.ActiveRace()!.State.CompletedSectors == 3 && x.Store.State == BrowserSaveState.Recovered);
            await x.App.AdvanceRaceAsync(1, 4);
            Check(x.App.ActiveRace()!.State.CompletedSectors == 4);
        }),
        ("Web: armazenamento indisponível bloqueia sem fabricar save", async () => {
            var transport = new MemoryTransport { ReadFailure = true };
            var store = new BrowserCheckpointStore(transport);
            await Reject(() => store.RefreshAsync());
            Check(store.State == BrowserSaveState.Failed && transport.Writes == 0);
            var app = new KartClientController(store);
            await Reject(() => app.ImportAsync(1, ClientCheckpoint.Encode(New()), true)); Check(!app.HasCareer);
            transport.ReadFailure = false; await store.RefreshAsync();
            await app.ImportAsync(1, ClientCheckpoint.Encode(New()), true); Check(app.HasCareer);
        }),
        ("Web: quota preserva estado anterior e oferece proposta válida para exportação", async () => {
            var x = await Ready(); var before = x.App.ExportCheckpoint(); var pair = x.Transport.Pairs[0];
            x.Transport.Failure = "quota";
            await Reject(() => x.App.AdvanceRaceAsync(1, 4, 3, Pace.Conservar));
            Check(before.SequenceEqual(x.App.ExportCheckpoint()) && Same(pair, x.Transport.Pairs[0]));
            Check(x.Store.State == BrowserSaveState.Failed && x.Store.Message.Contains("Espaço"));
            var draft = x.Store.RecoveryCheckpoint!;
            Check(ClientCheckpoint.Decode(draft).GetActiveRace()!.State.CompletedSectors == 6);
            draft[0] ^= 1; Check(ClientCheckpoint.Decode(x.Store.RecoveryCheckpoint!).GetActiveRace()!.State.CompletedSectors == 6);
            await Reject(() => x.App.AdvanceRaceAsync(1, 4)); // precisa reler após falha
            await x.App.LoadAsync(1); await x.App.AdvanceRaceAsync(1, 4);
            Check(x.Store.RecoveryCheckpoint == null && x.App.ActiveRace()!.State.CompletedSectors == 4);
        }),
        ("Web: confirmação perdida pode ter gravado; reler evita avanço duplicado", async () => {
            var x = await Ready(); x.Transport.LoseAcknowledgement = true;
            await Reject(() => x.App.AdvanceRaceAsync(1, 4, 2, Pace.Atacar));
            Check(x.Store.State == BrowserSaveState.Uncertain && x.App.ActiveRace()!.State.CompletedSectors == 3);
            Check(x.Store.RecoveryCheckpoint != null);
            await x.App.LoadAsync(1); Check(x.App.ActiveRace()!.State.CompletedSectors == 5);
            await Reject(() => x.App.AdvanceRaceAsync(1, 4)); await x.App.AdvanceRaceAsync(1, 6);
            Check(x.App.ActiveRace()!.State.CompletedSectors == 6);
        }),
        ("Web: CAS compara par A/B completo entre duas abas", async () => {
            var x = await Ready(); var other = new BrowserCheckpointStore(x.Transport); await other.RefreshAsync();
            var app = new KartClientController(other); await app.LoadAsync(1);
            await x.App.AdvanceRaceAsync(1, 4);
            await Reject(() => app.AdvanceRaceAsync(1, 4, 1, Pace.Conservar));
            Check(app.ActiveRace()!.State.CompletedSectors == 3 && other.State == BrowserSaveState.Failed);
            await app.LoadAsync(1); Check(app.ActiveRace()!.State.CompletedSectors == 4);
        }),
        ("Web: interrupção antes do commit não confirma nem altera bancos", async () => {
            var x = await Ready(); var pair = x.Transport.Pairs[0];
            x.Transport.Failure = "unavailable";
            await Reject(() => x.App.AdvanceRaceAsync(1, 4));
            Check(Same(pair, x.Transport.Pairs[0]) && x.App.ActiveRace()!.State.CompletedSectors == 3);
            await x.App.LoadAsync(1); await x.App.AdvanceRaceAsync(1, 4);
            Check(x.App.ActiveRace()!.State.CompletedSectors == 4);
        }),
        ("Web: bandeirada só pontua após confirmação e retomada não duplica", async () => {
            var x = await Ready();
            for (var sector = 4; sector <= 35; sector++) await x.App.AdvanceRaceAsync(1, sector);
            x.Transport.Gate = new TaskCompletionSource<bool>(); var finish = x.App.AdvanceRaceAsync(1, 36);
            Check(x.App.History().CompletedRounds == 0 && x.App.History().DriverStandings.All(d => d.Points == 0));
            x.Transport.Gate.SetResult(true); await finish;
            Check(x.App.History().CompletedRounds == 1);
            await x.App.LoadAsync(1); await Reject(() => x.App.AdvanceRaceAsync(1, 36));
            Check(x.App.History().CompletedRounds == 1 && x.App.ActiveRace() == null);
        }),
        ("Web: save sem avançar e consultas não calculam futuro", async () => {
            var x = await Ready(); var before = x.App.ExportCheckpoint(); var observed = Json(x.App.ActiveRace());
            await x.App.SaveAsync();
            Check(before.SequenceEqual(x.App.ExportCheckpoint()) && observed == Json(x.App.ActiveRace()));
            Check(x.App.ActiveRace()!.State.Events.All(e => e.Step <= 3));
            Check(x.App.History().CompletedRounds == 0);
        }),
        ("Web: importação exige consentimento, slot vazio, formato válido e origem intacta", async () => {
            var x = await Ready(); var original = ClientCheckpoint.Encode(New(false));
            var copy = (byte[])original.Clone();
            await Reject(() => x.App.ImportAsync(2, original, false)); Check(!x.Store.Exists(2));
            await x.App.ImportAsync(2, original, true); Check(original.SequenceEqual(copy));
            await Reject(() => x.App.ImportAsync(2, original, true));
            await Reject(() => x.App.ImportAsync(3, new byte[] { 1 }, true)); Check(!x.Store.Exists(3));
            await Reject(() => x.App.ImportAsync(3, null!, true));
            Check(x.Store.Read(2).SequenceEqual(original));
        }),
        ("Web: roundtrip Base64 e exportação não alteram o slot nem o estado", async () => {
            var x = await Ready(); var pair = x.Transport.Pairs[0];
            var bytes = Convert.FromBase64String(Convert.ToBase64String(x.App.ExportCheckpoint()));
            Check(bytes.SequenceEqual(x.Store.Read(1)));
            bytes[0] ^= 1;
            Check(Same(pair, x.Transport.Pairs[0]) && ClientCheckpoint.Decode(x.App.ExportCheckpoint()).GetActiveRace() != null);
        }),
        ("Web: envelope adulterado com hash recomposto não aceita checkpoint inválido", async () => {
            var x = await Ready(); var valid = x.Transport.Pairs[0].A!; var raw = (byte[])valid.Clone();
            raw[^40] ^= 1; SHA256.HashData(raw.AsSpan(0, raw.Length - 32)).CopyTo(raw.AsSpan(raw.Length - 32));
            x.Transport.Pairs[0] = new BrowserSlotPair(raw, null); await x.Store.RefreshAsync();
            await Reject(() => x.App.LoadAsync(1)); Check(x.Store.Exists(1));
            var malformed = (byte[])valid.Clone();
            for (var i = 0; i < 5; i++) malformed[i] = 255;
            SHA256.HashData(malformed.AsSpan(0, malformed.Length - 32)).CopyTo(malformed.AsSpan(malformed.Length - 32));
            x.Transport.Pairs[0] = new BrowserSlotPair(valid, malformed);
            await x.App.LoadAsync(1); Check(x.Store.State == BrowserSaveState.Recovered);
        }),
        ("Web: geração saturada falha sem sobrescrever e guarda proposta", async () => {
            var x = await Ready();
            x.Transport.Pairs[0] = new BrowserSlotPair(Generation(x.Transport.Pairs[0].A!, long.MaxValue), null);
            await x.App.LoadAsync(1); var pair = x.Transport.Pairs[0];
            await Reject(() => x.App.AdvanceRaceAsync(1, 4));
            Check(Same(pair, x.Transport.Pairs[0]) && x.Store.RecoveryCheckpoint != null);
        }),
        ("Web: snapshots do transporte são isolados e slots fora de faixa rejeitados", async () => {
            var x = await Ready(); var pair = await x.Transport.ReadPairAsync(1);
            var bytes = pair.A!; bytes[0] ^= 1;
            Check(!bytes.SequenceEqual(x.Transport.Pairs[0].A!));
            foreach (var slot in new[] { 0, 4 }) {
                await Reject(() => { x.Store.Read(slot); return Task.CompletedTask; });
                await Reject(() => x.Store.WriteAsync(slot, x.App.ExportCheckpoint(), null));
            }
        }),
        ("Web: temporada mista e retomadas mantêm determinismo e histórico oficial", async () => {
            var x = await Ready(); var reference = New(true);
            for (var round = 1; round <= 8; round++) {
                if (round > 1) {
                    reference.PrepareTactical(round, Training.Descanso, Plan());
                    await x.App.PrepareAsync(Training.Descanso, Plan(), true);
                }
                if (round % 2 == 0) { reference.RunNext(round); await x.App.RunAsync(); }
                else {
                    if (round > 1) { reference.StartRace(round); await x.App.StartRaceAsync(round); }
                    while (reference.GetActiveRace() is { } active) {
                        var sector = active.State.CompletedSectors + 1;
                        var pace = sector % 5 == 0 ? Pace.Atacar : (Pace?)null;
                        reference.AdvanceRace(round, sector, 1, pace);
                        await x.App.AdvanceRaceAsync(round, sector, 1, pace);
                        if (sector % 6 == 0) await x.App.LoadAsync(1);
                    }
                }
                Check(ClientCheckpoint.Encode(reference).SequenceEqual(x.App.ExportCheckpoint()));
            }
            Check(x.App.History().Finished && x.App.History().CompletedRounds == 8);
            Console.WriteLine("WEB HEADLESS: temporada/checkpoint SHA-256 " + Convert.ToHexString(SHA256.HashData(x.App.ExportCheckpoint())));
        }),
        ("Web: novo slot não troca o piloto corrente enquanto gravação falha", async () => {
            var x = await Ready(); var before = x.App.ExportCheckpoint();
            x.Transport.Failure = "quota";
            await Reject(() => x.App.CreateAsync(2, 99, "Lia Horizonte", "Brasileira", CareerOrigin.SimRacer, "t01", true));
            Check(x.App.ActiveSlot == 1 && before.SequenceEqual(x.App.ExportCheckpoint()));
            await x.Store.RefreshAsync(); Check(!x.Store.Exists(2));
        }),
        ("Desktop: caminho síncrono preservado e API Async usa o mesmo checkpoint", async () => {
            var dir = Path.Combine(Path.GetTempPath(), "stintoria-web-desktop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                var app = new KartClientController(new DesktopCheckpointStore(dir));
                await app.ImportAsync(1, ClientCheckpoint.Encode(New(true)), true);
                await app.AdvanceRaceAsync(1, 4); var before = app.ExportCheckpoint();
                app.Save(); await app.LoadAsync(1); Check(before.SequenceEqual(app.ExportCheckpoint()));
                app.AdvanceRace(1, 5); Check(app.ActiveRace()!.State.CompletedSectors == 5);
            } finally { Directory.Delete(dir, true); }
        }),
    }.Select(test => (test.Name, (Action)(() => test.Body().GetAwaiter().GetResult())));

    private sealed class MemoryTransport : IBrowserCheckpointTransport
    {
        internal readonly BrowserSlotPair[] Pairs = { new(null, null), new(null, null), new(null, null) };
        internal TaskCompletionSource<bool>? Gate;
        internal string? Failure;
        internal bool ReadFailure, LoseAcknowledgement;
        internal int Writes;
        public Task<BrowserSlotPair> ReadPairAsync(int slot)
        {
            if (ReadFailure) throw new BrowserStorageException("unavailable");
            var pair = Pairs[slot - 1];
            return Task.FromResult(new BrowserSlotPair(pair.A, pair.B, pair.EncodedA, pair.EncodedB));
        }
        public async Task WriteBankAsync(int slot, bool bankA, byte[] record, BrowserSlotPair expected)
        {
            Writes++;
            if (Gate != null) await Gate.Task;
            if (Failure != null) { var code = Failure; Failure = null; throw new BrowserStorageException(code); }
            var i = slot - 1;
            if (!Same(Pairs[i], expected)) throw new BrowserStorageException("conflict");
            Pairs[i] = bankA ? new BrowserSlotPair(record, Pairs[i].B, null, Pairs[i].EncodedB) :
                new BrowserSlotPair(Pairs[i].A, record, Pairs[i].EncodedA, null);
            if (LoseAcknowledgement) { LoseAcknowledgement = false; throw new BrowserStorageException("uncertain"); }
        }
    }
}
