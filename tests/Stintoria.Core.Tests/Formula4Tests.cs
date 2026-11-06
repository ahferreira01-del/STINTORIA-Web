using System;
using System.Linq;
using System.Text.Json;
using Stintoria.ClientBridge;
using Stintoria.Core;

internal static class Formula4Tests
{
    private static readonly System.Collections.Generic.Dictionary<string, byte[]> PromotionCache =
        new System.Collections.Generic.Dictionary<string, byte[]>(StringComparer.Ordinal);

    internal static (string Name, Action Body)[] Cases() => new (string, Action)[]
    {
        ("F4: contrato próprio, dez pistas, vinte pilotos e dois por equipe", () =>
        {
            Check(Formula4SportRules.Version == "formula4-sport-v1");
            Check(Formula4SportRules.Laps == 15 && Formula4SportRules.TotalSectors == 45);
            Check(Formula4Data.Calendar.Count == 10 && Formula4Data.Circuits.Count == 10);
            Check(Formula4Data.Teams.Count == 10 && Formula4Data.Drivers.Count == 20);
            Check(InitialData.Categories.Single(category => category.Id == "f4").Implemented);
            Check(!InitialData.Categories.Single(category => category.Id == "regional").Implemented);
            Check(Formula4SportRules.Points.Sum() == 140);
        }),
        ("F4: promoção mantém identidade e substitui somente uma vaga", () =>
        {
            var bridge = Promoted(42, "f4t04");
            var snapshot = bridge.StartFormula4Season();
            Check(snapshot.Drivers.Count == 20 && snapshot.Teams.Count == 10);
            Check(snapshot.Drivers.Count(driver => driver.IsPlayer) == 1);
            Check(snapshot.Drivers.Single(driver => driver.IsPlayer).TeamId == "f4t04");
            Check(snapshot.Drivers.GroupBy(driver => driver.TeamId).All(group => group.Count() == 2));
            Check(snapshot.Calendar.Select(round => round.Number).SequenceEqual(Enumerable.Range(1, 10)));
        }),
        ("F4: preparação, sequência e início duplicado são protegidos", () =>
        {
            var bridge = Promoted(10);
            bridge.StartFormula4Season();
            Throws<InvalidOperationException>(() => bridge.StartFormula4Race(1));
            var strategy = Balanced();
            bridge.PrepareFormula4Race(1, strategy);
            var before = bridge.ExportCheckpoint();
            Throws<InvalidOperationException>(() => bridge.PrepareFormula4Race(1, strategy));
            Check(before.SequenceEqual(bridge.ExportCheckpoint()));
            Throws<InvalidOperationException>(() => bridge.PrepareFormula4Race(2, strategy));
            Check(before.SequenceEqual(bridge.ExportCheckpoint()));
            bridge.StartFormula4Race(1);
            var started = bridge.ExportCheckpoint();
            Throws<InvalidOperationException>(() => bridge.StartFormula4Race(1));
            Check(started.SequenceEqual(bridge.ExportCheckpoint()));
        }),
        ("F4: estratégia valida fronteiras e ritmos", () =>
        {
            var strategy = new Formula4Strategy(Pace.Conservar, Pace.Equilibrado, Pace.Atacar, 16, 31);
            Check(strategy.PaceFor(1) == Pace.Conservar);
            Check(strategy.PaceFor(16) == Pace.Equilibrado);
            Check(strategy.PaceFor(31) == Pace.Atacar);
            Throws<ArgumentOutOfRangeException>(() =>
                new Formula4Strategy((Pace)99, Pace.Equilibrado, Pace.Atacar));
            Throws<ArgumentOutOfRangeException>(() =>
                new Formula4Strategy(Pace.Conservar, Pace.Atacar, Pace.Equilibrado, 31, 16));
            Throws<ArgumentOutOfRangeException>(() => strategy.PaceFor(46));
        }),
        ("F4: ritmo solicitado altera desgaste do setor correto", () =>
        {
            var conserve = FirstSector(77, Pace.Conservar);
            var attack = FirstSector(77, Pace.Atacar);
            var c = conserve.Classification.Single(row => row.IsPlayer);
            var a = attack.Classification.Single(row => row.IsPlayer);
            Check(c.TireWear == 1 && a.TireWear == 4);
            Check(c.Pace == Pace.Conservar && a.Pace == Pace.Atacar);
            Check(conserve.CompletedSectors == 1 && attack.CompletedSectors == 1);
            Check(!conserve.Finished && conserve.Classification.All(row => row.Points == 0));
            Check(a.ElapsedMs < c.ElapsedMs,
                "ataque precisa reduzir o tempo observado para a mesma seed sem alterar outros fatores");
        }),
        ("F4: entradas inválidas não avançam setor nem estado aleatório", () =>
        {
            var bridge = Promoted(13);
            bridge.StartFormula4Season();
            bridge.PrepareFormula4Race(1, Balanced());
            bridge.StartFormula4Race(1);
            var before = Snapshot(bridge).ActiveRace!;
            var bytes = bridge.ExportCheckpoint();
            Throws<ArgumentOutOfRangeException>(() => bridge.AdvanceFormula4Race(1, 1, 0));
            Throws<ArgumentOutOfRangeException>(() => bridge.AdvanceFormula4Race(1, 1, 4));
            Throws<ArgumentOutOfRangeException>(() => bridge.AdvanceFormula4Race(1, 1, 1, (Pace)99));
            Check(bytes.SequenceEqual(bridge.ExportCheckpoint()));
            Check(JsonSerializer.Serialize(before) ==
                JsonSerializer.Serialize(Snapshot(bridge).ActiveRace));
        }),
        ("F4: avanço participa por setor e bloqueia repetição/retroatividade", () =>
        {
            var bridge = Promoted(15);
            bridge.StartFormula4Season();
            bridge.PrepareFormula4Race(1, Balanced());
            bridge.StartFormula4Race(1);
            var sector1 = bridge.AdvanceFormula4Race(1, 1, 1, Pace.Atacar);
            var bytes = bridge.ExportCheckpoint();
            Throws<InvalidOperationException>(() => bridge.AdvanceFormula4Race(1, 1));
            Throws<InvalidOperationException>(() => bridge.AdvanceFormula4Race(1, 3));
            Check(bytes.SequenceEqual(bridge.ExportCheckpoint()));
            var sector2 = bridge.AdvanceFormula4Race(1, 2, 1, Pace.Conservar);
            Check(sector1.CompletedSectors == 1 && sector2.CompletedSectors == 2);
            Check(sector1.Classification.Single(row => row.IsPlayer).TireWear == 4);
            Check(sector2.Classification.Single(row => row.IsPlayer).TireWear == 5);
        }),
        ("F4: mesma seed e plano produzem os mesmos tempos e eventos", () =>
        {
            var a = SimulateFirstRace(991);
            var b = SimulateFirstRace(991);
            var c = SimulateFirstRace(992);
            Check(JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b));
            Check(JsonSerializer.Serialize(a.Events) == JsonSerializer.Serialize(b.Events));
            Check(JsonSerializer.Serialize(a) != JsonSerializer.Serialize(c));
        }),
        ("F4: pontos só aparecem na bandeirada e são próprios da categoria", () =>
        {
            var bridge = Promoted(31);
            bridge.StartFormula4Season();
            bridge.PrepareFormula4Race(1, Balanced());
            bridge.StartFormula4Race(1);
            for (var sector = 1; sector < Formula4SportRules.TotalSectors; sector++)
            {
                var snapshot = bridge.AdvanceFormula4Race(1, sector, 1);
                Check(snapshot.Classification.All(row => row.Points == 0));
            }
            var finish = bridge.AdvanceFormula4Race(1, Formula4SportRules.TotalSectors);
            Check(finish.Finished && finish.Classification.Count == 20);
            Check(finish.Classification.Sum(row => row.Points) == 140);
            Check(finish.Classification.Select(row => row.Points)
                .SequenceEqual(Enumerable.Range(1, 20).Select(Formula4SportRules.PointsFor)));
            Check(finish.Events.Count(item => item.Code == "bandeirada") == 1);
            Check(!finish.Events.Any(item => item.Code == "fim"));
            Throws<InvalidOperationException>(() => bridge.AdvanceFormula4Race(1, 46));
            Check(Snapshot(bridge).Rounds.Count == 1);
        }),
        ("F4: desgaste, confiabilidade e eventos são parte do motor determinístico", () =>
        {
            var result = SimulateFirstRace(55);
            Check(result.Classification.All(row => row.TireWear > 0 && row.TireWear <= 100));
            Check(result.Classification.All(row => row.MechanicalCondition >= 0 &&
                row.MechanicalCondition <= 100 && row.MechanicalIssues >= 0));
            Check(result.Events.Any(item => item.Code == "setor"));
            Check(result.Events.Any(item => item.Code == "ritmo"));
            Check(result.Events.Any(item => item.Code == "bandeirada"));
            Check(result.Events.All(item => !string.IsNullOrWhiteSpace(item.Explanation)));
        }),
        ("F4: classificação e desempates são estáveis e derivados dos resultados", () =>
        {
            var bridge = Promoted(88);
            bridge.StartFormula4Season();
            RunRound(bridge, 1, Balanced());
            var snapshot = Snapshot(bridge);
            Check(snapshot.DriverStandings.Count == 20 && snapshot.TeamStandings.Count == 10);
            Check(snapshot.DriverStandings.Select(row => row.Points)
                .SequenceEqual(snapshot.DriverStandings.Select(row => row.Points).OrderByDescending(value => value)));
            Check(snapshot.TeamStandings.Sum(row => row.Points) ==
                snapshot.DriverStandings.Sum(row => row.Points));
            Check(snapshot.DriverStandings.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() == 20);
        }),
        ("F4: temporada completa tem dez provas, pontua uma vez e encerra", () =>
        {
            var bridge = Promoted(2026);
            bridge.StartFormula4Season();
            for (var round = 1; round <= 10; round++)
            {
                RunRound(bridge, round, new Formula4Strategy(
                    Pace.Conservar, Pace.Equilibrado, Pace.Atacar));
                var snapshot = Snapshot(bridge);
                Check(snapshot.CompletedRounds == round);
                Check(snapshot.NextRound == round + 1);
                Check(snapshot.Finished == (round == 10));
            }
            var final = Snapshot(bridge);
            Check(final.Rounds.Select(round => round.Number).SequenceEqual(Enumerable.Range(1, 10)));
            Check(final.DriverStandings.Sum(row => row.Points) == 1400);
            Check(final.TeamStandings.Sum(row => row.Points) == 1400);
            Check(final.DriverStandings.Sum(row => row.Wins) == 10);
            Check(final.TeamStandings.Sum(row => row.Wins) == 10);
            var finishedBytes = bridge.ExportCheckpoint();
            Throws<InvalidOperationException>(() => bridge.PrepareFormula4Race(10, Balanced()));
            Check(finishedBytes.SequenceEqual(bridge.ExportCheckpoint()));
        }),
        ("F4: save v4 retoma setor, PRNG, eventos, estratégia e checkpoint idênticos", () =>
        {
            var bridge = Promoted(404);
            bridge.StartFormula4Season();
            bridge.PrepareFormula4Race(1,
                new Formula4Strategy(Pace.Conservar, Pace.Atacar, Pace.Equilibrado, 12, 29));
            bridge.StartFormula4Race(1);
            for (var sector = 1; sector <= 9; sector++)
                bridge.AdvanceFormula4Race(1, sector, 1, sector == 4 ? Pace.Atacar : null);
            var beforeBytes = bridge.ExportCheckpoint();
            Check(CareerJourneyBridge.GetCheckpointFormatVersion(beforeBytes) == 4);
            var before = Snapshot(bridge);
            var restored = CareerJourneyBridge.LoadCheckpoint(beforeBytes);
            Check(beforeBytes.SequenceEqual(restored.ExportCheckpoint()));
            Check(JsonSerializer.Serialize(before) ==
                JsonSerializer.Serialize(Snapshot(restored)));
            var expected = bridge.AdvanceFormula4Race(1, 10, 3);
            var actual = restored.AdvanceFormula4Race(1, 10, 3);
            Check(JsonSerializer.Serialize(expected) == JsonSerializer.Serialize(actual));
            Check(bridge.ExportCheckpoint().SequenceEqual(restored.ExportCheckpoint()));
        }),
        ("F4: save/reload entre etapas conserva histórico e avança só para a próxima", () =>
        {
            var bridge = Promoted(1001);
            bridge.StartFormula4Season();
            RunRound(bridge, 1, Balanced());
            var firstCheckpoint = bridge.ExportCheckpoint();
            var firstHistory = JsonSerializer.Serialize(Snapshot(bridge).Rounds);
            var restored = CareerJourneyBridge.LoadCheckpoint(firstCheckpoint);
            Check(firstHistory == JsonSerializer.Serialize(Snapshot(restored).Rounds));
            Check(Snapshot(restored).NextRound == 2);
            Throws<InvalidOperationException>(() => restored.PrepareFormula4Race(1, Balanced()));
            RunRound(bridge, 2, Balanced());
            RunRound(restored, 2, Balanced());
            Check(bridge.ExportCheckpoint().SequenceEqual(restored.ExportCheckpoint()));
        }),
        ("F4: save/reload de temporada encerrada preserva campeão, tabela e Kart", () =>
        {
            var bridge = Promoted(7);
            bridge.StartFormula4Season();
            var kartHistory = JsonSerializer.Serialize(bridge.ReadHistory());
            for (var round = 1; round <= 10; round++) RunRound(bridge, round, Balanced());
            var completed = Snapshot(bridge);
            var checkpoint = bridge.ExportCheckpoint();
            var restored = CareerJourneyBridge.LoadCheckpoint(checkpoint);
            Check(completed.Finished && Snapshot(restored).Finished);
            Check(JsonSerializer.Serialize(completed) ==
                JsonSerializer.Serialize(Snapshot(restored)));
            Check(kartHistory == JsonSerializer.Serialize(restored.ReadHistory()));
            Check(checkpoint.SequenceEqual(restored.ExportCheckpoint()));
        }),
        ("F4: v1/v2 Kart e promoção v3 continuam legíveis e preservados", () =>
        {
            var v1Bridge = CompleteKartBridge(202, false);
            var v1 = v1Bridge.ExportCheckpoint();
            Check(CareerJourneyBridge.GetCheckpointFormatVersion(v1) == 1);
            v1Bridge.AcceptPromotion("f4t05");
            var v3v1 = v1Bridge.ExportCheckpoint();
            Check(CareerJourneyBridge.GetCheckpointFormatVersion(v3v1) == 3);
            Check(v3v1.SequenceEqual(CareerJourneyBridge.LoadCheckpoint(v3v1).ExportCheckpoint()));
            v1Bridge.StartFormula4Season();
            Check(CareerJourneyBridge.GetCheckpointFormatVersion(v1Bridge.ExportCheckpoint()) == 4);

            var v2Bridge = CompleteKartBridge(203, true);
            var v2 = v2Bridge.ExportCheckpoint();
            Check(CareerJourneyBridge.GetCheckpointFormatVersion(v2) == 2);
            var oldHistory = JsonSerializer.Serialize(v2Bridge.ReadHistory());
            v2Bridge.AcceptPromotion("f4t06");
            v2Bridge.StartFormula4Season();
            var v4 = v2Bridge.ExportCheckpoint();
            var loaded = CareerJourneyBridge.LoadCheckpoint(v4);
            Check(CareerJourneyBridge.GetCheckpointFormatVersion(v4) == 4);
            Check(oldHistory == JsonSerializer.Serialize(loaded.ReadHistory()));
        }),
        ("F4: checkpoint corrompido rejeitado sem alterar origem", () =>
        {
            var bridge = Promoted(313);
            bridge.StartFormula4Season();
            bridge.PrepareFormula4Race(1, Balanced());
            bridge.StartFormula4Race(1);
            bridge.AdvanceFormula4Race(1, 1, 1, Pace.Atacar);
            var original = bridge.ExportCheckpoint();
            var corrupt = (byte[])original.Clone();
            corrupt[corrupt.Length / 2] ^= 0x10;
            Throws<InvalidDataException>(() => CareerJourneyBridge.LoadCheckpoint(corrupt));
            Check(original.SequenceEqual(bridge.ExportCheckpoint()));
        }),
        ("F4: regras, pontos e comandos ficam isolados das corridas Kart", () =>
        {
            var kart = new CareerSession(77,
                new CareerProfile("Marina Costa", "Brasil", CareerOrigin.LigadaAoEsporte, "t03"));
            kart.Prepare(1, Training.Descanso, Pace.Equilibrado);
            kart.RunNext(1);
            var kartResult = JsonSerializer.Serialize(kart.Rounds[0].Race.Result);
            var f4 = Promoted(77);
            f4.StartFormula4Season();
            RunRound(f4, 1, Balanced());
            Check(JsonSerializer.Serialize(kart.Rounds[0].Race.Result) == kartResult);
            Check(Snapshot(f4).Rounds[0].Result.RandomVersion == SeededRandom.Version);
            Throws<InvalidOperationException>(() => f4.StartRace(9));
            Check(Snapshot(f4).Rounds[0].Result.Classification.Sum(row => row.Points) == 140);
        }),
        ("Ponte Web F4: criar season, iniciar, avançar, recarregar e rejeitar repetição", () =>
        {
            var promoted = CreatePromotedBrowserReply();
            var start = BrowserCareerScenario.Dispatch(new BrowserCareerRequest
            {
                Operation = "startFormula4Season", Checkpoint = promoted.Checkpoint
            });
            Check(start.Ok && start.Formula4 != null && start.CheckpointFormatVersion == 4,
                "iniciar temporada F4; resposta: " + start.Error + " v" + start.CheckpointFormatVersion);
            Check(start.Formula4!.Calendar.Count == 10 && start.Formula4.Drivers.Count == 20,
                "catálogo F4 retornado pela ponte");
            var prepared = BrowserCareerScenario.Dispatch(new BrowserCareerRequest
            {
                Operation = "prepareFormula4Race", Checkpoint = start.Checkpoint, ExpectedRound = 1,
                BeginningPace = nameof(Pace.Conservar), MiddlePace = nameof(Pace.Equilibrado),
                FinalPace = nameof(Pace.Atacar), MiddleStartSector = 16, FinalStartSector = 31
            });
            Check(prepared.Ok && prepared.Formula4!.Prepared, "preparação F4");
            var started = BrowserCareerScenario.Dispatch(new BrowserCareerRequest
            {
                Operation = "startFormula4Race", Checkpoint = prepared.Checkpoint, ExpectedRound = 1
            });
            Check(started.Ok && started.Formula4!.RaceActive, "início da corrida F4");
            var advanced = BrowserCareerScenario.Dispatch(new BrowserCareerRequest
            {
                Operation = "advanceFormula4Race", Checkpoint = started.Checkpoint, ExpectedRound = 1,
                ExpectedSector = 1, Count = 3, Pace = nameof(Pace.Atacar)
            });
            Check(advanced.Ok && advanced.Formula4!.ActiveRace!.CompletedSectors == 3,
                "avanço de três setores F4");
            var loaded = BrowserCareerScenario.Dispatch(new BrowserCareerRequest
            {
                Operation = "load", Checkpoint = advanced.Checkpoint
            });
            Check(loaded.Ok && JsonSerializer.Serialize(advanced.Formula4) ==
                JsonSerializer.Serialize(loaded.Formula4), "reload do snapshot F4");
            var duplicate = BrowserCareerScenario.Dispatch(new BrowserCareerRequest
            {
                Operation = "advanceFormula4Race", Checkpoint = loaded.Checkpoint,
                ExpectedRound = 1, ExpectedSector = 3, Count = 1
            });
            Check(!duplicate.Ok && loaded.Checkpoint == advanced.Checkpoint,
                "rejeição idempotente de setor duplicado");
        })
    };

    private static Formula4Strategy Balanced() =>
        new Formula4Strategy(Pace.Equilibrado, Pace.Equilibrado, Pace.Equilibrado);

    private static CareerJourneyBridge Promoted(uint seed, string teamId = "f4t03")
    {
        var key = seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + teamId;
        if (PromotionCache.TryGetValue(key, out var saved))
            return CareerJourneyBridge.LoadCheckpoint((byte[])saved.Clone());
        var bridge = CareerJourneyBridge.Create(seed, "Marina Costa", "Brasil",
            CareerOrigin.LigadaAoEsporte, "t03");
        CompleteKart(bridge);
        bridge.AcceptPromotion(teamId);
        var checkpoint = bridge.ExportCheckpoint();
        PromotionCache.Add(key, checkpoint);
        return CareerJourneyBridge.LoadCheckpoint((byte[])checkpoint.Clone());
    }

    private static CareerJourneyBridge CompleteKartBridge(uint seed, bool participatory)
    {
        var bridge = CareerJourneyBridge.Create(seed, "Marina Costa", "Brasil",
            CareerOrigin.LigadaAoEsporte, "t03");
        CompleteKart(bridge, participatory);
        return bridge;
    }

    private static void CompleteKart(CareerJourneyBridge bridge, bool participatory = false)
    {
        for (var round = 1; round <= InitialData.KartCalendar.Count; round++)
        {
            bridge.PrepareSingle(round, Training.Descanso, Pace.Equilibrado);
            if (!participatory)
            {
                bridge.RunNext(round);
                continue;
            }
            bridge.StartRace(round);
            for (var sector = 1; sector <= KartRules.DefaultLaps * 3; sector++)
                bridge.AdvanceRace(round, sector, 1, sector % 7 == 0 ? Pace.Atacar : null);
        }
    }

    private static BrowserCareerReply CreatePromotedBrowserReply()
    {
        var bridge = Promoted(22);
        return new BrowserCareerReply
        {
            Ok = true,
            Checkpoint = Convert.ToBase64String(bridge.ExportCheckpoint()),
            CheckpointFormatVersion = CareerJourneyBridge.GetCheckpointFormatVersion(bridge.ExportCheckpoint())
        };
    }

    private static void RunRound(CareerJourneyBridge bridge, int round, Formula4Strategy strategy)
    {
        bridge.PrepareFormula4Race(round, strategy);
        bridge.StartFormula4Race(round);
        for (var sector = 1; sector <= Formula4SportRules.TotalSectors; sector += 3)
            bridge.AdvanceFormula4Race(round, sector, Math.Min(3,
                Formula4SportRules.TotalSectors - sector + 1));
    }

    private static Formula4RaceSnapshot FirstSector(uint seed, Pace pace)
    {
        var bridge = Promoted(seed);
        bridge.StartFormula4Season();
        bridge.PrepareFormula4Race(1, Balanced());
        bridge.StartFormula4Race(1);
        return bridge.AdvanceFormula4Race(1, 1, 1, pace);
    }

    private static Formula4RaceSnapshot SimulateFirstRace(uint seed)
    {
        var bridge = Promoted(seed);
        bridge.StartFormula4Season();
        bridge.PrepareFormula4Race(1,
            new Formula4Strategy(Pace.Conservar, Pace.Equilibrado, Pace.Atacar));
        bridge.StartFormula4Race(1);
        for (var sector = 1; sector <= Formula4SportRules.TotalSectors; sector++)
            bridge.AdvanceFormula4Race(1, sector, 1,
                sector % 10 == 0 ? Pace.Conservar : null);
        return Snapshot(bridge).Rounds[0].Result;
    }

    private static Formula4ChampionshipSnapshot Snapshot(CareerJourneyBridge bridge) =>
        bridge.ReadFormula4Championship() ??
        throw new Exception("Snapshot F4 ausente após iniciar o motor da categoria.");

    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("Invariante do motor F4 não atendida.");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("Invariante do motor F4 não atendida: " + label + ".");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Esperava " + typeof(T).Name);
    }
}
