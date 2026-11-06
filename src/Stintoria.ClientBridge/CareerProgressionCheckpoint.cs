using System;
using System.IO;
using System.Linq;
using System.Text;
using Stintoria.Core;

namespace Stintoria.ClientBridge
{
    /// <summary>
    /// Opt-in envelope for a career that made a promotion decision. Unpromoted Kart
    /// careers continue to use their original v1/v2 bytes unchanged.
    /// </summary>
    public static class CareerProgressionCheckpoint
    {
        private const string Magic = "STINTORIA-CARREIRA-3";
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(CareerProgression progression)
            => EncodeCore(progression, null);

        internal static byte[] Encode(CareerProgression progression, byte[] originalKartCheckpoint)
            => EncodeCore(progression, originalKartCheckpoint);

        private static byte[] EncodeCore(CareerProgression progression, byte[]? originalKartCheckpoint)
        {
            if (progression == null) throw new ArgumentNullException(nameof(progression));
            if (progression.Decision == PromotionDecision.None)
                throw new InvalidOperationException("Sem decisão de promoção, preserve o checkpoint Kart legado.");

            var inner = originalKartCheckpoint == null
                ? ClientCheckpoint.Encode(progression.KartCareer)
                : (byte[])originalKartCheckpoint.Clone();
            using var payload = new MemoryStream();
            using (var writer = new BinaryWriter(payload, Utf8, true))
            {
                WriteText(writer, Magic);
                WriteText(writer, CareerProgression.Version);
                WriteText(writer, Formula4Data.DataVersion);
                writer.Write(inner.Length);
                writer.Write(inner);
                writer.Write((int)progression.Decision);
                WriteText(writer, progression.Formula4TeamId ?? "");
            }
            var data = payload.ToArray();
            if (data.Length + 32 > ClientCheckpoint.MaxBytes)
                throw new InvalidDataException("Checkpoint multicategoria excede o limite permitido.");
            return data.Concat(CheckpointFingerprint.Hash(data)).ToArray();
        }

        public static int GetFormatVersion(byte[] checkpoint)
        {
            if (IsEnvelope(checkpoint))
            {
                Decode(checkpoint);
                return 3;
            }
            return ClientCheckpoint.GetFormatVersion(checkpoint);
        }

        public static bool IsEnvelope(byte[]? checkpoint)
        {
            if (checkpoint == null || checkpoint.Length < 32 + 4 + Magic.Length) return false;
            try
            {
                using var stream = new MemoryStream(checkpoint, 0, checkpoint.Length - 32, false);
                using var reader = new BinaryReader(stream, Utf8);
                return ReadText(reader) == Magic;
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException ||
                exception is DecoderFallbackException)
            {
                return false;
            }
        }

        public static CareerProgression Decode(byte[] checkpoint)
        {
            if (checkpoint == null || checkpoint.Length < 64 || checkpoint.Length > ClientCheckpoint.MaxBytes)
                throw Invalid();
            try
            {
                var data = checkpoint.Take(checkpoint.Length - 32).ToArray();
                if (!CheckpointFingerprint.Hash(data).SequenceEqual(checkpoint.Skip(data.Length)))
                    throw Invalid();
                using var stream = new MemoryStream(data, false);
                using var reader = new BinaryReader(stream, Utf8);
                if (ReadText(reader) != Magic ||
                    ReadText(reader) != CareerProgression.Version ||
                    ReadText(reader) != Formula4Data.DataVersion)
                    throw Invalid();
                var size = reader.ReadInt32();
                if (size < 1 || size > ClientCheckpoint.MaxBytes || size > stream.Length - stream.Position - 5)
                    throw Invalid();
                var kart = ClientCheckpoint.Decode(ReadBytes(reader, size));
                var decision = (PromotionDecision)reader.ReadInt32();
                var teamId = ReadText(reader);
                if (stream.Position != stream.Length) throw Invalid();
                var progression = new CareerProgression(
                    kart, decision, teamId.Length == 0 ? null : teamId);
                if (!kart.Finished || decision == PromotionDecision.None)
                    throw Invalid();
                return progression;
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException ||
                exception is InvalidOperationException || exception is OverflowException ||
                exception is DecoderFallbackException)
            {
                throw Invalid();
            }
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
            if (size < 0 || size > 512) throw Invalid();
            return Utf8.GetString(ReadBytes(reader, size));
        }

        private static byte[] ReadBytes(BinaryReader reader, int size)
        {
            var bytes = reader.ReadBytes(size);
            if (bytes.Length != size) throw Invalid();
            return bytes;
        }

        private static InvalidDataException Invalid() =>
            new InvalidDataException("Checkpoint multicategoria inválido, corrompido ou incompatível. O save original foi preservado.");
    }
}
