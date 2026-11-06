using System;
using System.IO;
using System.Security.Cryptography;
using Stintoria.Core;

namespace Stintoria.ClientBridge
{
    internal static class CheckpointFingerprint
    {
        internal static byte[] Hash(byte[] bytes) { using var sha = SHA256.Create(); return sha.ComputeHash(bytes); }
        internal static byte[] Active(ParticipatoryRace active) => Digest(w => {
            var state = active.State;
            w.Write(active.RandomState); w.Write(state.Seed); w.Write(state.RandomVersion);
            w.Write(state.CircuitId); w.Write(state.CompletedSectors); w.Write(state.TotalLaps); w.Write((int)state.Weather);
            w.Write(active.Decisions.Count);
            foreach (var d in active.Decisions) { w.Write(d.Sector); w.Write(d.DriverId); w.Write((int)d.Pace); }
            foreach (var d in state.Classification) {
                w.Write(d.DriverId); w.Write(d.Position); w.Write(d.ElapsedMs);
                w.Write(d.TireWear); w.Write((int)d.Pace); w.Write(d.Points);
            }
            w.Write(state.Events.Count);
            foreach (var e in state.Events) { w.Write(e.Step); w.Write(e.DriverId); w.Write(e.Code); w.Write(e.Explanation); }
        });
        internal static byte[] Round(CareerRound round) => Digest(w => {
            w.Write(round.Race.Number); Preparation(w, round.Preparation);
            w.Write(round.Race.Decisions.Count);
            foreach (var d in round.Race.Decisions) { w.Write(d.Sector); w.Write(d.DriverId); w.Write((int)d.Pace); }
            var r = round.Race.Result;
            w.Write(r.Seed); w.Write(r.RandomVersion); w.Write(r.CircuitId); w.Write(r.CompletedSectors);
            w.Write(r.TotalLaps); w.Write((int)r.Weather);
            w.Write(r.Classification.Count);
            foreach (var d in r.Classification) {
                w.Write(d.DriverId); w.Write(d.Position); w.Write(d.ElapsedMs); w.Write(d.TireWear);
                w.Write((int)d.Pace); w.Write(d.Points);
            }
            w.Write(r.Events.Count);
            foreach (var e in r.Events) { w.Write(e.Step); w.Write(e.DriverId); w.Write(e.Code); w.Write(e.Explanation); }
        });
        internal static byte[] State(CareerSession c) => Digest(w => {
            w.Write(c.Seed); w.Write(c.Profile.Name); w.Write(c.Profile.Nationality); w.Write(c.Profile.Age);
            w.Write((int)c.Profile.Origin); w.Write(c.Profile.TeamId); w.Write(c.Profile.DisplacedDriverId);
            w.Write(c.Skill); w.Write(c.TrainingCredits); w.Write(c.NextRound); w.Write(c.Finished);
            w.Write(c.Rounds.Count);
            foreach (var r in c.Rounds) w.Write(Round(r));
            w.Write(c.Pending != null); if (c.Pending != null) Preparation(w, c.Pending);
            foreach (var table in new[] {c.DriverStandings, c.TeamStandings})
                foreach (var row in table) {
                    w.Write(row.Id); w.Write(row.Points);
                    w.Write(row.Finishes.Count);
                    foreach (var value in row.Finishes) w.Write(value);
                    w.Write(row.RecentPositions.Count);
                    foreach (var value in row.RecentPositions) w.Write(value);
                }
        });
        private static void Preparation(BinaryWriter w, CareerPreparation p)
        {
            w.Write(p.Round); w.Write((int)p.Training); w.Write((int)p.Pace);
            w.Write(p.SkillApplied); w.Write(p.CreditsRemaining);
            Plan(w, p.GetStrategy());
            w.Write(p.GetOpponentPlans().Count);
            foreach (var ai in p.GetOpponentPlans()) {
                w.Write(ai.DriverId); w.Write((int)ai.Profile); w.Write(ai.ObservedSkill);
                w.Write((int)ai.ObservedWeather); w.Write(ai.Reason); Plan(w, ai.Strategy);
            }
        }
        private static void Plan(BinaryWriter w, RaceStrategy? p)
        {
            w.Write(p != null); if (p == null) return;
            w.Write(p.MiddleStartSector); w.Write(p.FinalStartSector);
            w.Write((int)p.Beginning); w.Write((int)p.Middle); w.Write((int)p.Final);
        }
        private static byte[] Digest(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) write(writer);
            return Hash(stream.ToArray());
        }
    }
}
