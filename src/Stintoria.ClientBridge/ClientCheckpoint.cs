using System;
using System.IO;
using System.Linq;
using System.Text;
using Stintoria.Core;

namespace Stintoria.ClientBridge
{
    /// <summary>Formato próprio do cliente: ledger + fingerprints oficiais, sem dependência de JSON/.NET 8.</summary>
    public static class ClientCheckpoint
    {
        public const int MaxBytes = 65536;
        private const string Magic = "STINTORIA-CLIENTE-1";
        private const string LiveMagic = "STINTORIA-CLIENTE-2";
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public static byte[] Encode(CareerSession career)
        {
            if (career == null) throw new ArgumentNullException(nameof(career));
            var live = HasParticipation(career);
            using var payload = new MemoryStream();
            using (var w = new BinaryWriter(payload, Utf8, true)) {
                foreach (var version in Versions(live)) Text(w, version);
                w.Write(career.Seed); Text(w, career.Profile.Name); Text(w, career.Profile.Nationality);
                w.Write((int)career.Profile.Origin); Text(w, career.Profile.TeamId);
                w.Write(career.Rounds.Count);
                foreach (var round in career.Rounds) {
                    Choice(w, round.Preparation);
                    if (live) { w.Write(round.GetLiveDecisions() != null); if (round.GetLiveDecisions() != null) Decisions(w, round.GetLiveDecisions()!); }
                    w.Write(CheckpointFingerprint.Round(round));
                }
                w.Write(career.Pending != null); if (career.Pending != null) Choice(w, career.Pending);
                if (live) {
                    var active = career.GetActiveRace(); w.Write(active != null);
                    if (active != null) {
                        w.Write(active.State.CompletedSectors); w.Write(active.RandomState);
                        Decisions(w, active.Decisions); w.Write(CheckpointFingerprint.Active(active));
                    }
                }
                w.Write(CheckpointFingerprint.State(career));
            }
            var data = payload.ToArray();
            if (data.Length + 32 > MaxBytes) throw Invalid();
            return data.Concat(CheckpointFingerprint.Hash(data)).ToArray();
        }
        public static byte[] EncodeLegacy(CareerSession career)
        {
            if (career == null) throw new ArgumentNullException(nameof(career));
            if (HasParticipation(career))
                throw new InvalidOperationException("Checkpoint legado v1 não suporta corrida participativa. Use o formato v2 sem remover decisões.");
            return Encode(career);
        }
        public static int GetFormatVersion(byte[] checkpoint)
        {
            if (checkpoint == null || checkpoint.Length < 64 || checkpoint.Length > MaxBytes) throw Invalid();
            var data = checkpoint.Take(checkpoint.Length - 32).ToArray();
            if (!CheckpointFingerprint.Hash(data).SequenceEqual(checkpoint.Skip(data.Length))) throw Invalid();
            using var stream = new MemoryStream(data, false);
            using var reader = new BinaryReader(stream, Utf8);
            var magic = ReadText(reader);
            if (magic == Magic) return 1;
            if (magic == LiveMagic) return 2;
            throw Invalid();
        }
        private static bool HasParticipation(CareerSession c) =>
            c.GetActiveRace() != null || c.Rounds.Any(round => round.GetLiveDecisions() != null);
        public static CareerSession Decode(byte[] checkpoint)
        {
            if (checkpoint == null || checkpoint.Length < 64 || checkpoint.Length > MaxBytes) throw Invalid();
            try {
                var data = checkpoint.Take(checkpoint.Length - 32).ToArray();
                if (!CheckpointFingerprint.Hash(data).SequenceEqual(checkpoint.Skip(data.Length))) throw Invalid();
                using var stream = new MemoryStream(data, false);
                using var r = new BinaryReader(stream, Utf8);
                var magic = ReadText(r);
                if (magic != Magic && magic != LiveMagic) throw Invalid();
                var live = magic == LiveMagic;
                foreach (var expected in Versions(live).Skip(1)) if (ReadText(r) != expected) throw Invalid();
                var seed = r.ReadUInt32(); var name = ReadText(r); var nationality = ReadText(r);
                var origin = (CareerOrigin)r.ReadInt32(); var team = ReadText(r);
                var career = new CareerSession(seed, new CareerProfile(name, nationality, origin, team));
                var count = r.ReadInt32(); if (count < 0 || count > 8) throw Invalid();
                for (var number = 1; number <= count; number++) {
                    Apply(r, career, number);
                    if (live && Boolean(r)) Replay(r, career, 36);
                    else career.RunNext(number);
                    var official = career.Rounds[number - 1];
                    if (!Bytes(r, 32).SequenceEqual(CheckpointFingerprint.Round(official))) throw Invalid();
                }
                if (Boolean(r)) Apply(r, career, career.NextRound);
                if (live && Boolean(r)) {
                    var sectors = r.ReadInt32(); var random = r.ReadUInt32();
                    if (sectors < 0 || sectors >= 36) throw Invalid();
                    Replay(r, career, sectors);
                    var active = career.GetActiveRace()!;
                    if (active.RandomState != random || !Bytes(r, 32).SequenceEqual(CheckpointFingerprint.Active(active))) throw Invalid();
                }
                if (HasParticipation(career) != live) throw Invalid();
                if (!Bytes(r, 32).SequenceEqual(CheckpointFingerprint.State(career)) || stream.Position != stream.Length) throw Invalid();
                return career;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidOperationException || e is OverflowException)
            { throw Invalid(); }
        }
        private static string[] Versions(bool live) => new[] {live ? LiveMagic : Magic, CareerSession.Version, Championship.Version,
            Championship.DataVersion, Championship.SeedVersion, RaceState.SimulationVersion,
            SeededRandom.Version, RaceStrategy.Version, AiTactics.Version};
        private static void Decisions(BinaryWriter w, System.Collections.Generic.IReadOnlyList<SectorDecision> choices)
        {
            w.Write(choices.Count);
            foreach (var d in choices) { w.Write(d.Sector); w.Write((int)d.Pace); }
        }
        private static void Replay(BinaryReader r, CareerSession c, int sectors)
        {
            var count = r.ReadInt32(); if (count < 0 || count > sectors) throw Invalid();
            var choices = new System.Collections.Generic.Dictionary<int, Pace>();
            var previous = 0;
            for (var i = 0; i < count; i++) {
                var sector = r.ReadInt32(); var pace = (Pace)r.ReadInt32();
                if (sector <= previous || sector > sectors || !Enum.IsDefined(typeof(Pace), pace)) throw Invalid();
                choices.Add(sector, pace); previous = sector;
            }
            c.StartRace(c.NextRound);
            for (var sector = 1; sector <= sectors; sector++)
                c.AdvanceRace(c.NextRound, sector, 1, choices.TryGetValue(sector, out var pace) ? pace : (Pace?)null);
        }
        private static void Choice(BinaryWriter w, CareerPreparation p)
        {
            w.Write((int)p.Training); w.Write((int)p.Pace);
            var plan = p.GetStrategy(); w.Write(plan != null);
            if (plan != null) {
                w.Write(plan.MiddleStartSector); w.Write(plan.FinalStartSector);
                w.Write((int)plan.Beginning); w.Write((int)plan.Middle); w.Write((int)plan.Final);
            }
            w.Write(p.GetOpponentPlans().Count != 0);
        }
        private static void Apply(BinaryReader r, CareerSession c, int round)
        {
            var training = (Training)r.ReadInt32(); var pace = (Pace)r.ReadInt32();
            RaceStrategy? plan = null;
            if (Boolean(r)) {
                var middle = r.ReadInt32(); var final = r.ReadInt32();
                plan = new RaceStrategy((Pace)r.ReadInt32(), (Pace)r.ReadInt32(), (Pace)r.ReadInt32(), middle, final);
                if (pace != plan.Beginning) throw Invalid();
            }
            var ai = Boolean(r);
            if (ai && plan == null) throw Invalid();
            if (plan == null) c.Prepare(round, training, pace);
            else if (ai) c.PrepareTactical(round, training, plan);
            else c.PrepareStrategy(round, training, plan);
        }
        private static bool Boolean(BinaryReader r) { var b = r.ReadByte(); if (b > 1) throw Invalid(); return b == 1; }
        private static void Text(BinaryWriter w, string text)
        { var bytes = Utf8.GetBytes(text); w.Write(bytes.Length); w.Write(bytes); }
        private static string ReadText(BinaryReader r)
        { var size = r.ReadInt32(); if (size < 1 || size > 256) throw Invalid(); return Utf8.GetString(Bytes(r, size)); }
        private static byte[] Bytes(BinaryReader r, int count)
        { var bytes = r.ReadBytes(count); return bytes.Length == count ? bytes : throw Invalid(); }
        private static InvalidDataException Invalid() => new InvalidDataException(
            "Checkpoint do cliente inválido, corrompido ou incompatível. Nenhuma carreira foi substituída.");
    }
}
