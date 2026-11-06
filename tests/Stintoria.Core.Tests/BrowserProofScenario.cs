using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using Stintoria.ClientBridge;
using Stintoria.Core;

internal static class BrowserProofScenario
{
    private const uint Seed = 42;
    private const string PilotName = "Piloto de Prova";
    private const string Nationality = "Brasil";
    private const string TeamId = "t01";
    private const Pace SelectedPace = Pace.Atacar;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private const string RaceOutputPrefix = "STINTORIA_BROWSER_RACE_JSON=";

    internal static BrowserRaceReply DispatchRaceCommand(BrowserRaceRequest? request)
    {
        try
        {
            if (request == null) throw new ArgumentException("Comando de corrida vazio.");
            switch (request.Operation)
            {
                case "start":
                {
                    var bridge = CareerBridge.Create(
                        Seed, PilotName, Nationality, CareerOrigin.SimRacer, TeamId);
                    bridge.PreparePlan(1, Training.Descanso,
                        new RaceStrategy(Pace.Equilibrado, Pace.Equilibrado, Pace.Equilibrado),
                        tacticalOpponents: false);
                    bridge.StartRace(1);
                    return BuildRaceReply(bridge, 1, null);
                }
                case "state":
                case "advance":
                {
                    var bridge = CareerBridge.LoadCheckpoint(DecodeCheckpoint(request.Checkpoint));
                    var active = bridge.ReadActiveRace();
                    if (request.Operation == "state")
                    {
                        var round = request.ExpectedRound ?? active?.Round ?? bridge.ReadOverview().NextRound - 1;
                        if (active != null && active.Round != round)
                            throw new InvalidOperationException("A etapa informada não corresponde à corrida em andamento.");
                        return BuildRaceReply(bridge, round, active?.State);
                    }

                    if (active == null)
                        throw new InvalidOperationException("Não há corrida em andamento neste checkpoint.");
                    if (request.ExpectedRound != active.Round)
                        throw new InvalidOperationException("A etapa informada não corresponde à corrida em andamento.");
                    if (!request.ExpectedSector.HasValue)
                        throw new ArgumentException("Informe o próximo setor esperado.");
                    if (!Enum.TryParse<Pace>(request.Pace, false, out var pace) ||
                        !Enum.IsDefined(typeof(Pace), pace))
                        throw new ArgumentException("Ritmo inválido. Escolha Conservar, Equilibrado ou Atacar.");

                    var sector = request.ExpectedSector.Value;
                    var state = bridge.AdvanceRaceAndReadState(active.Round, sector, count: 1, pace: pace);
                    return BuildRaceReply(bridge, active.Round, state);
                }
                default:
                    throw new ArgumentException("Comando inválido. Use start, state ou advance.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException ||
            exception is InvalidOperationException || exception is InvalidDataException ||
            exception is FormatException || exception is OverflowException)
        {
            return new BrowserRaceReply { Ok = false, Error = exception.Message };
        }
    }

    internal static int RunRaceCommandCli()
    {
        BrowserRaceReply reply;
        try
        {
            var json = Console.In.ReadToEnd();
            if (Encoding.UTF8.GetByteCount(json) > 131_072)
                throw new ArgumentException("A solicitação excede o tamanho permitido.");
            var request = JsonSerializer.Deserialize<BrowserRaceRequest>(json, WebJson);
            reply = DispatchRaceCommand(request);
        }
        catch (Exception exception) when (exception is JsonException || exception is ArgumentException)
        {
            reply = new BrowserRaceReply { Ok = false, Error = "Solicitação inválida: " + exception.Message };
        }

        Console.WriteLine(RaceOutputPrefix + JsonSerializer.Serialize(reply, WebJson));
        return 0;
    }

    internal static IEnumerable<(string Name, Action Body)> RaceFlowCases() => new (string, Action)[]
    {
        ("Ponte web: iniciar e consultar corrida sem avançar setores", () =>
        {
            var started = DispatchRaceCommand(new BrowserRaceRequest { Operation = "start" });
            Check(started.Ok && started.Race != null && started.Checkpoint != null);
            var startedRace = started.Race ?? throw new Exception("Snapshot inicial ausente.");
            var startedCheckpoint = started.Checkpoint ?? throw new Exception("Checkpoint inicial ausente.");
            Check(startedRace.CompletedSectors == 0 && !startedRace.Finished);
            Check(started.CheckpointFormatVersion == 2);
            Check(startedRace.Classification.Count == 20);
            Check(startedRace.Classification.Count(row => row.IsProtagonist) == 1);
            Check(startedRace.Classification.All(row => row.Points == 0));
            Check(startedRace.Progress.ContractVersion == RaceProgressSnapshot.CurrentContractVersion);
            Check(startedRace.Progress.Identity.Seed == startedRace.Seed &&
                startedRace.Progress.Identity.Round == startedRace.Round);
            Check(startedRace.Progress.Drivers.Count == 20 &&
                startedRace.Progress.Drivers.Select(row => row.DriverId)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .SequenceEqual(startedRace.Classification.Select(row => row.DriverId)
                        .OrderBy(id => id, StringComparer.Ordinal)));
            Check(startedRace.SectorTimings.ContractVersion == RaceSectorTimingSnapshot.CurrentContractVersion);
            Check(startedRace.SectorTimings.LastUpdateSequence == 0 &&
                startedRace.SectorTimings.Drivers.Count == 20 &&
                startedRace.SectorTimings.Drivers.All(row => row.Available && row.Sectors.Count == 0));
            using (var json = JsonDocument.Parse(SerializeRace(startedRace)))
            {
                var progress = json.RootElement.GetProperty("progress");
                Check(progress.GetProperty("contractVersion").GetInt32() == 1);
                Check(progress.GetProperty("drivers").GetArrayLength() == 20);
                Check(progress.GetProperty("identity").GetProperty("id").ValueKind == JsonValueKind.Null);
                Check(progress.GetProperty("drivers")[0].GetProperty("trackProgress")
                    .GetProperty("physicalPositionKnown").GetBoolean() == false);
                Check(progress.GetProperty("drivers")[0].GetProperty("trackProgress")
                    .GetProperty("distanceMeters").ValueKind == JsonValueKind.Null);
                var sectorTimings = json.RootElement.GetProperty("sectorTimings");
                Check(sectorTimings.GetProperty("contractVersion").GetInt32() == 1);
                Check(sectorTimings.GetProperty("drivers").GetArrayLength() == 20);
                Check(sectorTimings.GetProperty("lastUpdateSequence").GetInt32() == 0);
            }

            var queried = DispatchRaceCommand(new BrowserRaceRequest
            {
                Operation = "state", Checkpoint = startedCheckpoint, ExpectedRound = 1
            });
            Check(queried.Ok && SerializeRace(queried.Race) == SerializeRace(startedRace));
            Check(queried.Checkpoint == startedCheckpoint);
        }),
        ("Ponte web: ritmo afeta o setor solicitado e retoma deterministicamente", () =>
        {
            var started = DispatchRaceCommand(new BrowserRaceRequest { Operation = "start" });
            var command = new BrowserRaceRequest
            {
                Operation = "advance", Checkpoint = started.Checkpoint,
                ExpectedRound = 1, ExpectedSector = 1, Pace = "Atacar"
            };
            var advanced = DispatchRaceCommand(command);
            Check(advanced.Ok && advanced.Race?.CompletedSectors == 1);
            Check(advanced.Race!.Classification.Single(row => row.IsProtagonist).Pace == "Atacar");
            Check(advanced.Race.Classification.All(row => row.Points == 0));
            Check(advanced.Race.Events.Any(e => e.Step == 1 && e.DriverId == "jogador"));
            Check(advanced.Race.SectorTimings.CompletedSectors == 1 &&
                advanced.Race.SectorTimings.Drivers.All(row => row.Available && row.Sectors.Count == 1));
            Check(advanced.Race.SectorTimings.Drivers.All(row =>
                row.Sectors[0].UpdateSequence == 1 && row.Sectors[0].Lap == 1 &&
                row.Sectors[0].Sector == 1 && row.Sectors[0].DurationMs > 0));

            var resumed = DispatchRaceCommand(new BrowserRaceRequest
            {
                Operation = "state", Checkpoint = advanced.Checkpoint, ExpectedRound = 1
            });
            Check(resumed.Ok && SerializeRace(resumed.Race) == SerializeRace(advanced.Race));
            var retried = DispatchRaceCommand(command);
            Check(retried.Ok && SerializeRace(retried.Race) == SerializeRace(advanced.Race));
            Check(retried.Checkpoint == advanced.Checkpoint);
        }),
        ("Ponte web: rejeita ritmo e setor obsoletos sem alterar checkpoint", () =>
        {
            var started = DispatchRaceCommand(new BrowserRaceRequest { Operation = "start" });
            var invalidPace = DispatchRaceCommand(new BrowserRaceRequest
            {
                Operation = "advance", Checkpoint = started.Checkpoint,
                ExpectedRound = 1, ExpectedSector = 1, Pace = "Turbo"
            });
            Check(!invalidPace.Ok && invalidPace.Error!.Contains("Ritmo inválido"));
            var staleSector = DispatchRaceCommand(new BrowserRaceRequest
            {
                Operation = "advance", Checkpoint = started.Checkpoint,
                ExpectedRound = 1, ExpectedSector = 2, Pace = "Equilibrado"
            });
            Check(!staleSector.Ok && staleSector.Error!.Contains("Setor"));
            var corrupt = DispatchRaceCommand(new BrowserRaceRequest
            {
                Operation = "state", Checkpoint = "nao-e-base64"
            });
            Check(!corrupt.Ok);
            var unchanged = DispatchRaceCommand(new BrowserRaceRequest
            {
                Operation = "state", Checkpoint = started.Checkpoint, ExpectedRound = 1
            });
            Check(unchanged.Ok && unchanged.Race!.CompletedSectors == 0);
        }),
        ("Ponte web: 36 avanços concluem uma única etapa com pontos oficiais", () =>
        {
            var reply = DispatchRaceCommand(new BrowserRaceRequest { Operation = "start" });
            for (var sector = 1; sector <= 36; sector++)
            {
                reply = DispatchRaceCommand(new BrowserRaceRequest
                {
                    Operation = "advance", Checkpoint = reply.Checkpoint,
                    ExpectedRound = 1, ExpectedSector = sector,
                    Pace = sector % 3 == 0 ? "Atacar" : "Equilibrado"
                });
                Check(reply.Ok && reply.Race?.CompletedSectors == sector);
            }
            var finalRace = reply.Race ?? throw new Exception("Resultado final ausente.");
            var finalCheckpoint = reply.Checkpoint ?? throw new Exception("Checkpoint final ausente.");
            Check(finalRace.Finished && finalRace.CompletedRounds == 1);
            Check(finalRace.Classification.Count == 20);
            Check(finalRace.Classification.Sum(row => row.Points) > 0);
            var duplicate = DispatchRaceCommand(new BrowserRaceRequest
            {
                Operation = "advance", Checkpoint = finalCheckpoint,
                ExpectedRound = 1, ExpectedSector = 37, Pace = "Atacar"
            });
            Check(!duplicate.Ok && duplicate.Race == null);
            var loaded = DispatchRaceCommand(new BrowserRaceRequest
            {
                Operation = "state", Checkpoint = finalCheckpoint, ExpectedRound = 1
            });
            var loadedRace = loaded.Race ?? throw new Exception("Resultado recarregado ausente.");
            Check(loaded.Ok && loadedRace.Finished && loadedRace.CompletedRounds == 1);
            Check(loadedRace.Progress.Finished && loadedRace.Progress.CompletedSectors == 36);
            Check(loadedRace.Classification.Select(row => row.Points)
                .SequenceEqual(finalRace.Classification.Select(row => row.Points)));
        })
    };

    internal static BrowserRaceReply BuildRaceReply(CareerBridge bridge, int round, RaceState? lastState)
    {
        var history = bridge.ReadHistory();
        var active = bridge.ReadActiveRace();
        var completedRound = history.Rounds.FirstOrDefault(item =>
            item.Number == round && item.Status == HistoryRoundStatus.Concluida);
        if (active == null && completedRound == null)
            throw new InvalidOperationException("O checkpoint não contém uma corrida ativa nem um resultado concluído.");

        var state = active?.State ?? lastState;
        var completedSectors = state?.CompletedSectors ?? KartRules.DefaultLaps * 3;
        var isFinished = state?.Finished ?? true;
        var drivers = bridge.ReadDrivers().ToDictionary(driver => driver.Id, driver => driver);
        var rows = completedRound != null
            ? completedRound.Classification.Select(row => new BrowserRaceDriver
            {
                DriverId = row.DriverId, Name = row.DriverName, TeamId = row.TeamId,
                TeamName = row.TeamName, IsProtagonist = row.IsProtagonist,
                Position = row.Position, ElapsedMs = row.ElapsedMs, TireWear = row.TireWear,
                Pace = row.FinalPace.ToString(), Points = row.Points
            }).ToArray()
            : state!.Classification.OrderBy(row => row.Position).Select(row =>
            {
                var driver = drivers[row.DriverId];
                var team = InitialData.Teams.Single(item => item.Id == driver.TeamId);
                return new BrowserRaceDriver
                {
                    DriverId = row.DriverId, Name = driver.Name, TeamId = driver.TeamId,
                    TeamName = team.Name, IsProtagonist = row.DriverId == CareerProfile.PlayerId,
                    Position = row.Position, ElapsedMs = row.ElapsedMs, TireWear = row.TireWear,
                    Pace = row.Pace.ToString(), Points = row.Points
                };
            }).ToArray();
        var events = (state?.Events ?? completedRound!.Events)
            .Where(item => item.Step == completedSectors)
            .Select(item => new BrowserRaceEvent
            {
                Step = item.Step, DriverId = item.DriverId,
                Code = item.Code, Explanation = item.Explanation
            }).ToArray();
        var decisions = (active?.Decisions ?? completedRound!.Decisions)
            .Select(item => new BrowserRaceDecision
            {
                Sector = item.Sector, DriverId = item.DriverId, Pace = item.Pace.ToString()
            }).ToArray();
        var protagonist = rows.Single(row => row.IsProtagonist);
        var snapshot = new BrowserRaceSnapshot
        {
            Engine = "Stintoria.Core via Stintoria.ClientBridge (.NET local)",
            ExecutionMode = "HTTP local, somente prévia de desenvolvimento",
            Seed = state?.Seed ?? completedRound?.RaceSeed ?? bridge.ReadOverview().Seed,
            Round = round, Pilot = protagonist.Name, PlayerDriverId = protagonist.DriverId,
            PlayerTeam = protagonist.TeamName, PlayerPosition = protagonist.Position,
            CompletedSectors = completedSectors,
            NextSector = active?.NextSector ?? 0,
            NextLap = active?.NextLap ?? 0,
            NextCircuitSector = active?.NextCircuitSector ?? 0,
            TotalLaps = state?.TotalLaps ?? KartRules.DefaultLaps,
            Finished = isFinished, CompletedRounds = history.CompletedRounds,
            Weather = state?.Weather.ToString() ?? completedRound?.Weather?.ToString(),
            RandomVersion = state?.RandomVersion ?? SeededRandom.Version,
            RandomState = active?.RandomState,
            ProtagonistPace = protagonist.Pace,
            ProtagonistTireWear = protagonist.TireWear,
            PaceOptions = Enum.GetNames(typeof(Pace)).Cast<string>().ToArray(),
            TireCompound = null,
            Classification = Array.AsReadOnly(rows),
            Events = Array.AsReadOnly(events),
            Decisions = Array.AsReadOnly(decisions),
            Progress = ToBrowserRaceProgress(bridge.ReadProgress(round)),
            SectorTimings = ToBrowserSectorTimings(bridge.ReadSectorTimings(round))
        };
        var checkpoint = bridge.ExportCheckpoint();
        return new BrowserRaceReply
        {
            Ok = true, Race = snapshot,
            Checkpoint = Convert.ToBase64String(checkpoint),
            CheckpointFormatVersion = ClientCheckpoint.GetFormatVersion(checkpoint)
        };
    }

    private static BrowserRaceProgress ToBrowserRaceProgress(RaceProgressSnapshot progress) =>
        new BrowserRaceProgress
        {
            ContractVersion = progress.ContractVersion,
            Identity = new BrowserRaceIdentity
            {
                Id = progress.Identity.Id,
                Seed = progress.Identity.Seed,
                Round = progress.Identity.Round,
                CircuitId = progress.Identity.CircuitId
            },
            CompletedSectors = progress.CompletedSectors,
            TotalSectors = progress.TotalSectors,
            CompletedLaps = progress.CompletedLaps,
            CurrentLap = progress.CurrentLap,
            TotalLaps = progress.TotalLaps,
            Finished = progress.Finished,
            LastConfirmedEventId = progress.LastConfirmedEventId,
            Drivers = Array.AsReadOnly(progress.Drivers.Select(driver => new BrowserDriverProgress
            {
                DriverId = driver.DriverId,
                Name = driver.Name,
                TeamId = driver.TeamId,
                OfficialPosition = driver.OfficialPosition,
                ElapsedMs = driver.ElapsedMs,
                TireWear = driver.TireWear,
                Pace = driver.Pace.ToString(),
                Status = driver.Status.ToString(),
                LastConfirmedEventId = driver.LastConfirmedEventId,
                TrackProgress = new BrowserTrackProgress
                {
                    Kind = driver.TrackProgress.Kind.ToString(),
                    CompletedSectors = driver.TrackProgress.CompletedSectors,
                    CompletedLaps = driver.TrackProgress.CompletedLaps,
                    CurrentLap = driver.TrackProgress.CurrentLap,
                    LastCompletedSectorInLap = driver.TrackProgress.LastCompletedSectorInLap,
                    PhysicalPositionKnown = driver.TrackProgress.PhysicalPositionKnown,
                    NormalizedLapProgress = driver.TrackProgress.NormalizedLapProgress,
                    DistanceMeters = driver.TrackProgress.DistanceMeters
                }
            }).ToArray())
        };

    private static BrowserRaceSectorTimings ToBrowserSectorTimings(RaceSectorTimingSnapshot snapshot) =>
        new BrowserRaceSectorTimings
        {
            ContractVersion = snapshot.ContractVersion,
            Identity = new BrowserRaceIdentity
            {
                Id = snapshot.Identity.Id,
                Seed = snapshot.Identity.Seed,
                Round = snapshot.Identity.Round,
                CircuitId = snapshot.Identity.CircuitId
            },
            SourceSimulationVersion = snapshot.SourceSimulationVersion,
            CompletedSectors = snapshot.CompletedSectors,
            TotalSectors = snapshot.TotalSectors,
            LastUpdateSequence = snapshot.LastUpdateSequence,
            Finished = snapshot.Finished,
            Drivers = Array.AsReadOnly(snapshot.Drivers.Select(driver => new BrowserDriverSectorTiming
            {
                DriverId = driver.DriverId,
                Name = driver.Name,
                TeamId = driver.TeamId,
                Status = driver.Status.ToString(),
                Available = driver.Available,
                Warning = driver.Warning,
                Sectors = Array.AsReadOnly(driver.Sectors.Select(sector => new BrowserRaceSectorTime
                {
                    UpdateSequence = sector.UpdateSequence,
                    Lap = sector.Lap,
                    Sector = sector.Sector,
                    DurationMs = sector.DurationMs,
                    ElapsedMs = sector.ElapsedMs,
                    Pace = sector.Pace.ToString()
                }).ToArray())
            }).ToArray())
        };

    private static byte[] DecodeCheckpoint(string? value)
    {
        var maxBase64Length = ((ClientCheckpoint.MaxBytes + 2) / 3) * 4;
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxBase64Length)
            throw new ArgumentException("Checkpoint ausente ou acima do limite permitido.");
        return Convert.FromBase64String(value);
    }

    private static string SerializeRace(BrowserRaceSnapshot? race) =>
        JsonSerializer.Serialize(race, WebJson);

    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("Invariante da ponte web não atendida.");
    }

    internal static BrowserProofResult Execute()
    {
        var strategy = new RaceStrategy(SelectedPace, SelectedPace, SelectedPace);

        var reference = new CareerSession(
            Seed,
            new CareerProfile(PilotName, Nationality, CareerOrigin.SimRacer, TeamId));
        reference.PrepareStrategy(1, Training.Descanso, strategy);
        reference.StartRace(1);
        reference.AdvanceRace(1, 1, 1, SelectedPace);
        var referenceRace = reference.GetActiveRace()
            ?? throw new InvalidOperationException("A corrida de referência não permaneceu ativa.");

        var bridge = CareerBridge.Create(
            Seed, PilotName, Nationality, CareerOrigin.SimRacer, TeamId);
        bridge.PreparePlan(1, Training.Descanso, strategy, tacticalOpponents: false);
        bridge.StartRace(1);
        bridge.AdvanceRace(1, 1, count: 1, pace: SelectedPace);
        var active = bridge.ReadActiveRace()
            ?? throw new InvalidOperationException("A ponte não retornou a sessão ativa.");

        var referenceSnapshot = CanonicalSnapshot(
            referenceRace.State, referenceRace.RandomState, referenceRace.Decisions);
        var bridgeSnapshot = CanonicalSnapshot(
            active.State, active.RandomState, active.Decisions);
        var referenceHash = HashText(referenceSnapshot);
        var bridgeHash = HashText(bridgeSnapshot);

        var checkpoint = bridge.ExportCheckpoint();
        var resumed = CareerBridge.LoadCheckpoint(checkpoint).ReadActiveRace()
            ?? throw new InvalidOperationException("O checkpoint não retomou a corrida ativa.");
        var resumedHash = HashText(CanonicalSnapshot(
            resumed.State, resumed.RandomState, resumed.Decisions));

        var drivers = active.Drivers.ToDictionary(driver => driver.Id, driver => driver);
        return new BrowserProofResult
        {
            Engine = "Stintoria.Core via Stintoria.ClientBridge (.NET local)",
            ExecutionMode = "HTTP local, somente prévia de desenvolvimento",
            Seed = Seed,
            Round = active.Round,
            Pilot = PilotName,
            SelectedPace = SelectedPace.ToString(),
            Command = "Setor 1: Atacar",
            CompletedSectors = active.State.CompletedSectors,
            TotalLaps = active.State.TotalLaps,
            Finished = active.State.Finished,
            RandomVersion = active.State.RandomVersion,
            RandomState = active.RandomState,
            EventCount = active.State.Events.Count,
            ParityVerified = referenceHash == bridgeHash,
            CheckpointRoundTripVerified = bridgeHash == resumedHash,
            ReferenceSha256 = referenceHash,
            BridgeSha256 = bridgeHash,
            ResumedSha256 = resumedHash,
            CheckpointSha256 = HashBytes(checkpoint),
            Classification = active.State.Classification
                .OrderBy(result => result.Position)
                .Select(result => new BrowserProofDriver
                {
                    DriverId = result.DriverId,
                    Name = drivers[result.DriverId].Name,
                    TeamId = drivers[result.DriverId].TeamId,
                    TeamName = InitialData.Teams.Single(team =>
                        team.Id == drivers[result.DriverId].TeamId).Name,
                    IsProtagonist = result.DriverId == CareerProfile.PlayerId,
                    Position = result.Position,
                    ElapsedMs = result.ElapsedMs,
                    TireWear = result.TireWear,
                    Pace = result.Pace.ToString(),
                    Points = result.Points,
                })
                .ToArray(),
            Events = active.State.Events
                .Select(raceEvent => new BrowserProofEvent
                {
                    Step = raceEvent.Step,
                    DriverId = raceEvent.DriverId,
                    Code = raceEvent.Code,
                    Explanation = raceEvent.Explanation,
                })
                .ToArray(),
        };
    }

    internal static int RunCli()
    {
        try
        {
            Console.WriteLine("STINTORIA_BROWSER_PROOF_JSON=" +
                JsonSerializer.Serialize(Execute(), WebJson));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("STINTORIA_BROWSER_PROOF_ERROR=" +
                exception.GetType().Name + ": " + exception.Message);
            return 1;
        }
    }

    private static string CanonicalSnapshot(
        RaceState state,
        uint randomState,
        IReadOnlyList<SectorDecision> decisions)
    {
        var snapshot = new
        {
            seed = state.Seed,
            randomVersion = state.RandomVersion,
            circuitId = state.CircuitId,
            completedSectors = state.CompletedSectors,
            totalLaps = state.TotalLaps,
            finished = state.Finished,
            weather = state.Weather.ToString(),
            randomState,
            decisions = decisions
                .OrderBy(decision => decision.Sector)
                .ThenBy(decision => decision.DriverId, StringComparer.Ordinal)
                .Select(decision => new
                {
                    sector = decision.Sector,
                    driverId = decision.DriverId,
                    pace = decision.Pace.ToString(),
                })
                .ToArray(),
            events = state.Events.Select(raceEvent => new
            {
                step = raceEvent.Step,
                driverId = raceEvent.DriverId,
                code = raceEvent.Code,
                explanation = raceEvent.Explanation,
            }).ToArray(),
            classification = state.Classification
                .OrderBy(result => result.Position)
                .Select(result => new
                {
                    driverId = result.DriverId,
                    position = result.Position,
                    elapsedMs = result.ElapsedMs,
                    tireWear = result.TireWear,
                    pace = result.Pace.ToString(),
                    points = result.Points,
                })
                .ToArray(),
        };
        return JsonSerializer.Serialize(snapshot, WebJson);
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string HashBytes(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value));
}

internal sealed class BrowserRaceRequest
{
    public string Operation { get; set; } = "";
    public string? Checkpoint { get; set; }
    public int? ExpectedRound { get; set; }
    public int? ExpectedSector { get; set; }
    public string? Pace { get; set; }
}

internal sealed class BrowserRaceReply
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public BrowserRaceSnapshot? Race { get; set; }
    public string? Checkpoint { get; set; }
    public int CheckpointFormatVersion { get; set; }
}

internal sealed class BrowserRaceSnapshot
{
    public string Engine { get; set; } = "";
    public string ExecutionMode { get; set; } = "";
    public uint Seed { get; set; }
    public int Round { get; set; }
    public string Pilot { get; set; } = "";
    public string PlayerDriverId { get; set; } = "";
    public string PlayerTeam { get; set; } = "";
    public int PlayerPosition { get; set; }
    public int CompletedSectors { get; set; }
    public int NextSector { get; set; }
    public int NextLap { get; set; }
    public int NextCircuitSector { get; set; }
    public int TotalLaps { get; set; }
    public bool Finished { get; set; }
    public int CompletedRounds { get; set; }
    public string? Weather { get; set; }
    public string RandomVersion { get; set; } = "";
    public uint? RandomState { get; set; }
    public string ProtagonistPace { get; set; } = "";
    public int ProtagonistTireWear { get; set; }
    public IReadOnlyList<string> PaceOptions { get; set; } = Array.Empty<string>();
    public string? TireCompound { get; set; }
    public IReadOnlyList<BrowserRaceDriver> Classification { get; set; } =
        Array.Empty<BrowserRaceDriver>();
    public IReadOnlyList<BrowserRaceEvent> Events { get; set; } =
        Array.Empty<BrowserRaceEvent>();
    public IReadOnlyList<BrowserRaceDecision> Decisions { get; set; } =
        Array.Empty<BrowserRaceDecision>();
    public BrowserRaceProgress Progress { get; set; } = new BrowserRaceProgress();
    public BrowserRaceSectorTimings SectorTimings { get; set; } = new BrowserRaceSectorTimings();
}

internal sealed class BrowserRaceProgress
{
    public int ContractVersion { get; set; }
    public BrowserRaceIdentity Identity { get; set; } = new BrowserRaceIdentity();
    public int CompletedSectors { get; set; }
    public int TotalSectors { get; set; }
    public int CompletedLaps { get; set; }
    public int CurrentLap { get; set; }
    public int TotalLaps { get; set; }
    public bool Finished { get; set; }
    public string? LastConfirmedEventId { get; set; }
    public IReadOnlyList<BrowserDriverProgress> Drivers { get; set; } =
        Array.Empty<BrowserDriverProgress>();
}

internal sealed class BrowserRaceSectorTimings
{
    public int ContractVersion { get; set; }
    public BrowserRaceIdentity Identity { get; set; } = new BrowserRaceIdentity();
    public string SourceSimulationVersion { get; set; } = "";
    public int CompletedSectors { get; set; }
    public int TotalSectors { get; set; }
    public int LastUpdateSequence { get; set; }
    public bool Finished { get; set; }
    public IReadOnlyList<BrowserDriverSectorTiming> Drivers { get; set; } =
        Array.Empty<BrowserDriverSectorTiming>();
}

internal sealed class BrowserDriverSectorTiming
{
    public string DriverId { get; set; } = "";
    public string Name { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Available { get; set; }
    public string? Warning { get; set; }
    public IReadOnlyList<BrowserRaceSectorTime> Sectors { get; set; } =
        Array.Empty<BrowserRaceSectorTime>();
}

internal sealed class BrowserRaceSectorTime
{
    public int UpdateSequence { get; set; }
    public int Lap { get; set; }
    public int Sector { get; set; }
    public long DurationMs { get; set; }
    public long ElapsedMs { get; set; }
    public string Pace { get; set; } = "";
}

internal sealed class BrowserRaceIdentity
{
    public string? Id { get; set; }
    public uint Seed { get; set; }
    public int Round { get; set; }
    public string CircuitId { get; set; } = "";
}

internal sealed class BrowserDriverProgress
{
    public string DriverId { get; set; } = "";
    public string Name { get; set; } = "";
    public string TeamId { get; set; } = "";
    public int OfficialPosition { get; set; }
    public long ElapsedMs { get; set; }
    public int TireWear { get; set; }
    public string Pace { get; set; } = "";
    public string Status { get; set; } = "";
    public string? LastConfirmedEventId { get; set; }
    public BrowserTrackProgress TrackProgress { get; set; } = new BrowserTrackProgress();
}

internal sealed class BrowserTrackProgress
{
    public string Kind { get; set; } = "";
    public int CompletedSectors { get; set; }
    public int CompletedLaps { get; set; }
    public int CurrentLap { get; set; }
    public int? LastCompletedSectorInLap { get; set; }
    public bool PhysicalPositionKnown { get; set; }
    public double? NormalizedLapProgress { get; set; }
    public double? DistanceMeters { get; set; }
}

internal sealed class BrowserRaceDriver
{
    public string DriverId { get; set; } = "";
    public string Name { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string TeamName { get; set; } = "";
    public bool IsProtagonist { get; set; }
    public int Position { get; set; }
    public long ElapsedMs { get; set; }
    public int TireWear { get; set; }
    public string Pace { get; set; } = "";
    public int Points { get; set; }
}

internal sealed class BrowserRaceEvent
{
    public int Step { get; set; }
    public string DriverId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Explanation { get; set; } = "";
}

internal sealed class BrowserRaceDecision
{
    public int Sector { get; set; }
    public string DriverId { get; set; } = "";
    public string Pace { get; set; } = "";
}

internal sealed class BrowserProofResult
{
    public string Engine { get; set; } = "";
    public string ExecutionMode { get; set; } = "";
    public uint Seed { get; set; }
    public int Round { get; set; }
    public string Pilot { get; set; } = "";
    public string SelectedPace { get; set; } = "";
    public string Command { get; set; } = "";
    public int CompletedSectors { get; set; }
    public int TotalLaps { get; set; }
    public bool Finished { get; set; }
    public string RandomVersion { get; set; } = "";
    public uint RandomState { get; set; }
    public int EventCount { get; set; }
    public bool ParityVerified { get; set; }
    public bool CheckpointRoundTripVerified { get; set; }
    public string ReferenceSha256 { get; set; } = "";
    public string BridgeSha256 { get; set; } = "";
    public string ResumedSha256 { get; set; } = "";
    public string CheckpointSha256 { get; set; } = "";
    public IReadOnlyList<BrowserProofDriver> Classification { get; set; } =
        Array.Empty<BrowserProofDriver>();
    public IReadOnlyList<BrowserProofEvent> Events { get; set; } =
        Array.Empty<BrowserProofEvent>();
}

internal sealed class BrowserProofDriver
{
    public string DriverId { get; set; } = "";
    public string Name { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string TeamName { get; set; } = "";
    public bool IsProtagonist { get; set; }
    public int Position { get; set; }
    public long ElapsedMs { get; set; }
    public int TireWear { get; set; }
    public string Pace { get; set; } = "";
    public int Points { get; set; }
}

internal sealed class BrowserProofEvent
{
    public int Step { get; set; }
    public string DriverId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Explanation { get; set; } = "";
}
