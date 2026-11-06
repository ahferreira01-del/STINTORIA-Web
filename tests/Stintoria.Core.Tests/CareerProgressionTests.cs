using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Stintoria.ClientBridge;
using Stintoria.Core;

internal static class CareerProgressionTests
{
    private static CareerSession? completedKart;

    internal static (string Name, Action Body)[] Cases() => new (string Name, Action Body)[]
    {
        ("Progressão: somente Kart e F4 têm motor nesta etapa", () =>
        {
            Check(InitialData.Categories.Select(category => category.Rounds)
                .SequenceEqual(new[] { 8, 10, 12, 14, 16, 20 }));
            Check(InitialData.Categories.Count(category => category.Implemented) == 2);
            Check(InitialData.Categories.Single(category => category.Id == "f4").Implemented);
            Check(Formula4Data.Teams.Count == 10 && Formula4Data.Drivers.Count == 20 &&
                Formula4Data.Calendar.Count == 10);
            Check(!Formula4Data.Drivers.Any(driver =>
                InitialData.Drivers.Any(kart => kart.Id == driver.Id || kart.Name == driver.Name)));
            KartRules.ValidateGrid(Formula4Data.Drivers, Formula4Data.Teams);
        }),
        ("Progressão: bloqueia oferta antes das oito etapas e preserva checkpoint legado", () =>
        {
            var bridge = CareerJourneyBridge.Create(42, "Marina Costa", "Brasil",
                CareerOrigin.LigadaAoEsporte, "t03");
            var before = bridge.ExportCheckpoint();
            Check(CareerJourneyBridge.GetCheckpointFormatVersion(before) == 1);
            Check(before.SequenceEqual(ClientCheckpoint.Encode(
                ClientCheckpoint.Decode(before))));
            Check(before.SequenceEqual(CareerJourneyBridge.LoadCheckpoint(before).ExportCheckpoint()));
            var snapshot = bridge.ReadProgression();
            Check(!snapshot.Eligible && !snapshot.OfferAvailable && snapshot.Decision == PromotionDecision.None);
            Throws<InvalidOperationException>(() => bridge.AcceptPromotion(Formula4Data.Teams[0].Id));
            Throws<InvalidOperationException>(() => bridge.DeclinePromotion());
            Check(before.SequenceEqual(bridge.ExportCheckpoint()));
        }),
        ("Progressão: temporada completa gera oferta sem exigir título", () =>
        {
            var progression = new CareerProgression(CompleteKart(42));
            var snapshot = progression.ReadSnapshot();
            Check(progression.IsEligible && snapshot.Eligible && snapshot.OfferAvailable);
            Check(snapshot.EligibilityRule.Contains("não exige título", StringComparison.Ordinal));
            Check(snapshot.CurrentCategoryId == "kart" && snapshot.Decision == PromotionDecision.None);
            Check(snapshot.Seasons.Count == 1 &&
                snapshot.Seasons[0].Status == CareerSeasonStatus.Concluida &&
                snapshot.Seasons[0].CompletedRounds == 8 &&
                snapshot.Seasons[0].History is { Finished: true, CompletedRounds: 8, TotalRounds: 8 });
        }),
            ("Progressão: aceitação cria estrutura F4 pendente e preserva identidade e histórico Kart", () =>
        {
            var kartCareer = CompleteKart(123);
            var progression = new CareerProgression(kartCareer);
            var kart = JsonSerializer.Serialize(HistoryQuery.ForCareer(kartCareer));
            var profile = kartCareer.Profile;
            Check(profile.Age == 14);
            progression.Accept(Formula4Data.Teams[3].Id);
            var snapshot = progression.ReadSnapshot();
            Check(snapshot.CurrentCategoryId == "f4" && snapshot.CurrentTeamId == Formula4Data.Teams[3].Id);
            Check(snapshot.Decision == PromotionDecision.Accepted && !snapshot.OfferAvailable);
            Check(snapshot.Seasons.Count == 2 && snapshot.TeamHistory.Count == 2);
            var formula4 = snapshot.Seasons.Single(season => season.CategoryId == "f4");
            Check(formula4.Status == CareerSeasonStatus.AguardandoMotorDaCategoria &&
                formula4.TotalRounds == 10 && formula4.CompletedRounds == 0);
            Check(formula4.Calendar.Select(round => round.Number).SequenceEqual(Enumerable.Range(1, 10)));
            Check(formula4.Participants.Count == 20 &&
                formula4.Participants.Count(driver => driver.IsPlayer) == 1);
            var player = formula4.Participants.Single(driver => driver.IsPlayer);
            Check(player.Id == CareerProfile.PlayerId && player.Name == profile.Name &&
                player.TeamId == Formula4Data.Teams[3].Id && profile.Age == 14);
            Check(JsonSerializer.Serialize(HistoryQuery.ForCareer(kartCareer)) == kart);
            var promotedBridge = CareerJourneyBridge.LoadCheckpoint(
                CareerProgressionCheckpoint.Encode(progression));
            Check(promotedBridge.ReadPreparationOptions(9).Count == 0);
            Throws<InvalidOperationException>(() => promotedBridge.StartRace(9));
            Throws<InvalidOperationException>(() =>
                promotedBridge.PrepareSingle(9, Training.Descanso, Pace.Equilibrado));
        }),
        ("Progressão: recusa mantém histórico Kart e impede segunda decisão", () =>
        {
            var declinedKart = CompleteKart(9);
            var kartHistory = JsonSerializer.Serialize(HistoryQuery.ForCareer(declinedKart));
            var declined = new CareerProgression(declinedKart);
            declined.Decline();
            var declinedSnapshot = declined.ReadSnapshot();
            Check(declinedSnapshot.Decision == PromotionDecision.Declined &&
                !declinedSnapshot.OfferAvailable && declinedSnapshot.CurrentCategoryId == "kart" &&
                declinedSnapshot.Seasons.Count == 1);
            Check(JsonSerializer.Serialize(HistoryQuery.ForCareer(declinedKart)) == kartHistory);
            Throws<InvalidOperationException>(() => declined.Decline());
            Throws<InvalidOperationException>(() => declined.Accept(Formula4Data.Teams[0].Id));
        }),
        ("Progressão: decisões inválidas não aceitam equipe ou categoria arbitrária", () =>
        {
            var progression = new CareerProgression(CompleteKart(55));
            Throws<ArgumentException>(() => progression.Accept("t01"));
            Throws<ArgumentException>(() => progression.Accept("f4-equipe-inexistente"));
            var snapshot = progression.ReadSnapshot();
            Check(snapshot.Decision == PromotionDecision.None && snapshot.CurrentCategoryId == "kart" &&
                snapshot.OfferAvailable);
            Check(snapshot.Categories.All(category => category.Id == "kart" || category.Id == "f4" ||
                category.Id == "regional" || category.Id == "f3" || category.Id == "f2" ||
                category.Id == "principal"));
        })
    };

    private static CareerSession CompleteKart(uint seed)
    {
        if (completedKart == null)
        {
            var career = new CareerSession(seed, new CareerProfile("Marina Costa", "Brasil",
                CareerOrigin.LigadaAoEsporte, "t03"));
            for (var round = 1; round <= InitialData.KartCalendar.Count; round++)
            {
                career.Prepare(round, Training.Descanso, Pace.Equilibrado);
                career.RunNext(round);
            }
            completedKart = career;
        }
        return completedKart;
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("Invariante da progressão multicategoria não atendida.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Esperava " + typeof(T).Name);
    }
}
