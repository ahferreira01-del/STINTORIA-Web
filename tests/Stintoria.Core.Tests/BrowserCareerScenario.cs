using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Stintoria.ClientBridge;
using Stintoria.Core;

internal static class BrowserCareerScenario
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private const string OutputPrefix = "STINTORIA_BROWSER_CAREER_JSON=";

    internal static BrowserCareerReply Dispatch(BrowserCareerRequest? request)
    {
        try
        {
            if (request == null) throw new ArgumentException("Solicitação de carreira vazia.");
            switch (request.Operation)
            {
                case "catalog":
                    return CatalogReply();
                case "create":
                {
                    if (!request.Seed.HasValue) throw new ArgumentException("Informe a seed da carreira.");
                    if (!Enum.TryParse<CareerOrigin>(request.Origin, false, out var origin) ||
                        !Enum.IsDefined(typeof(CareerOrigin), origin))
                        throw new ArgumentException("Escolha uma origem disponível no motor C#.");
                    var bridge = CareerJourneyBridge.Create(request.Seed.Value,
                        request.Name ?? "", request.Nationality ?? "", origin, request.TeamId ?? "");
                    return BuildReply(bridge);
                }
                case "load":
                {
                    var bridge = CareerJourneyBridge.LoadCheckpoint(DecodeCheckpoint(request.Checkpoint));
                    return BuildReply(bridge);
                }
                case "prepare":
                {
                    if (!request.ExpectedRound.HasValue)
                        throw new ArgumentException("Informe a próxima etapa para confirmar a preparação.");
                    if (!Enum.TryParse<Training>(request.Training, false, out var training) ||
                        !Enum.IsDefined(typeof(Training), training))
                        throw new ArgumentException("Escolha uma preparação disponível no motor C#.");
                    if (!Enum.TryParse<Pace>(request.Pace, false, out var pace) ||
                        !Enum.IsDefined(typeof(Pace), pace))
                        throw new ArgumentException("Escolha um ritmo de largada disponível no motor C#.");
                    var bridge = CareerJourneyBridge.LoadCheckpoint(DecodeCheckpoint(request.Checkpoint));
                    bridge.PrepareSingle(request.ExpectedRound.Value, training, pace);
                    return BuildReply(bridge);
                }
                case "startRace":
                {
                    if (!request.ExpectedRound.HasValue)
                        throw new ArgumentException("Informe a etapa preparada para iniciar a corrida.");
                    var bridge = CareerJourneyBridge.LoadCheckpoint(DecodeCheckpoint(request.Checkpoint));
                    bridge.StartRace(request.ExpectedRound.Value);
                    return BuildReply(bridge);
                }
                case "advanceRace":
                {
                    if (!request.ExpectedRound.HasValue || !request.ExpectedSector.HasValue)
                        throw new ArgumentException("Informe a etapa e o próximo setor confirmado.");
                    if (!Enum.TryParse<Pace>(request.Pace, false, out var pace) ||
                        !Enum.IsDefined(typeof(Pace), pace))
                        throw new ArgumentException("Escolha um ritmo disponível no motor C#.");
                    var bridge = CareerJourneyBridge.LoadCheckpoint(DecodeCheckpoint(request.Checkpoint));
                    var state = bridge.AdvanceRaceAndReadState(
                        request.ExpectedRound.Value,
                        request.ExpectedSector.Value,
                        count: 1,
                        pace: pace);
                    return BuildReply(bridge, state);
                }
                case "acceptPromotion":
                {
                    var bridge = CareerJourneyBridge.LoadCheckpoint(DecodeCheckpoint(request.Checkpoint));
                    bridge.AcceptPromotion(request.TeamId ?? "");
                    return BuildReply(bridge);
                }
                case "declinePromotion":
                {
                    var bridge = CareerJourneyBridge.LoadCheckpoint(DecodeCheckpoint(request.Checkpoint));
                    bridge.DeclinePromotion();
                    return BuildReply(bridge);
                }
                case "startFormula4Season":
                {
                    var bridge = CareerJourneyBridge.LoadCheckpoint(DecodeCheckpoint(request.Checkpoint));
                    bridge.StartFormula4Season();
                    return BuildReply(bridge);
                }
                case "prepareFormula4Race":
                {
                    if (!request.ExpectedRound.HasValue)
                        throw new ArgumentException("Informe a etapa F4 para preparar.");
                    var bridge = CareerJourneyBridge.LoadCheckpoint(DecodeCheckpoint(request.Checkpoint));
                    var strategy = new Formula4Strategy(
                        ParsePace(request.BeginningPace, "ritmo inicial"),
                        ParsePace(request.MiddlePace, "ritmo intermediário"),
                        ParsePace(request.FinalPace, "ritmo final"),
                        request.MiddleStartSector ?? 16,
                        request.FinalStartSector ?? 31);
                    bridge.PrepareFormula4Race(request.ExpectedRound.Value, strategy);
                    return BuildReply(bridge);
                }
                case "startFormula4Race":
                {
                    if (!request.ExpectedRound.HasValue)
                        throw new ArgumentException("Informe a etapa F4 para iniciar.");
                    var bridge = CareerJourneyBridge.LoadCheckpoint(DecodeCheckpoint(request.Checkpoint));
                    bridge.StartFormula4Race(request.ExpectedRound.Value);
                    return BuildReply(bridge);
                }
                case "advanceFormula4Race":
                {
                    if (!request.ExpectedRound.HasValue || !request.ExpectedSector.HasValue)
                        throw new ArgumentException("Informe a etapa e o próximo setor F4.");
                    var bridge = CareerJourneyBridge.LoadCheckpoint(DecodeCheckpoint(request.Checkpoint));
                    var pace = request.Pace == null ? (Pace?)null : ParsePace(request.Pace, "ritmo F4");
                    bridge.AdvanceFormula4Race(request.ExpectedRound.Value, request.ExpectedSector.Value,
                        request.Count ?? 1, pace);
                    return BuildReply(bridge);
                }
                default:
                    throw new ArgumentException("Comando inválido. Use catalog, create, load, prepare, startRace, advanceRace, acceptPromotion, declinePromotion ou os comandos Formula4.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException ||
            exception is InvalidOperationException || exception is InvalidDataException ||
            exception is FormatException || exception is OverflowException)
        {
            return new BrowserCareerReply { Ok = false, Error = exception.Message };
        }
    }

    private static Pace ParsePace(string? value, string label)
    {
        if (!Enum.TryParse<Pace>(value, false, out var pace) || !Enum.IsDefined(typeof(Pace), pace))
            throw new ArgumentException("Escolha um " + label + " F4 válido.");
        return pace;
    }

    internal static int RunCli()
    {
        BrowserCareerReply reply;
        try
        {
            var json = Console.In.ReadToEnd();
            if (Encoding.UTF8.GetByteCount(json) > 131_072)
                throw new ArgumentException("A solicitação excede o tamanho permitido.");
            reply = Dispatch(JsonSerializer.Deserialize<BrowserCareerRequest>(json, WebJson));
        }
        catch (Exception exception) when (exception is JsonException || exception is ArgumentException)
        {
            reply = new BrowserCareerReply { Ok = false, Error = "Solicitação inválida: " + exception.Message };
        }

        Console.WriteLine(OutputPrefix + JsonSerializer.Serialize(reply, WebJson));
        return 0;
    }

    internal static int RunFirstRoundFixtureCli()
    {
        var current = Prepare(Create(), nameof(Training.Descanso), nameof(Pace.Equilibrado));
        if (!current.Ok) throw new InvalidOperationException(current.Error ?? "Falha ao preparar fixture C#.");
        current = StartRace(current);
        if (!current.Ok) throw new InvalidOperationException(current.Error ?? "Falha ao iniciar fixture C#.");
        for (var sector = 1; sector <= KartRules.DefaultLaps * 3; sector++)
        {
            current = AdvanceRace(current, sector, nameof(Pace.Equilibrado));
            if (!current.Ok)
                throw new InvalidOperationException(current.Error ?? $"Falha no setor {sector} da fixture C#.");
        }
        if (current.Race?.Finished != true || current.Career?.CompletedRounds != 1 ||
            current.Career.NextRound != 2 || current.Race.Classification.Count != 20 ||
            current.CheckpointFormatVersion != 2)
            throw new InvalidOperationException("Fixture C# da etapa 1 não chegou à bandeirada oficial.");
        Console.WriteLine(OutputPrefix + JsonSerializer.Serialize(current, WebJson));
        return 0;
    }

    internal static (string Name, Action Body)[] TestCases() => new (string, Action)[]
    {
        ("Ponte web de carreira: catálogo C# entrega equipes e calendário oficiais", () =>
        {
            var reply = Dispatch(new BrowserCareerRequest { Operation = "catalog" });
            Check(reply.Ok && reply.Catalog != null);
            Check(reply.Catalog!.Teams.Length == 10);
            Check(reply.Catalog.Teams.Select(item => item.Id)
                .SequenceEqual(InitialData.Teams.Select(item => item.Id)));
            Check(reply.Catalog.Origins.SequenceEqual(Enum.GetNames<CareerOrigin>()));
            Check(reply.Catalog.Calendar.Length == 8);
            Check(reply.Catalog.Calendar.Select(item => item.Number)
                .SequenceEqual(Enumerable.Range(1, 8)));
            Check(reply.Catalog.Calendar.Select(item => item.CircuitId)
                .SequenceEqual(InitialData.KartCalendar.Select(item => item.CircuitId)));
        }),
        ("Ponte web de carreira: criação retorna piloto e temporada do C# sem resultados inventados", () =>
        {
            var reply = Create();
            Check(reply.Ok && reply.Career != null && reply.Checkpoint != null);
            var career = reply.Career!;
            Check(career.Profile.Name == "Marina Costa" && career.Profile.Age == 14);
            Check(career.Profile.Origin == nameof(CareerOrigin.LigadaAoEsporte));
            Check(career.Profile.TeamId == "t03" && career.Profile.TeamName == "Vértice Racing");
            Check(career.CompletedRounds == 0 && career.NextRound == 1 && !career.Finished);
            Check(career.Calendar.Length == 8 && career.Calendar.All(round => round.Status == "NaoDisputada"));
            Check(career.Calendar.All(round => round.Classification.Length == 0));
            Check(career.DriverStandings.Length == 0 && career.TeamStandings.Length == 0);
            Check(reply.CheckpointFormatVersion == 1);
        }),
        ("Ponte web de carreira: entradas inválidas e equipe inelegível são rejeitadas", () =>
        {
            Check(!Dispatch(new BrowserCareerRequest
            {
                Operation = "create", Seed = 42, Name = "M", Nationality = "Brasil",
                Origin = nameof(CareerOrigin.SimRacer), TeamId = "t01"
            }).Ok);
            Check(!Dispatch(new BrowserCareerRequest
            {
                Operation = "create", Seed = 42, Name = "Caio Valença", Nationality = "Brasil",
                Origin = nameof(CareerOrigin.SimRacer), TeamId = "t01"
            }).Ok);
            Check(!Dispatch(new BrowserCareerRequest
            {
                Operation = "create", Seed = 42, Name = "Nome Válido", Nationality = "Brasil",
                Origin = "OrigemInventada", TeamId = "t01"
            }).Ok);
            Check(!Dispatch(new BrowserCareerRequest
            {
                Operation = "create", Seed = 42, Name = "Nome Válido", Nationality = "Brasil",
                Origin = nameof(CareerOrigin.SimRacer), TeamId = "fora-do-kart"
            }).Ok);
        }),
        ("Ponte web de carreira: checkpoint retoma perfil, calendário e histórico sem mudar bytes", () =>
        {
            var created = Create();
            var checkpoint = created.Checkpoint ?? throw new Exception("Checkpoint C# ausente.");
            var loaded = Dispatch(new BrowserCareerRequest { Operation = "load", Checkpoint = checkpoint });
            Check(loaded.Ok && loaded.Career != null);
            Check(loaded.Checkpoint == checkpoint);
            Check(loaded.CheckpointFormatVersion == created.CheckpointFormatVersion);
            Check(JsonSerializer.Serialize(loaded.Career, WebJson) ==
                JsonSerializer.Serialize(created.Career, WebJson));
            Check(JsonSerializer.Serialize(loaded.Career!.Calendar, WebJson) ==
                JsonSerializer.Serialize(created.Career!.Calendar, WebJson));
        }),
        ("Ponte web de carreira: opções C# mostram custo e consequência sem alterar o checkpoint", () =>
        {
            var created = Create();
            var checkpoint = created.Checkpoint ?? throw new Exception("Checkpoint C# ausente.");
            var loaded = Dispatch(new BrowserCareerRequest { Operation = "load", Checkpoint = checkpoint });
            Check(loaded.Ok && loaded.Career != null && loaded.Checkpoint == checkpoint);
            var career = loaded.Career!;
            Check(career.PaceOptions.SequenceEqual(Enum.GetNames(typeof(Pace))));
            Check(career.PreparationOptions.Select(option => option.Training)
                .SequenceEqual(Enum.GetNames(typeof(Training))));
            var rest = career.PreparationOptions.Single(option => option.Training == nameof(Training.Descanso));
            var technical = career.PreparationOptions.Single(option => option.Training == nameof(Training.Tecnico));
            Check(rest.Available && rest.CreditCost == 0 && rest.SkillDelta == 0 && rest.UnavailableReason == null);
            Check(technical.Available && technical.CreditCost == 1 && technical.SkillDelta == 2 &&
                technical.UnavailableReason == null);
            Check(!career.Prepared && career.CurrentPreparation == null);
            Check(loaded.Checkpoint == created.Checkpoint);
        }),
        ("Ponte web de carreira: descanso é confirmado pelo C# e retomado no checkpoint", () =>
        {
            var created = Create();
            var prepared = Prepare(created, nameof(Training.Descanso), nameof(Pace.Conservar));
            Check(prepared.Ok && prepared.Career != null && prepared.Checkpoint != null);
            var career = prepared.Career!;
            Check(career.Prepared && career.Skill == 70 && career.Credits == CareerSession.InitialCredits);
            Check(career.Calendar[0].Status == "Preparada" && career.Calendar[0].Classification.Length == 0);
            Check(career.CurrentPreparation?.Round == 1 &&
                career.CurrentPreparation.Training == nameof(Training.Descanso) &&
                career.CurrentPreparation.Pace == nameof(Pace.Conservar) &&
                career.CurrentPreparation.SkillApplied == 70 &&
                career.CurrentPreparation.CreditsRemaining == CareerSession.InitialCredits);
            Check(career.PreparationOptions.All(option => !option.Available));
            var restored = Dispatch(new BrowserCareerRequest
            {
                Operation = "load",
                Checkpoint = prepared.Checkpoint
            });
            Check(restored.Ok && restored.Career != null && restored.Checkpoint == prepared.Checkpoint);
            Check(JsonSerializer.Serialize(restored.Career, WebJson) == JsonSerializer.Serialize(career, WebJson));
            Check(!Prepare(prepared, nameof(Training.Tecnico), nameof(Pace.Atacar)).Ok);
            var demo = BrowserProofScenario.DispatchRaceCommand(new BrowserRaceRequest { Operation = "start" });
            Check(demo.Ok && demo.Race?.Pilot == "Piloto de Prova");
        }),
        ("Ponte web de carreira: treino técnico gasta um crédito e persiste habilidade oficial", () =>
        {
            var created = Create();
            var prepared = Prepare(created, nameof(Training.Tecnico), nameof(Pace.Equilibrado));
            Check(prepared.Ok && prepared.Career != null && prepared.Checkpoint != null);
            Check(prepared.Career!.Skill == CareerSession.InitialSkill + 2);
            Check(prepared.Career.Credits == CareerSession.InitialCredits - 1);
            Check(prepared.Career.CurrentPreparation?.Training == nameof(Training.Tecnico));
            Check(prepared.Career.CurrentPreparation?.SkillApplied == prepared.Career.Skill);
            Check(prepared.Career.CurrentPreparation?.CreditsRemaining == prepared.Career.Credits);
            var restored = Dispatch(new BrowserCareerRequest
            {
                Operation = "load",
                Checkpoint = prepared.Checkpoint
            });
            Check(restored.Ok && restored.Career != null && restored.Checkpoint == prepared.Checkpoint);
            var restoredCareer = restored.Career ?? throw new Exception("Carreira carregada ausente.");
            Check(restoredCareer.Skill == 72 && restoredCareer.Credits == 3 && restoredCareer.Prepared);
        }),
        ("Ponte web de carreira: créditos insuficientes e comandos inválidos não preparam a etapa", () =>
        {
            var bridge = CareerBridge.Create(42, "Ari Horizonte", "Brasil",
                CareerOrigin.LigadaAoEsporte, "t03");
            for (var round = 1; round <= CareerSession.InitialCredits; round++)
            {
                bridge.PrepareSingle(round, Training.Tecnico, Pace.Equilibrado);
                bridge.Run(round);
            }
            var checkpoint = Convert.ToBase64String(bridge.ExportCheckpoint());
            var loaded = Dispatch(new BrowserCareerRequest { Operation = "load", Checkpoint = checkpoint });
            Check(loaded.Ok && loaded.Career != null && loaded.Career.Credits == 0);
            var technical = loaded.Career!.PreparationOptions.Single(option =>
                option.Training == nameof(Training.Tecnico));
            var rest = loaded.Career.PreparationOptions.Single(option =>
                option.Training == nameof(Training.Descanso));
            Check(!technical.Available && technical.CreditCost == 1 &&
                technical.UnavailableReason != null);
            Check(rest.Available && rest.CreditCost == 0);
            Check(!Prepare(loaded, nameof(Training.Tecnico), nameof(Pace.Equilibrado), 5).Ok);
            Check(!Prepare(loaded, "TreinoInventado", nameof(Pace.Equilibrado), 5).Ok);
            Check(!Prepare(loaded, nameof(Training.Descanso), "RitmoInventado", 5).Ok);
            var unchanged = Dispatch(new BrowserCareerRequest { Operation = "load", Checkpoint = checkpoint });
            Check(unchanged.Ok && unchanged.Career != null &&
                unchanged.Career.Credits == 0 && !unchanged.Career.Prepared &&
                unchanged.Checkpoint == checkpoint);
        }),
        ("Ponte web de carreira: classificação e destaques só aparecem após etapa oficial concluída", () =>
        {
            var bridge = CareerBridge.Create(42, "Marina Costa", "Brasil",
                CareerOrigin.LigadaAoEsporte, "t03");
            bridge.PreparePlan(1, Training.Descanso,
                new RaceStrategy(Pace.Equilibrado, Pace.Equilibrado, Pace.Equilibrado),
                tacticalOpponents: false);
            bridge.Run(1);
            var reply = BuildReply(bridge);
            var career = reply.Career ?? throw new Exception("Resumo C# da temporada ausente.");
            Check(reply.Ok && career.CompletedRounds == 1);
            var first = career.Calendar[0];
            Check(first.Status == "Concluida" && first.Classification.Length == 20);
            Check(first.Classification.Count(row => row.IsProtagonist) == 1);
            Check(career.DriverStandings.Length == 20 && career.TeamStandings.Length == 10);
            Check(career.DriverStandings.Any(row =>
                row.IsProtagonist && row.Points == first.Classification.Single(result => result.IsProtagonist).Points));
        }),
        ("Ponte web de carreira: checkpoint inválido não cria estado recuperado", () =>
        {
            var invalid = Dispatch(new BrowserCareerRequest { Operation = "load", Checkpoint = "nao-e-base64" });
            Check(!invalid.Ok && invalid.Career == null && invalid.Checkpoint == null);
        }),
        ("Ponte web de carreira: demonstração permanece separada do piloto pessoal", () =>
        {
            var personal = Create();
            var demo = BrowserProofScenario.DispatchRaceCommand(
                new BrowserRaceRequest { Operation = "start" });
            Check(personal.Ok && personal.Career!.Profile.Name == "Marina Costa");
            Check(demo.Ok && demo.Race!.Pilot == "Piloto de Prova");
            Check(demo.Race!.PlayerDriverId == CareerProfile.PlayerId);
        }),
        ("Ponte web de carreira: início cria sessão oficial de 20 pilotos e impede início duplicado", () =>
        {
            var prepared = Prepare(Create(), nameof(Training.Descanso), nameof(Pace.Equilibrado));
            var started = StartRace(prepared);
            var race = started.Race ?? throw new Exception("Snapshot da corrida ativa ausente.");
            Check(started.Ok && started.CheckpointFormatVersion == 2);
            Check(started.Career?.CompletedRounds == 0 && started.Career.NextRound == 1);
            Check(race.Round == 1 && !race.Finished && race.CompletedSectors == 0);
            Check(race.Classification.Count == 20 &&
                race.Classification.Select(row => row.DriverId).Distinct().Count() == 20);
            Check(race.Classification.Count(row => row.IsProtagonist &&
                row.DriverId == race.PlayerDriverId && row.Name == "Marina Costa") == 1);
            Check(race.Progress.Drivers.Count == 20 && race.SectorTimings.Drivers.Count == 20);
            Check(race.PaceOptions.SequenceEqual(Enum.GetNames(typeof(Pace))));
            var resumed = Dispatch(new BrowserCareerRequest
            {
                Operation = "load",
                Checkpoint = started.Checkpoint
            });
            var resumedRace = resumed.Race ?? throw new Exception("Corrida retomada ausente.");
            Check(resumed.Ok && !resumedRace.Finished);
            Check(resumed.Checkpoint == started.Checkpoint);
            Check(resumedRace.RandomState == race.RandomState);
            Check(!Dispatch(new BrowserCareerRequest
            {
                Operation = "startRace",
                Checkpoint = started.Checkpoint,
                ExpectedRound = 1
            }).Ok);
        }),
        ("Ponte web de carreira: decisões e tempos de setor são oficiais e retomáveis", () =>
        {
            var started = StartRace(Prepare(Create(), nameof(Training.Tecnico), nameof(Pace.Conservar)));
            var first = AdvanceRace(started, 1, nameof(Pace.Atacar));
            var race = first.Race ?? throw new Exception("Snapshot do setor ausente.");
            Check(first.Ok && first.Career?.CompletedRounds == 0);
            Check(race.CompletedSectors == 1 && race.NextSector == 2);
            Check(race.Progress.CompletedSectors == 1 && race.SectorTimings.CompletedSectors == 1);
            Check(race.SectorTimings.Drivers.All(driver =>
                !driver.Available || driver.Sectors.Count == 1));
            Check(race.Decisions.Any(item => item.DriverId == race.PlayerDriverId &&
                item.Sector == 1 && item.Pace == nameof(Pace.Atacar)));
            Check(race.Classification.Count == 20);
            var reloaded = Dispatch(new BrowserCareerRequest
            {
                Operation = "load",
                Checkpoint = first.Checkpoint
            });
            var reloadedRace = reloaded.Race ?? throw new Exception("Snapshot após reload ausente.");
            Check(reloaded.Ok && reloadedRace.CompletedSectors == 1 &&
                reloadedRace.RandomState == race.RandomState &&
                reloaded.Checkpoint == first.Checkpoint);
            Check(!AdvanceRace(first, 1, nameof(Pace.Atacar)).Ok);
            Check(!AdvanceRace(first, 3, nameof(Pace.Conservar)).Ok);
            Check(!AdvanceRace(first, 2, "RitmoInventado").Ok);
            var branch = AdvanceRace(reloaded, 2, nameof(Pace.Equilibrado));
            var sameBranch = AdvanceRace(first, 2, nameof(Pace.Equilibrado));
            Check(branch.Ok && sameBranch.Ok && branch.Checkpoint == sameBranch.Checkpoint);
            Check(JsonSerializer.Serialize(branch.Race, WebJson) ==
                JsonSerializer.Serialize(sameBranch.Race, WebJson));
        }),
        ("Ponte web de carreira: bandeirada pontua uma vez e reload preserva o resultado", () =>
        {
            var started = StartRace(Prepare(Create(), nameof(Training.Descanso), nameof(Pace.Equilibrado)));
            var current = started;
            for (var sector = 1; sector <= KartRules.DefaultLaps * 3; sector++)
            {
                var pace = (sector % 3) switch
                {
                    1 => nameof(Pace.Conservar),
                    2 => nameof(Pace.Equilibrado),
                    _ => nameof(Pace.Atacar)
                };
                current = AdvanceRace(current, sector, pace);
                Check(current.Ok);
            }

            var finalRace = current.Race ?? throw new Exception("Classificação final C# ausente.");
            var finalCareer = current.Career ?? throw new Exception("Resumo final C# ausente.");
            Check(finalRace.Finished && finalRace.CompletedSectors == KartRules.DefaultLaps * 3);
            Check(finalRace.Classification.Count == 20 &&
                finalCareer.CompletedRounds == 1 && finalCareer.NextRound == 2);
            Check(finalCareer.Calendar[0].Status == nameof(HistoryRoundStatus.Concluida));
            Check(finalRace.Classification.Select(row => row.Points)
                .SequenceEqual(finalCareer.Calendar[0].Classification
                    .OrderBy(row => row.Position).Select(row => row.Points)));
            var playerResult = finalRace.Classification.Single(row => row.IsProtagonist);
            Check(finalCareer.DriverStandings.Single(row => row.IsProtagonist).Points == playerResult.Points);
            Check(finalRace.Decisions.Count(decision => decision.DriverId == finalRace.PlayerDriverId) ==
                KartRules.DefaultLaps * 3);

            var reloaded = Dispatch(new BrowserCareerRequest
            {
                Operation = "load",
                Checkpoint = current.Checkpoint
            });
            Check(reloaded.Ok && reloaded.Race?.Finished == true && reloaded.Career?.CompletedRounds == 1);
            Check(reloaded.Checkpoint == current.Checkpoint);
            Check(JsonSerializer.Serialize(reloaded.Race!.Classification, WebJson) ==
                JsonSerializer.Serialize(finalRace.Classification, WebJson));
            Check(finalRace.Weather is "Seco" or "Chuva");
            Check(reloaded.Race.Weather == finalRace.Weather);
            CheckSnapshotEquivalent("career", finalCareer, reloaded.Career!);
            CheckSnapshotEquivalent("race", finalRace, reloaded.Race!);
            Check(!AdvanceRace(current, KartRules.DefaultLaps * 3, nameof(Pace.Atacar)).Ok);
            Check(!Dispatch(new BrowserCareerRequest
            {
                Operation = "startRace",
                Checkpoint = current.Checkpoint,
                ExpectedRound = 1
            }).Ok);
        }),
        ("Ponte web: temporada completa usa oito etapas oficiais, acumula pontos e retoma checkpoints", () =>
        {
            var current = Create();
            for (var round = 1; round <= InitialData.KartCalendar.Count; round++)
            {
                var training = round is >= 2 and <= 5
                    ? nameof(Training.Tecnico)
                    : nameof(Training.Descanso);
                if (round == 1)
                {
                    Check(!Prepare(current, training, nameof(Pace.Equilibrado), 2).Ok);
                    Check(!StartRace(current, 2).Ok);
                }
                current = Prepare(current, training, nameof(Pace.Equilibrado), round);
                Check(current.Ok && current.Career?.CurrentPreparation?.Round == round);
                current = StartRace(current, round);
                Check(current.Ok && current.Race?.Round == round &&
                    current.Race.CompletedSectors == 0 && current.Checkpoint != null);

                for (var sector = 1; sector <= KartRules.DefaultLaps * 3; sector++)
                {
                    var pace = ((sector + round) % 3) switch
                    {
                        0 => nameof(Pace.Conservar),
                        1 => nameof(Pace.Equilibrado),
                        _ => nameof(Pace.Atacar)
                    };
                    current = AdvanceRace(current, sector, pace, round);
                    Check(current.Ok && current.Race?.CompletedSectors == sector);

                    if (sector == 18)
                    {
                        var beforeReload = current.Race!;
                        var checkpoint = current.Checkpoint!;
                        current = Dispatch(new BrowserCareerRequest
                        {
                            Operation = "load",
                            Checkpoint = checkpoint
                        });
                        Check(current.Ok && current.Checkpoint == checkpoint &&
                            current.CheckpointFormatVersion == 2);
                        CheckSnapshotEquivalent($"setor {round}/{sector}",
                            beforeReload, current.Race!);
                    }
                }

                var race = current.Race ?? throw new Exception("Resultado da etapa ausente.");
                var career = current.Career ?? throw new Exception("Resumo da carreira ausente.");
                Check(race.Finished && race.CompletedSectors == KartRules.DefaultLaps * 3);
                Check(race.Round == round && race.Classification.Count == 20);
                Check(career.CompletedRounds == round && career.NextRound == round + 1);
                Check(career.Finished == (round == InitialData.KartCalendar.Count));
                Check(career.Calendar.Count(item => item.Status == nameof(HistoryRoundStatus.Concluida)) == round);
                Check(career.Calendar.Take(round).All(item =>
                    item.Status == nameof(HistoryRoundStatus.Concluida) &&
                    item.Classification.Length == 20));

                var officialResults = career.Calendar
                    .Where(item => item.Status == nameof(HistoryRoundStatus.Concluida))
                    .SelectMany(item => item.Classification).ToArray();
                Check(career.DriverStandings.Length == 20 && career.TeamStandings.Length == 10);
                Check(career.DriverStandings.Select((row, index) => row.Position == index + 1).All(value => value));
                Check(career.TeamStandings.Select((row, index) => row.Position == index + 1).All(value => value));
                Check(career.DriverStandings.Sum(row => row.Points) ==
                    officialResults.Sum(result => result.Points));
                Check(career.TeamStandings.Sum(row => row.Points) ==
                    officialResults.Sum(result => result.Points));
                foreach (var standing in career.DriverStandings)
                {
                    var results = officialResults.Where(result => result.DriverId == standing.Id).ToArray();
                    Check(standing.Points == results.Sum(result => result.Points));
                    Check(standing.Wins == results.Count(result => result.Position == 1));
                }
                foreach (var standing in career.TeamStandings)
                {
                    var results = officialResults.Where(result => result.TeamId == standing.Id).ToArray();
                    Check(standing.Points == results.Sum(result => result.Points));
                    Check(standing.Wins == results.Count(result => result.Position == 1));
                }

                var completedCheckpoint = current.Checkpoint!;
                Check(!StartRace(current, round).Ok);
                Check(!AdvanceRace(current, KartRules.DefaultLaps * 3,
                    nameof(Pace.Atacar), round).Ok);
                Check(current.Checkpoint == completedCheckpoint);
                current = Dispatch(new BrowserCareerRequest
                {
                    Operation = "load",
                    Checkpoint = completedCheckpoint
                });
                Check(current.Ok && current.Checkpoint == completedCheckpoint);
                CheckSnapshotEquivalent($"etapa {round} carreira",
                    career, current.Career!);
                CheckSnapshotEquivalent($"etapa {round} corrida",
                    race, current.Race!);
            }

            var finalCareer = current.Career!;
            Check(finalCareer.Finished && finalCareer.CompletedRounds == 8 &&
                finalCareer.Calendar.All(item => item.Status == nameof(HistoryRoundStatus.Concluida)));
            Check(finalCareer.DriverStandings[0].Position == 1);
            Check(!Prepare(current, nameof(Training.Descanso), nameof(Pace.Equilibrado), 9).Ok);
            Check(!StartRace(current, 8).Ok);
            Check(!AdvanceRace(current, KartRules.DefaultLaps * 3, nameof(Pace.Atacar), 8).Ok);

            var pendingProgression = finalCareer.Progression;
            Check(pendingProgression.CurrentCategoryId == "kart" &&
                pendingProgression.Eligible && pendingProgression.OfferAvailable &&
                pendingProgression.Decision == "None");
            Check(pendingProgression.Categories.Length == 6 &&
                pendingProgression.Categories.Single(category => category.Id == "f4").Rounds == 10 &&
                pendingProgression.Categories.Single(category => category.Id == "f4").Implemented);
            Check(pendingProgression.Seasons.Length == 1 &&
                pendingProgression.Seasons[0].CategoryId == "kart" &&
                pendingProgression.Seasons[0].Status == nameof(CareerSeasonStatus.Concluida));
            Check(pendingProgression.OfferedTeams.Length == Formula4Data.Teams.Count);
            var originalKart = JsonSerializer.Serialize(finalCareer.Calendar, WebJson);

            var accepted = Dispatch(new BrowserCareerRequest
            {
                Operation = "acceptPromotion",
                Checkpoint = current.Checkpoint,
                TeamId = Formula4Data.Teams[4].Id
            });
            Check(accepted.Ok && accepted.CheckpointFormatVersion == 3);
            var promoted = accepted.Career!;
            Check(promoted.Progression.CurrentCategoryId == "f4" &&
                promoted.Progression.Decision == "Accepted" &&
                promoted.Progression.CurrentTeamId == Formula4Data.Teams[4].Id &&
                promoted.CurrentTeamName == Formula4Data.Teams[4].Name);
            Check(promoted.Profile.Name == finalCareer.Profile.Name &&
                promoted.Profile.Nationality == finalCareer.Profile.Nationality &&
                promoted.Profile.Origin == finalCareer.Profile.Origin);
            Check(promoted.Calendar.Length == 8 &&
                JsonSerializer.Serialize(promoted.Calendar, WebJson) == originalKart);
            var f4Season = promoted.Progression.Seasons.Single(season => season.CategoryId == "f4");
            Check(f4Season.SeasonNumber == 1 &&
                f4Season.Status == nameof(CareerSeasonStatus.AguardandoMotorDaCategoria) &&
                f4Season.TotalRounds == 10 && f4Season.CompletedRounds == 0 &&
                f4Season.Calendar.Select(round => round.Number).SequenceEqual(Enumerable.Range(1, 10)));
            Check(f4Season.Participants.Length == 20 &&
                f4Season.Participants.Count(participant => participant.IsPlayer) == 1 &&
                f4Season.Participants.Single(participant => participant.IsPlayer).Id == CareerProfile.PlayerId &&
                f4Season.Participants.Single(participant => participant.IsPlayer).Name == finalCareer.Profile.Name);
            Check(promoted.Progression.TeamHistory.Length == 2 &&
                promoted.Progression.TeamHistory[0].CategoryId == "kart" &&
                promoted.Progression.TeamHistory[1].CategoryId == "f4");
            var promotedCheckpoint = accepted.Checkpoint!;
            var resumedPromotion = Dispatch(new BrowserCareerRequest
            {
                Operation = "load",
                Checkpoint = promotedCheckpoint
            });
            Check(resumedPromotion.Ok && resumedPromotion.CheckpointFormatVersion == 3 &&
                resumedPromotion.Checkpoint == promotedCheckpoint);
            CheckSnapshotEquivalent("promoção aceita após reload", promoted, resumedPromotion.Career!);
            Check(!Dispatch(new BrowserCareerRequest
            {
                Operation = "acceptPromotion",
                Checkpoint = promotedCheckpoint,
                TeamId = Formula4Data.Teams[0].Id
            }).Ok);
            Check(!StartRace(resumedPromotion, 9).Ok);

            var declined = Dispatch(new BrowserCareerRequest
            {
                Operation = "declinePromotion",
                Checkpoint = current.Checkpoint
            });
            var declinedCareer = declined.Career ?? throw new Exception("Resposta de recusa sem carreira.");
            Check(declined.Ok && declined.CheckpointFormatVersion == 3 &&
                declinedCareer.Progression.Decision == "Declined" &&
                !declinedCareer.Progression.OfferAvailable &&
                declinedCareer.Progression.CurrentCategoryId == "kart" &&
                declinedCareer.Progression.Seasons.Length == 1);
            Check(JsonSerializer.Serialize(declinedCareer.Calendar, WebJson) == originalKart);
            var resumedDecline = Dispatch(new BrowserCareerRequest
            {
                Operation = "load",
                Checkpoint = declined.Checkpoint
            });
            CheckSnapshotEquivalent("promoção recusada após reload", declinedCareer, resumedDecline.Career!);
            Check(!Dispatch(new BrowserCareerRequest
            {
                Operation = "declinePromotion",
                Checkpoint = declined.Checkpoint
            }).Ok);
        })
    };

    private static BrowserCareerReply Create() => Dispatch(new BrowserCareerRequest
    {
        Operation = "create",
        Seed = 42,
        Name = "Marina Costa",
        Nationality = "Brasil",
        Origin = nameof(CareerOrigin.LigadaAoEsporte),
        TeamId = "t03"
    });

    private static BrowserCareerReply Prepare(BrowserCareerReply source, string training, string pace,
        int expectedRound = 1) => Dispatch(new BrowserCareerRequest
    {
        Operation = "prepare",
        Checkpoint = source.Checkpoint,
        ExpectedRound = expectedRound,
        Training = training,
        Pace = pace
    });

    private static BrowserCareerReply StartRace(BrowserCareerReply source, int expectedRound = 1) =>
        Dispatch(new BrowserCareerRequest
        {
            Operation = "startRace",
            Checkpoint = source.Checkpoint,
            ExpectedRound = expectedRound
        });

    private static BrowserCareerReply AdvanceRace(BrowserCareerReply source, int expectedSector, string pace,
        int expectedRound = 1) => Dispatch(new BrowserCareerRequest
        {
            Operation = "advanceRace",
            Checkpoint = source.Checkpoint,
            ExpectedRound = expectedRound,
            ExpectedSector = expectedSector,
            Pace = pace
        });

    private static BrowserCareerReply CatalogReply() => new()
    {
        Ok = true,
        Catalog = new BrowserCareerCatalog
        {
            Teams = InitialData.Teams.Select(team => new BrowserCareerTeam
            {
                Id = team.Id,
                Name = team.Name
            }).ToArray(),
            Origins = Enum.GetNames<CareerOrigin>(),
            Calendar = InitialData.KartCalendar.Select(round =>
            {
                var circuit = InitialData.Circuits.Single(item => item.Id == round.CircuitId);
                return new BrowserCareerCalendarRound
                {
                    Number = round.Number,
                    CircuitId = circuit.Id,
                    CircuitName = circuit.Name
                };
            }).ToArray()
        }
    };

    private static BrowserCareerReply BuildReply(CareerBridge bridge, RaceState? lastRaceState = null) =>
        BuildReply(CareerJourneyBridge.LoadCheckpoint(bridge.ExportCheckpoint()), lastRaceState);

    private static BrowserCareerReply BuildReply(CareerJourneyBridge bridge, RaceState? lastRaceState = null)
    {
        var overview = bridge.ReadOverview();
        var history = bridge.ReadHistory();
        var activeRace = bridge.ReadActiveRace();
        var lastCompletedRound = history.Rounds.LastOrDefault(round =>
            round.Status == HistoryRoundStatus.Concluida);
        var raceRound = activeRace?.Round ?? lastCompletedRound?.Number ?? 0;
        var raceSnapshot = raceRound == 0
            ? null
            : BrowserProofScenario.BuildRaceReply(
                bridge.Kart, raceRound, activeRace?.State ?? lastRaceState).Race;
        var profile = overview.Profile;
        var progression = bridge.ReadProgression();
        var teams = InitialData.Teams.Concat(Formula4Data.Teams).ToDictionary(team => team.Id);
        var checkpoint = bridge.ExportCheckpoint();
        var reply = new BrowserCareerReply
        {
            Ok = true,
            Career = new BrowserCareerSnapshot
            {
                Engine = "Stintoria.Core via Stintoria.ClientBridge (.NET local)",
                ExecutionMode = "HTTP local, somente prévia de desenvolvimento",
                Seed = overview.Seed,
                Profile = new BrowserCareerProfile
                {
                    Name = profile.Name,
                    Nationality = profile.Nationality,
                    Age = profile.Age,
                    Origin = profile.Origin.ToString(),
                    TeamId = profile.TeamId,
                    TeamName = teams[profile.TeamId].Name
                },
                Progression = ToBrowserProgression(progression),
                CurrentTeamId = progression.CurrentTeamId,
                CurrentTeamName = teams[progression.CurrentTeamId].Name,
                NextRound = overview.NextRound,
                Skill = overview.Skill,
                Credits = overview.Credits,
                Prepared = overview.Prepared,
                PreparationOptions = overview.Finished
                    ? Array.Empty<BrowserCareerPreparationOption>()
                    : bridge.ReadPreparationOptions(overview.NextRound).Select(option =>
                        new BrowserCareerPreparationOption
                        {
                            Training = option.Training.ToString(),
                            Available = option.Available,
                            CreditCost = option.CreditCost,
                            SkillDelta = option.SkillDelta,
                            UnavailableReason = option.UnavailableReason
                        }).ToArray(),
                PaceOptions = Enum.GetNames(typeof(Pace)).Cast<string>().ToArray(),
                CurrentPreparation = overview.Finished ? null :
                    history.GetRound(overview.NextRound).Preparation is { } preparation
                        ? new BrowserCareerPreparation
                        {
                            Round = overview.NextRound,
                            Training = preparation.Training.ToString(),
                            Pace = preparation.Pace.ToString(),
                            SkillApplied = preparation.SkillApplied,
                            CreditsRemaining = preparation.CreditsRemaining
                        }
                        : null,
                Finished = overview.Finished,
                CompletedRounds = history.CompletedRounds,
                TotalRounds = history.TotalRounds,
                Calendar = history.Rounds.Select(round => new BrowserCareerCalendarRound
                {
                    Number = round.Number,
                    CircuitId = round.CircuitId,
                    CircuitName = round.CircuitName,
                    Status = round.Status.ToString(),
                    Classification = round.Status == HistoryRoundStatus.Concluida
                        ? round.Classification.Select(result => new BrowserCareerResult
                        {
                            Position = result.Position,
                            DriverId = result.DriverId,
                            DriverName = result.DriverName,
                            TeamId = result.TeamId,
                            TeamName = result.TeamName,
                            IsProtagonist = result.IsProtagonist,
                            Points = result.Points,
                            ElapsedMs = result.ElapsedMs
                        }).ToArray()
                        : Array.Empty<BrowserCareerResult>(),
                    Highlights = round.Highlights.Select(item => item.Explanation).ToArray()
                }).ToArray(),
                DriverStandings = history.CompletedRounds == 0
                    ? Array.Empty<BrowserCareerStanding>()
                    : history.DriverStandings.Select(row => new BrowserCareerStanding
                    {
                        Position = row.Position,
                        Id = row.Id,
                        Name = row.Name,
                        IsProtagonist = row.IsPlayerAssociated,
                        Points = row.Points,
                        Wins = row.Wins
                    }).ToArray(),
                TeamStandings = history.CompletedRounds == 0
                    ? Array.Empty<BrowserCareerStanding>()
                    : history.TeamStandings.Select(row => new BrowserCareerStanding
                    {
                        Position = row.Position,
                        Id = row.Id,
                        Name = row.Name,
                        IsProtagonist = row.IsPlayerAssociated,
                        Points = row.Points,
                        Wins = row.Wins
                    }).ToArray()
            },
            Race = raceSnapshot,
            Formula4 = progression.Decision == PromotionDecision.Accepted
                ? bridge.ReadFormula4Championship()
                : null,
            Checkpoint = Convert.ToBase64String(checkpoint),
            CheckpointFormatVersion = CareerJourneyBridge.GetCheckpointFormatVersion(checkpoint)
        };
        return reply;
    }

    private static BrowserProgressionSnapshot ToBrowserProgression(CareerProgressionSnapshot source) => new()
    {
        Version = source.Version,
        DataVersion = source.DataVersion,
        CurrentCategoryId = source.CurrentCategoryId,
        CurrentTeamId = source.CurrentTeamId,
        Eligible = source.Eligible,
        OfferAvailable = source.OfferAvailable,
        Decision = source.Decision.ToString(),
        EligibilityRule = source.EligibilityRule,
        Categories = source.Categories.Select(category => new BrowserCareerCategory
        {
            Id = category.Id,
            Name = category.Name,
            Rounds = category.Rounds,
            Level = category.Level.ToString(),
            Implemented = category.Implemented
        }).ToArray(),
        Formula4Teams = source.Formula4Teams.Select(team => new BrowserCareerTeam
        {
            Id = team.Id,
            Name = team.Name
        }).ToArray(),
        OfferedTeams = source.OfferedTeams.Select(team => new BrowserCareerTeam
        {
            Id = team.Id,
            Name = team.Name
        }).ToArray(),
        Seasons = source.Seasons.Select(season => new BrowserCareerSeason
        {
            CategoryId = season.CategoryId,
            SeasonNumber = season.SeasonNumber,
            Status = season.Status.ToString(),
            TeamId = season.TeamId,
            TeamName = season.TeamName,
            TotalRounds = season.TotalRounds,
            CompletedRounds = season.CompletedRounds,
            Calendar = season.Calendar.Select(round => new BrowserCareerSeasonRound
            {
                Number = round.Number,
                Id = round.Id,
                Name = round.Name
            }).ToArray(),
            History = season.History == null ? null : new BrowserCareerSeasonHistory
            {
                ProtagonistId = season.History.ProtagonistId,
                CompletedRounds = season.History.CompletedRounds,
                TotalRounds = season.History.TotalRounds,
                Finished = season.History.Finished,
                DriverChampionId = season.History.DriverChampionId,
                TeamChampionId = season.History.TeamChampionId,
                RoundNumbers = season.History.Rounds.Select(round => round.Number).ToArray()
            },
            Participants = season.Participants.Select(participant => new BrowserCareerParticipant
            {
                Id = participant.Id,
                Name = participant.Name,
                TeamId = participant.TeamId,
                IsPlayer = participant.IsPlayer
            }).ToArray()
        }).ToArray(),
        TeamHistory = source.TeamHistory.Select(entry => new BrowserCareerTeamHistory
        {
            CategoryId = entry.CategoryId,
            SeasonNumber = entry.SeasonNumber,
            TeamId = entry.TeamId,
            TeamName = entry.TeamName
        }).ToArray(),
        CurrentParticipants = source.CurrentParticipants.Select(participant => new BrowserCareerParticipant
        {
            Id = participant.Id,
            Name = participant.Name,
            TeamId = participant.TeamId,
            IsPlayer = participant.IsPlayer
        }).ToArray()
    };

    private static byte[] DecodeCheckpoint(string? value)
    {
        var maxBase64Length = ((ClientCheckpoint.MaxBytes + 2) / 3) * 4;
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxBase64Length)
            throw new ArgumentException("Checkpoint de carreira ausente ou acima do limite permitido.");
        return Convert.FromBase64String(value);
    }

    private static void Check(bool condition, [CallerLineNumber] int line = 0)
    {
        if (!condition) throw new Exception("Invariante da ponte web de carreira não atendida (linha " + line + ").");
    }

    private static void CheckSnapshotEquivalent<T>(string name, T expected, T actual)
    {
        var expectedJson = JsonSerializer.SerializeToElement(expected, WebJson);
        var actualJson = JsonSerializer.SerializeToElement(actual, WebJson);
        var differences = new List<string>();
        CollectDifferences(expectedJson, actualJson, "$", differences);
        if (differences.Count > 0)
        {
            throw new Exception(
                $"Divergência no snapshot {name} após reload:{Environment.NewLine}" +
                string.Join(Environment.NewLine, differences));
        }
    }

    private static void CollectDifferences(
        JsonElement expected,
        JsonElement actual,
        string path,
        List<string> differences)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            differences.Add($"{path}: {expected.ValueKind} -> {actual.ValueKind}");
            return;
        }

        if (expected.ValueKind == JsonValueKind.Object)
        {
            var expectedProperties = expected.EnumerateObject().ToDictionary(item => item.Name, item => item.Value);
            var actualProperties = actual.EnumerateObject().ToDictionary(item => item.Name, item => item.Value);
            foreach (var propertyName in expectedProperties.Keys.Union(actualProperties.Keys).OrderBy(value => value))
            {
                var propertyPath = $"{path}.{propertyName}";
                if (!expectedProperties.TryGetValue(propertyName, out var expectedValue))
                {
                    differences.Add($"{propertyPath}: ausente -> {actualProperties[propertyName].GetRawText()}");
                }
                else if (!actualProperties.TryGetValue(propertyName, out var actualValue))
                {
                    differences.Add($"{propertyPath}: {expectedValue.GetRawText()} -> ausente");
                }
                else
                {
                    CollectDifferences(expectedValue, actualValue, propertyPath, differences);
                }
            }
            return;
        }

        if (expected.ValueKind == JsonValueKind.Array)
        {
            var expectedItems = expected.EnumerateArray().ToArray();
            var actualItems = actual.EnumerateArray().ToArray();
            if (expectedItems.Length != actualItems.Length)
                differences.Add($"{path}.length: {expectedItems.Length} -> {actualItems.Length}");
            for (var index = 0; index < Math.Min(expectedItems.Length, actualItems.Length); index++)
                CollectDifferences(expectedItems[index], actualItems[index], $"{path}[{index}]", differences);
            return;
        }

        var equal = expected.ValueKind switch
        {
            JsonValueKind.String => expected.GetString() == actual.GetString(),
            JsonValueKind.Number => expected.GetDecimal() == actual.GetDecimal(),
            JsonValueKind.True or JsonValueKind.False => expected.GetBoolean() == actual.GetBoolean(),
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            _ => expected.GetRawText() == actual.GetRawText()
        };
        if (!equal)
            differences.Add($"{path}: {expected.GetRawText()} -> {actual.GetRawText()}");
    }
}

internal sealed class BrowserCareerRequest
{
    public string Operation { get; set; } = "";
    public uint? Seed { get; set; }
    public string? Name { get; set; }
    public string? Nationality { get; set; }
    public string? Origin { get; set; }
    public string? TeamId { get; set; }
    public string? Checkpoint { get; set; }
    public int? ExpectedRound { get; set; }
    public int? ExpectedSector { get; set; }
    public string? Training { get; set; }
    public string? Pace { get; set; }
    public string? BeginningPace { get; set; }
    public string? MiddlePace { get; set; }
    public string? FinalPace { get; set; }
    public int? MiddleStartSector { get; set; }
    public int? FinalStartSector { get; set; }
    public int? Count { get; set; }
}

internal sealed class BrowserCareerReply
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public BrowserCareerCatalog? Catalog { get; set; }
    public BrowserCareerSnapshot? Career { get; set; }
    public BrowserRaceSnapshot? Race { get; set; }
    public Formula4ChampionshipSnapshot? Formula4 { get; set; }
    public string? Checkpoint { get; set; }
    public int CheckpointFormatVersion { get; set; }
}

internal sealed class BrowserCareerCatalog
{
    public BrowserCareerTeam[] Teams { get; set; } = Array.Empty<BrowserCareerTeam>();
    public string[] Origins { get; set; } = Array.Empty<string>();
    public BrowserCareerCalendarRound[] Calendar { get; set; } = Array.Empty<BrowserCareerCalendarRound>();
}

internal sealed class BrowserCareerTeam
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

internal sealed class BrowserCareerProfile
{
    public string Name { get; set; } = "";
    public string Nationality { get; set; } = "";
    public int Age { get; set; }
    public string Origin { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string TeamName { get; set; } = "";
}

internal sealed class BrowserCareerSnapshot
{
    public string Engine { get; set; } = "";
    public string ExecutionMode { get; set; } = "";
    public uint Seed { get; set; }
    public BrowserCareerProfile Profile { get; set; } = new();
    public BrowserProgressionSnapshot Progression { get; set; } = new();
    public string CurrentTeamId { get; set; } = "";
    public string CurrentTeamName { get; set; } = "";
    public int NextRound { get; set; }
    public int Skill { get; set; }
    public int Credits { get; set; }
    public bool Prepared { get; set; }
    public BrowserCareerPreparationOption[] PreparationOptions { get; set; } =
        Array.Empty<BrowserCareerPreparationOption>();
    public string[] PaceOptions { get; set; } = Array.Empty<string>();
    public BrowserCareerPreparation? CurrentPreparation { get; set; }
    public bool Finished { get; set; }
    public int CompletedRounds { get; set; }
    public int TotalRounds { get; set; }
    public BrowserCareerCalendarRound[] Calendar { get; set; } = Array.Empty<BrowserCareerCalendarRound>();
    public BrowserCareerStanding[] DriverStandings { get; set; } = Array.Empty<BrowserCareerStanding>();
    public BrowserCareerStanding[] TeamStandings { get; set; } = Array.Empty<BrowserCareerStanding>();
}

internal sealed class BrowserProgressionSnapshot
{
    public string Version { get; set; } = "";
    public string DataVersion { get; set; } = "";
    public string CurrentCategoryId { get; set; } = "";
    public string CurrentTeamId { get; set; } = "";
    public bool Eligible { get; set; }
    public bool OfferAvailable { get; set; }
    public string Decision { get; set; } = "None";
    public string EligibilityRule { get; set; } = "";
    public BrowserCareerCategory[] Categories { get; set; } = Array.Empty<BrowserCareerCategory>();
    public BrowserCareerTeam[] Formula4Teams { get; set; } = Array.Empty<BrowserCareerTeam>();
    public BrowserCareerTeam[] OfferedTeams { get; set; } = Array.Empty<BrowserCareerTeam>();
    public BrowserCareerSeason[] Seasons { get; set; } = Array.Empty<BrowserCareerSeason>();
    public BrowserCareerTeamHistory[] TeamHistory { get; set; } = Array.Empty<BrowserCareerTeamHistory>();
    public BrowserCareerParticipant[] CurrentParticipants { get; set; } = Array.Empty<BrowserCareerParticipant>();
}

internal sealed class BrowserCareerCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Rounds { get; set; }
    public string Level { get; set; } = "";
    public bool Implemented { get; set; }
}

internal sealed class BrowserCareerSeason
{
    public string CategoryId { get; set; } = "";
    public int SeasonNumber { get; set; }
    public string Status { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string TeamName { get; set; } = "";
    public int TotalRounds { get; set; }
    public int CompletedRounds { get; set; }
    public BrowserCareerSeasonRound[] Calendar { get; set; } = Array.Empty<BrowserCareerSeasonRound>();
    public BrowserCareerParticipant[] Participants { get; set; } = Array.Empty<BrowserCareerParticipant>();
    public BrowserCareerSeasonHistory? History { get; set; }
}

internal sealed class BrowserCareerSeasonHistory
{
    public string? ProtagonistId { get; set; }
    public int CompletedRounds { get; set; }
    public int TotalRounds { get; set; }
    public bool Finished { get; set; }
    public string? DriverChampionId { get; set; }
    public string? TeamChampionId { get; set; }
    public int[] RoundNumbers { get; set; } = Array.Empty<int>();
}

internal sealed class BrowserCareerSeasonRound
{
    public int Number { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

internal sealed class BrowserCareerParticipant
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TeamId { get; set; } = "";
    public bool IsPlayer { get; set; }
}

internal sealed class BrowserCareerTeamHistory
{
    public string CategoryId { get; set; } = "";
    public int SeasonNumber { get; set; }
    public string TeamId { get; set; } = "";
    public string TeamName { get; set; } = "";
}

internal sealed class BrowserCareerPreparationOption
{
    public string Training { get; set; } = "";
    public bool Available { get; set; }
    public int CreditCost { get; set; }
    public int SkillDelta { get; set; }
    public string? UnavailableReason { get; set; }
}

internal sealed class BrowserCareerPreparation
{
    public int Round { get; set; }
    public string Training { get; set; } = "";
    public string Pace { get; set; } = "";
    public int SkillApplied { get; set; }
    public int CreditsRemaining { get; set; }
}

internal sealed class BrowserCareerCalendarRound
{
    public int Number { get; set; }
    public string CircuitId { get; set; } = "";
    public string CircuitName { get; set; } = "";
    public string Status { get; set; } = "NaoDisputada";
    public BrowserCareerResult[] Classification { get; set; } = Array.Empty<BrowserCareerResult>();
    public string[] Highlights { get; set; } = Array.Empty<string>();
}

internal sealed class BrowserCareerResult
{
    public int Position { get; set; }
    public string DriverId { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string TeamName { get; set; } = "";
    public bool IsProtagonist { get; set; }
    public int Points { get; set; }
    public long ElapsedMs { get; set; }
}

internal sealed class BrowserCareerStanding
{
    public int Position { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsProtagonist { get; set; }
    public int Points { get; set; }
    public int Wins { get; set; }
}
