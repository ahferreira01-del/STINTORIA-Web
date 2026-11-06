using System;
using System.IO;
using System.Linq;

namespace Stintoria.ClientBridge
{
    /// <summary>Envelope A/B separado: bytes portáteis v1/v2 permanecem intactos.</summary>
    internal sealed class BrowserCheckpointRecord
    {
        internal long Generation { get; }
        internal byte[] Checkpoint { get; }
        private BrowserCheckpointRecord(long generation, byte[] checkpoint)
        { Generation = generation; Checkpoint = checkpoint; }
        internal static byte[] Encode(long generation, byte[] checkpoint)
        {
            if (generation <= 0) throw new InvalidDataException("Geração inválida.");
            ClientCheckpoint.Decode(checkpoint);
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) {
                w.Write("STINTORIA-NAVEGADOR-1"); w.Write(generation); w.Write(checkpoint.Length); w.Write(checkpoint);
            }
            var payload = stream.ToArray();
            return payload.Concat(CheckpointFingerprint.Hash(payload)).ToArray();
        }
        internal static BrowserCheckpointRecord? TryRead(byte[]? bytes)
        {
            if (bytes == null || bytes.Length < 64 || bytes.Length > ClientCheckpoint.MaxBytes + 128) return null;
            try {
                var payload = bytes.Take(bytes.Length - 32).ToArray();
                if (!CheckpointFingerprint.Hash(payload).SequenceEqual(bytes.Skip(payload.Length))) return null;
                using var stream = new MemoryStream(payload);
                using var r = new BinaryReader(stream);
                if (r.ReadString() != "STINTORIA-NAVEGADOR-1") return null;
                var generation = r.ReadInt64(); var size = r.ReadInt32();
                if (generation <= 0 || size < 1 || size > ClientCheckpoint.MaxBytes) return null;
                var checkpoint = r.ReadBytes(size);
                if (checkpoint.Length != size || stream.Position != stream.Length) return null;
                ClientCheckpoint.Decode(checkpoint);
                return new BrowserCheckpointRecord(generation, checkpoint);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException ||
                e is ArgumentException || e is InvalidOperationException || e is FormatException ||
                e is OverflowException) { return null; }
        }
    }
}
