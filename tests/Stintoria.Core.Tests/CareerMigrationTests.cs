using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stintoria.ClientBridge;
using Stintoria.Core;
using Stintoria.Local;

internal static class CareerMigrationTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[] {
        ("Migração: v1/v2/v3 preservam JSON, identidade, versões, histórico e escolhas pendentes", () => Folder(dir => {
            foreach (var mode in new[] {1, 2, 3}) {
                var career = New();
                Prepare(career, 1, mode); career.RunNext(1); Prepare(career, 2, mode);
                var path = Write(dir, $"v{mode}.json", career);
                var before = File.ReadAllBytes(path); var slots = Path.Combine(dir, "slots-" + mode);
                var migrator = new TerminalCareerMigration(); var preview = migrator.Preview(path, slots, mode);
                Check(!Directory.Exists(slots) && !preview.DestinationOccupied);
                Check(preview.SourceFormat == "stintoria-carreira-v" + mode);
                Check(preview.CompletedRounds == 1 && preview.HasPendingPreparation && preview.Profile.Age == 14);
                Check(preview.Profile.DisplacedDriverId == career.Profile.DisplacedDriverId && preview.Profile.Name == career.Profile.Name);
                Check(preview.SourceSha256 == Convert.ToHexString(SHA256.HashData(before)));
                Check(preview.SimulationVersion == RaceState.SimulationVersion && preview.RandomVersion == SeededRandom.Version);
                migrator.Confirm(preview, true);
                var app = new KartClientController(new DesktopCheckpointStore(slots)); app.Load(mode);
                Check(Json(app.History()) == Json(HistoryQuery.ForCareer(career)));
                var restored = ClientCheckpoint.Decode(File.ReadAllBytes(preview.DestinationPath));
                Check(CareerArchive.Encode(restored) == CareerArchive.Encode(career));
                Check(ClientCheckpoint.Encode(restored).SequenceEqual(ClientCheckpoint.Encode(career)));
                app.Run(); career.RunNext(2);
                Check(Json(app.History()) == Json(HistoryQuery.ForCareer(career)));
                Check(File.ReadAllBytes(path).SequenceEqual(before));
            }
        })),
        ("Migração: prévia/cancelamento não criam pasta, checkpoint nem lock do JSON", () => Folder(dir => {
            var path = Write(dir, "origem.json", New()); var before = File.ReadAllBytes(path);
            var slots = Path.Combine(dir, "ainda-nao-existe"); var service = new TerminalCareerMigration();
            var p = service.Preview(path, slots, 1);
            Throws<OperationCanceledException>(() => service.Confirm(p, false));
            Check(!Directory.Exists(slots) && !File.Exists(path + ".lock") && File.ReadAllBytes(path).SequenceEqual(before));
        })),
        ("Migração: alteração do JSON após prévia exige novo consentimento", () => Folder(dir => {
            var path = Write(dir, "origem.json", New()); var slots = Path.Combine(dir, "slots");
            var service = new TerminalCareerMigration(); var p = service.Preview(path, slots, 1);
            File.AppendAllText(path, "\n"); var changed = File.ReadAllBytes(path);
            Throws<InvalidOperationException>(() => service.Confirm(p, true));
            Check(!Directory.Exists(slots) && File.ReadAllBytes(path).SequenceEqual(changed));
            service.Confirm(service.Preview(path, slots, 1), true);
            Check(File.ReadAllBytes(path).SequenceEqual(changed));
        })),
        ("Migração: slot ocupado, importação repetida e conflito após prévia nunca sobrescrevem", () => Folder(dir => {
            var path = Write(dir, "origem.json", New()); var slots = Path.Combine(dir, "slots");
            var service = new TerminalCareerMigration(); var p = service.Preview(path, slots, 1);
            service.Confirm(p, true); var first = File.ReadAllBytes(p.DestinationPath);
            Throws<InvalidOperationException>(() => service.Confirm(p, true));
            var occupied = service.Preview(path, slots, 1); Check(occupied.DestinationOccupied);
            Throws<InvalidOperationException>(() => service.Confirm(occupied, true));
            Check(File.ReadAllBytes(p.DestinationPath).SequenceEqual(first));
            var second = service.Preview(path, slots, 2);
            File.WriteAllText(second.DestinationPath, "ocupado e inválido");
            Throws<InvalidOperationException>(() => service.Confirm(second, true));
            Check(File.ReadAllText(second.DestinationPath) == "ocupado e inválido");
            // Repetir para OUTRO slot vazio é uma cópia explícita, não renomeia o piloto.
            var third = service.Preview(path, slots, 3); service.Confirm(third, true);
            Check(File.ReadAllBytes(third.DestinationPath).SequenceEqual(first));
        })),
        ("Migração: entradas, origem igual ao destino e diretório como slot são recusados", () => Folder(dir => {
            var path = Write(dir, "origem.json", New()); var service = new TerminalCareerMigration();
            Throws<ArgumentException>(() => service.Preview("", dir, 1));
            Throws<ArgumentException>(() => service.Preview(path, " ", 1));
            Throws<ArgumentOutOfRangeException>(() => service.Preview(path, dir, 0));
            Throws<ArgumentOutOfRangeException>(() => service.Preview(path, dir, 4));
            Throws<FileNotFoundException>(() => service.Preview(Path.Combine(dir, "ausente.json"), dir, 1));
            var same = Write(dir, "carreira-1.stint", New());
            Throws<ArgumentException>(() => service.Preview(same, dir, 1));
            Directory.CreateDirectory(Path.Combine(dir, "carreira-2.stint"));
            var p = service.Preview(path, dir, 2); Check(p.DestinationOccupied);
            Throws<InvalidOperationException>(() => service.Confirm(p, true));
        })),
        ("Migração: corrupção, checksum recomposto, versão desconhecida e tamanho são rejeitados", () => Folder(dir => {
            var career = New(); Prepare(career, 1, 3); career.RunNext(1);
            var text = CareerArchive.Encode(career);
            var envelope = JsonNode.Parse(text)!.AsObject(); var payload = JsonNode.Parse(envelope["Payload"]!.GetValue<string>())!.AsObject();
            payload["Simulation"] = "desconhecida";
            envelope["Payload"] = payload.ToJsonString(); envelope["Sha256"] = Hash(payload.ToJsonString());
            var seedEnvelope = JsonNode.Parse(text)!.AsObject();
            var altered = JsonNode.Parse(seedEnvelope["Payload"]!.GetValue<string>())!.AsObject();
            altered["Seed"] = 123;
            seedEnvelope["Payload"] = altered.ToJsonString(); seedEnvelope["Sha256"] = Hash(altered.ToJsonString());
            var invalids = new[] {"{", text[..^12], text.Replace("Ari Horizonte", "Ari Alterado"), envelope.ToJsonString(), seedEnvelope.ToJsonString()};
            var service = new TerminalCareerMigration();
            foreach (var invalid in invalids) {
                var source = Path.Combine(dir, "invalido.json"); File.WriteAllText(source, invalid);
                var bytes = File.ReadAllBytes(source);
                Throws<InvalidDataException>(() => service.Preview(source, Path.Combine(dir, "slots"), 1));
                Check(File.ReadAllBytes(source).SequenceEqual(bytes) && !Directory.Exists(Path.Combine(dir, "slots")));
            }
            var large = Path.Combine(dir, "grande.json");
            using (var file = File.Create(large)) file.SetLength(ChampionshipArchive.MaxBytes + 1L);
            Throws<InvalidDataException>(() => service.Preview(large, dir, 1));
        })),
        ("Migração: BOM/UTF-8 válido é preservado; codificação inválida não é convertida", () => Folder(dir => {
            var text = CareerArchive.Encode(New()); var source = Path.Combine(dir, "bom.json");
            File.WriteAllBytes(source, new byte[] {0xef, 0xbb, 0xbf}.Concat(Encoding.UTF8.GetBytes(text)).ToArray());
            var before = File.ReadAllBytes(source); var service = new TerminalCareerMigration();
            var p = service.Preview(source, Path.Combine(dir, "slots"), 1); service.Confirm(p, true);
            Check(File.ReadAllBytes(source).SequenceEqual(before));
            File.WriteAllBytes(Path.Combine(dir, "bytes.json"), new byte[] {0xff, 0xfe, 0xff});
            Throws<DecoderFallbackException>(() => service.Preview(Path.Combine(dir, "bytes.json"), dir, 1));
        })),
        ("Migração: origem marcada somente-leitura é aceita e permanece intacta", () => Folder(dir => {
            var source = Write(dir, "somente-leitura.json", New()); var before = File.ReadAllBytes(source);
            var attributes = File.GetAttributes(source);
            try {
                File.SetAttributes(source, attributes | FileAttributes.ReadOnly);
                var service = new TerminalCareerMigration(); var p = service.Preview(source, Path.Combine(dir, "slots"), 1);
                service.Confirm(p, true);
                Check(File.ReadAllBytes(source).SequenceEqual(before));
                Check(File.GetAttributes(source).HasFlag(FileAttributes.ReadOnly));
            }
            finally { File.SetAttributes(source, attributes); }
        })),
        ("Migração: lock real de destino impede publicação e permite nova tentativa", () => Folder(dir => {
            var source = Write(dir, "origem.json", New()); var slots = Path.Combine(dir, "slots");
            var service = new TerminalCareerMigration(); var p = service.Preview(source, slots, 1);
            Directory.CreateDirectory(slots); var before = File.ReadAllBytes(source);
            using (File.Open(p.DestinationPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Throws<IOException>(() => service.Confirm(p, true));
            Check(!File.Exists(p.DestinationPath) && File.ReadAllBytes(source).SequenceEqual(before));
            service.Confirm(p, true);
            new KartClientController(new DesktopCheckpointStore(slots)).Load(1);
        })),
        ("Migração: falha injetada de quota/escrita antes da publicação é recuperável", () => Folder(dir => {
            var path = Write(dir, "origem.json", New()); var slots = Path.Combine(dir, "slots");
            var fault = new FaultStore(slots); var service = new TerminalCareerMigration(_ => fault);
            var p = service.Preview(path, slots, 1); var before = File.ReadAllBytes(path);
            Throws<IOException>(() => service.Confirm(p, true));
            Check(!File.Exists(p.DestinationPath) && File.ReadAllBytes(path).SequenceEqual(before));
            Check(File.Exists(Path.Combine(slots, "interrompida.tmp")));
            fault.Fail = false; service.Confirm(p, true);
            var app = new KartClientController(new DesktopCheckpointStore(slots)); app.Load(1);
            Check(app.Overview().Profile.Name == "Ari Horizonte" && app.History().CompletedRounds == 0);
        })),
        ("Migração: permissão real de pasta e destino inválido retornam erro sem perder origem", () => Folder(dir => {
            var source = Write(dir, "origem.json", New()); var service = new TerminalCareerMigration();
            var blocked = Path.Combine(dir, "arquivo-nao-pasta"); File.WriteAllText(blocked, "preservar");
            var bad = service.Preview(source, blocked, 1);
            Throws<IOException>(() => service.Confirm(bad, true)); Check(File.ReadAllText(blocked) == "preservar");
            if (OperatingSystem.IsLinux() && Environment.UserName != "root") {
                var slots = Path.Combine(dir, "sem-permissao"); Directory.CreateDirectory(slots);
                var p = service.Preview(source, slots, 1); var old = File.GetUnixFileMode(slots);
                try {
                    File.SetUnixFileMode(slots, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                    Throws<UnauthorizedAccessException>(() => service.Confirm(p, true));
                    Check(!File.Exists(p.DestinationPath));
                }
                finally { File.SetUnixFileMode(slots, old); }
                service.Confirm(p, true);
                Console.WriteLine("PERMISSÃO: bloqueio Linux real verificado e importação retomada.");
            }
            else Console.WriteLine("PERMISSÃO: cenário Unix não aplicável; não alegar teste real de bloqueio.");
        })),
        ("Migração: oito etapas completas, tabelas e relatórios coincidem após carregar no controlador", () => Folder(dir => {
            var career = New();
            for (var r = 1; r <= 8; r++) { Prepare(career, r, r < 3 ? r : 3); career.RunNext(r); }
            var source = Write(dir, "temporada.json", career); var before = File.ReadAllBytes(source);
            var migration = new TerminalCareerMigration(); var p = migration.Preview(source, Path.Combine(dir, "slots"), 1);
            Check(p.Finished && p.CompletedRounds == 8 && !p.HasPendingPreparation);
            migration.Confirm(p, true);
            var app = new KartClientController(new DesktopCheckpointStore(p.SlotsDirectory)); app.Load(1);
            Check(Json(app.History()) == Json(HistoryQuery.ForCareer(career)));
            for (var round = 1; round <= 8; round++)
                Check(Json(app.Report(round, CareerProfile.PlayerId)) ==
                    Json(SegmentReportQuery.ForRound(HistoryQuery.ForCareer(career).GetRound(round), CareerProfile.PlayerId)));
            Throws<InvalidOperationException>(() => app.Run());
            Check(File.ReadAllBytes(source).SequenceEqual(before));
            Console.WriteLine("MIGRAÇÃO: 8 etapas, JSON preservado e checkpoint SHA-256 " +
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.DestinationPath))));
        }))
    };
    private sealed class FaultStore : IClientCheckpointStore
    {
        private readonly string directory; public bool Fail = true;
        internal FaultStore(string directory) { this.directory = directory; }
        public bool Exists(int slot) => new DesktopCheckpointStore(directory).Exists(slot);
        public byte[] Read(int slot) => new DesktopCheckpointStore(directory).Read(slot);
        public void Write(int slot, byte[] bytes, byte[]? expected) {
            if (Fail) {
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "interrompida.tmp"), bytes.Take(11).ToArray());
                throw new IOException("Falha injetada de quota/flush; não é quota real esgotada.");
            }
            new DesktopCheckpointStore(directory).Write(slot, bytes, expected);
        }
    }
    private static CareerSession New() => new(42, new CareerProfile("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02"));
    private static void Prepare(CareerSession c, int r, int mode) {
        if (mode == 1) c.Prepare(r, Training.Descanso, Pace.Atacar);
        else {
            var plan = new RaceStrategy(Pace.Conservar, Pace.Equilibrado, Pace.Atacar, 8, 29);
            if (mode == 2) c.PrepareStrategy(r, Training.Descanso, plan); else c.PrepareTactical(r, Training.Descanso, plan);
        }
    }
    private static string Write(string dir, string name, CareerSession c) {
        var path = Path.Combine(dir, name); File.WriteAllText(path, CareerArchive.Encode(c), new UTF8Encoding(false)); return path;
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static void Folder(Action<string> action) {
        var dir = Path.Combine(Path.GetTempPath(), "stintoria-migrate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir); try { action(dir); } finally { Directory.Delete(dir, true); }
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Invariante da migração não atendida."); }
    private static void Throws<T>(Action action) where T : Exception {
        try { action(); } catch (T) { return; } throw new Exception("Esperava " + typeof(T).Name);
    }
}
