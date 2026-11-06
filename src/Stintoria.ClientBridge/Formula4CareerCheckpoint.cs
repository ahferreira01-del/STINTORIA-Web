using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Stintoria.Core;

namespace Stintoria.ClientBridge
{
    internal sealed class Formula4CareerCheckpointState
    {
        internal CareerProgression Progression { get; }
        internal Formula4Championship Season { get; }
        internal byte[] PromotionCheckpoint { get; }
        internal Formula4CareerCheckpointState(CareerProgression progression,
            Formula4Championship season, byte[] promotionCheckpoint)
        {
            Progression = progression;
            Season = season;
            PromotionCheckpoint = (byte[])promotionCheckpoint.Clone();
        }
    }

    /// <summary>
    /// Envelope opt-in v4. O checkpoint v3 de promoção permanece como payload interno,
    /// sem reserializar o histórico Kart.
    /// </summary>
    public static class Formula4CareerCheckpoint
    {
        private const string Magic = "STINTORIA-CARREIRA-F4-4";
        private const string SeasonMagic = "STINTORIA-F4-SEASON-1";
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        internal static byte[] Encode(CareerProgression progression, byte[] promotionCheckpoint,
            Formula4Championship season)
        {
            if (progression == null || promotionCheckpoint == null || season == null)
                throw new ArgumentNullException("Estado F4 incompleto.");
            if (!progression.IsPromoted || progression.Formula4TeamId != season.TeamId ||
                progression.KartCareer.Seed != season.Seed)
                throw new InvalidOperationException("Temporada F4 não pertence a esta carreira promovida.");
            var baseProgression = CareerProgressionCheckpoint.Decode(promotionCheckpoint);
            if (!baseProgression.IsPromoted ||
                baseProgression.Formula4TeamId != progression.Formula4TeamId ||
                baseProgression.KartCareer.Seed != progression.KartCareer.Seed)
                throw new InvalidDataException("Checkpoint v3 interno não corresponde à promoção.");
            var seasonBytes = EncodeSeason(season);
            using var payload = new MemoryStream();
            using (var writer = new BinaryWriter(payload, Utf8, true))
            {
                WriteText(writer, Magic);
                WriteText(writer, CareerProgression.Version);
                WriteText(writer, Formula4Data.DataVersion);
                WriteText(writer, Formula4SportRules.Version);
                WriteBytes(writer, promotionCheckpoint);
                WriteBytes(writer, seasonBytes);
            }
            var data = payload.ToArray();
            if (data.Length + 32 > ClientCheckpoint.MaxBytes)
                throw new InvalidDataException("Checkpoint da temporada F4 excede o limite permitido.");
            return data.Concat(CheckpointFingerprint.Hash(data)).ToArray();
        }

        public static bool IsEnvelope(byte[]? checkpoint)
        {
            if (checkpoint == null || checkpoint.Length < 64) return false;
            try
            {
                using var stream = new MemoryStream(checkpoint, 0, checkpoint.Length - 32, false);
                using var reader = new BinaryReader(stream, Utf8);
                return ReadText(reader) == Magic;
            }
            catch (Exception exception) when (exception is IOException ||
                exception is ArgumentException || exception is DecoderFallbackException)
            {
                return false;
            }
        }

        public static int GetFormatVersion(byte[] checkpoint)
        {
            if (IsEnvelope(checkpoint))
            {
                Decode(checkpoint);
                return 4;
            }
            return CareerProgressionCheckpoint.GetFormatVersion(checkpoint);
        }

        internal static Formula4CareerCheckpointState Decode(byte[] checkpoint)
        {
            if (checkpoint == null || checkpoint.Length < 64 ||
                checkpoint.Length > ClientCheckpoint.MaxBytes) throw Invalid();
            try
            {
                var data = checkpoint.Take(checkpoint.Length - 32).ToArray();
                if (!CheckpointFingerprint.Hash(data).SequenceEqual(checkpoint.Skip(data.Length)))
                    throw Invalid();
                using var stream = new MemoryStream(data, false);
                using var reader = new BinaryReader(stream, Utf8);
                if (ReadText(reader) != Magic ||
                    ReadText(reader) != CareerProgression.Version ||
                    ReadText(reader) != Formula4Data.DataVersion ||
                    ReadText(reader) != Formula4SportRules.Version)
                    throw Invalid();
                var promotionBytes = ReadBytes(reader, ClientCheckpoint.MaxBytes);
                var seasonBytes = ReadBytes(reader, ClientCheckpoint.MaxBytes);
                if (stream.Position != stream.Length) throw Invalid();
                var progression = CareerProgressionCheckpoint.Decode(promotionBytes);
                if (!progression.IsPromoted) throw Invalid();
                var season = DecodeSeason(seasonBytes, progression);
                progression.RestoreFormula4Season(season);
                return new Formula4CareerCheckpointState(progression, season, promotionBytes);
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException ||
                exception is InvalidOperationException || exception is OverflowException ||
                exception is DecoderFallbackException || exception is InvalidCastException)
            {
                throw Invalid();
            }
        }

        private static byte[] EncodeSeason(Formula4Championship season)
        {
            using var payload = new MemoryStream();
            using (var writer = new BinaryWriter(payload, Utf8, true))
            {
                WriteText(writer, SeasonMagic);
                WriteText(writer, Formula4Data.DataVersion);
                WriteText(writer, Formula4SportRules.Version);
                writer.Write(season.Seed);
                WriteText(writer, season.TeamId);
                writer.Write(season.Rounds.Count);
                foreach (var round in season.Rounds)
                {
                    writer.Write(round.Number);
                    WriteStrategy(writer, round.Strategy);
                    WriteDecisions(writer, round.Decisions);
                    writer.Write(RaceFingerprint(round.Result).Length);
                    writer.Write(RaceFingerprint(round.Result));
                }
                writer.Write(season.ActiveRace != null);
                if (season.ActiveRace != null)
                {
                    var active = season.ActiveRace;
                    writer.Write(active.Round);
                    WriteStrategy(writer, active.Strategy);
                    writer.Write(active.State.CompletedSectors);
                    WriteDecisions(writer, active.Decisions);
                    writer.Write(active.RandomState);
                    WriteBytes(writer, RaceFingerprint(active.State));
                }
                else
                {
                    writer.Write(season.PendingStrategy != null);
                    if (season.PendingStrategy != null) WriteStrategy(writer, season.PendingStrategy);
                }
                WriteBytes(writer, SeasonFingerprint(season));
            }
            var bytes = payload.ToArray();
            return bytes.Concat(CheckpointFingerprint.Hash(bytes)).ToArray();
        }

        private static Formula4Championship DecodeSeason(byte[] bytes, CareerProgression progression)
        {
            if (bytes.Length < 64 || bytes.Length > ClientCheckpoint.MaxBytes) throw Invalid();
            var payload = bytes.Take(bytes.Length - 32).ToArray();
            if (!CheckpointFingerprint.Hash(payload).SequenceEqual(bytes.Skip(payload.Length))) throw Invalid();
            using var stream = new MemoryStream(payload, false);
            using var reader = new BinaryReader(stream, Utf8);
            if (ReadText(reader) != SeasonMagic ||
                ReadText(reader) != Formula4Data.DataVersion ||
                ReadText(reader) != Formula4SportRules.Version)
                throw Invalid();
            var seed = reader.ReadUInt32();
            var teamId = ReadText(reader);
            if (seed != progression.KartCareer.Seed || teamId != progression.Formula4TeamId)
                throw Invalid();
            var season = new Formula4Championship(seed, teamId,
                progression.KartCareer.Profile, progression.KartCareer.Skill);
            var roundCount = reader.ReadInt32();
            if (roundCount < 0 || roundCount > Formula4Data.Calendar.Count) throw Invalid();
            for (var index = 0; index < roundCount; index++)
            {
                var round = reader.ReadInt32();
                if (round != index + 1) throw Invalid();
                var strategy = ReadStrategy(reader);
                var decisions = ReadDecisions(reader, Formula4SportRules.TotalSectors);
                var expectedFingerprint = ReadBytes(reader, 32);
                season.RestoreCompletedRound(round, strategy, decisions);
                var restored = season.Rounds.Last();
                if (!RaceFingerprint(restored.Result).SequenceEqual(expectedFingerprint)) throw Invalid();
            }

            var active = ReadBoolean(reader);
            if (active)
            {
                var round = reader.ReadInt32();
                if (round != season.NextRound || season.Finished) throw Invalid();
                var strategy = ReadStrategy(reader);
                var completed = reader.ReadInt32();
                var decisions = ReadDecisions(reader, completed);
                var randomState = reader.ReadUInt32();
                var expectedFingerprint = ReadBytes(reader, 32);
                season.RestoreActiveRound(round, strategy, decisions, completed);
                if (season.ActiveRace == null ||
                    season.ActiveRace.RandomState != randomState ||
                    !RaceFingerprint(season.ActiveRace.State).SequenceEqual(expectedFingerprint))
                    throw Invalid();
            }
            else if (ReadBoolean(reader))
            {
                if (season.Finished || season.ActiveRace != null)
                    throw Invalid();
                season.PrepareRace(season.NextRound, ReadStrategy(reader));
            }
            var expectedSeasonFingerprint = ReadBytes(reader, 32);
            if (!SeasonFingerprint(season).SequenceEqual(expectedSeasonFingerprint) ||
                stream.Position != stream.Length)
                throw Invalid();
            return season;
        }

        private static void WriteStrategy(BinaryWriter writer, Formula4Strategy strategy)
        {
            writer.Write(strategy.MiddleStartSector);
            writer.Write(strategy.FinalStartSector);
            writer.Write((int)strategy.Beginning);
            writer.Write((int)strategy.Middle);
            writer.Write((int)strategy.Final);
        }

        private static Formula4Strategy ReadStrategy(BinaryReader reader)
        {
            var middle = reader.ReadInt32();
            var final = reader.ReadInt32();
            var beginningPace = (Pace)reader.ReadInt32();
            var middlePace = (Pace)reader.ReadInt32();
            var finalPace = (Pace)reader.ReadInt32();
            try { return new Formula4Strategy(beginningPace, middlePace, finalPace, middle, final); }
            catch (ArgumentException) { throw Invalid(); }
        }

        private static void WriteDecisions(BinaryWriter writer,
            System.Collections.Generic.IReadOnlyList<Formula4PaceDecision> decisions)
        {
            writer.Write(decisions.Count);
            foreach (var decision in decisions)
            {
                writer.Write(decision.Sector);
                WriteText(writer, decision.DriverId);
                writer.Write((int)decision.Pace);
            }
        }

        private static Formula4PaceDecision[] ReadDecisions(BinaryReader reader, int maxSector)
        {
            var count = reader.ReadInt32();
            if (count < 0 || count > maxSector) throw Invalid();
            var decisions = new Formula4PaceDecision[count];
            var previousSector = 0;
            for (var index = 0; index < count; index++)
            {
                var sector = reader.ReadInt32();
                var driverId = ReadText(reader);
                var pace = (Pace)reader.ReadInt32();
                if (sector <= previousSector || sector > maxSector ||
                    driverId != CareerProfile.PlayerId || !Enum.IsDefined(typeof(Pace), pace))
                    throw Invalid();
                decisions[index] = new Formula4PaceDecision(sector, driverId, pace);
                previousSector = sector;
            }
            return decisions;
        }

        private static byte[] RaceFingerprint(Formula4RaceSnapshot race) => Digest(writer =>
        {
            writer.Write(race.Seed);
            writer.Write(race.RandomVersion);
            writer.Write(race.RandomState);
            writer.Write(race.Round);
            writer.Write(race.CircuitId);
            writer.Write(race.CompletedSectors);
            foreach (var result in race.Classification)
            {
                writer.Write(result.DriverId);
                writer.Write(result.TeamId);
                writer.Write(result.Position);
                writer.Write(result.ElapsedMs);
                writer.Write(result.TireWear);
                writer.Write(result.MechanicalCondition);
                writer.Write(result.MechanicalIssues);
                writer.Write((int)result.Pace);
                writer.Write(result.Points);
            }
            writer.Write(race.Events.Count);
            foreach (var item in race.Events)
            {
                writer.Write(item.Sector);
                writer.Write(item.DriverId);
                writer.Write(item.Code);
                writer.Write(item.Explanation);
            }
        });

        private static byte[] SeasonFingerprint(Formula4Championship season) => Digest(writer =>
        {
            writer.Write(season.Seed);
            writer.Write(season.TeamId);
            writer.Write(season.NextRound);
            writer.Write(season.Finished);
            writer.Write(season.Rounds.Count);
            foreach (var round in season.Rounds)
                writer.Write(RaceFingerprint(round.Result));
            writer.Write(season.ActiveRace != null);
            if (season.ActiveRace != null)
                writer.Write(RaceFingerprint(season.ActiveRace.State));
            writer.Write(season.PendingStrategy != null);
            if (season.PendingStrategy != null) WriteStrategy(writer, season.PendingStrategy);
            foreach (var standing in season.DriverStandings.Concat(season.TeamStandings))
            {
                writer.Write(standing.Id);
                writer.Write(standing.Points);
                foreach (var finish in standing.Finishes) writer.Write(finish);
            }
        });

        private static byte[] Digest(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Utf8, true)) write(writer);
            return CheckpointFingerprint.Hash(stream.ToArray());
        }

        private static void WriteText(BinaryWriter writer, string value)
        {
            var bytes = Utf8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader)
        {
            var size = reader.ReadInt32();
            if (size < 1 || size > 512) throw Invalid();
            var bytes = reader.ReadBytes(size);
            if (bytes.Length != size) throw Invalid();
            return Utf8.GetString(bytes);
        }

        private static void WriteBytes(BinaryWriter writer, byte[] bytes)
        {
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static byte[] ReadBytes(BinaryReader reader, int maxSize)
        {
            var size = reader.ReadInt32();
            if (size < 1 || size > maxSize || size > reader.BaseStream.Length - reader.BaseStream.Position)
                throw Invalid();
            var bytes = reader.ReadBytes(size);
            if (bytes.Length != size) throw Invalid();
            return bytes;
        }

        private static bool ReadBoolean(BinaryReader reader)
        {
            var value = reader.ReadByte();
            if (value > 1) throw Invalid();
            return value == 1;
        }

        private static InvalidDataException Invalid() =>
            new InvalidDataException("Checkpoint F4 inválido, corrompido ou incompatível; o save de origem não foi alterado.");
    }
}
