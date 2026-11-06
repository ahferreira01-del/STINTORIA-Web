using System;
using System.IO;
using System.Linq;

namespace Stintoria.ClientBridge
{
    public interface IClientCheckpointStore
    {
        bool Exists(int slot);
        byte[] Read(int slot);
        void Write(int slot, byte[] checkpoint, byte[]? expectedPrevious);
    }

    /// <summary>Somente desktop. Sem fallback destrutivo se o filesystem não suportar replace/lock.</summary>
    public sealed class DesktopCheckpointStore : IClientCheckpointStore
    {
        private readonly string directory;
        public DesktopCheckpointStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Diretório obrigatório.");
            this.directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(this.directory);
        }
        private string PathFor(int slot)
        {
            if (slot < 1 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot), "Slot deve estar entre 1 e 3.");
            return Path.Combine(directory, "carreira-" + slot + ".stint");
        }
        public bool Exists(int slot) => File.Exists(PathFor(slot));
        public byte[] Read(int slot)
        {
            var path = PathFor(slot);
            using var held = Lock(path);
            return ReadChecked(path);
        }
        public void Write(int slot, byte[] checkpoint, byte[]? expectedPrevious)
        {
            ClientCheckpoint.Decode(checkpoint);
            var path = PathFor(slot);
            using var held = Lock(path);
            if (expectedPrevious == null && File.Exists(path))
                throw new IOException("Slot ocupado; nenhuma carreira foi sobrescrita.");
            if (expectedPrevious != null && (!File.Exists(path) || !ReadChecked(path).SequenceEqual(expectedPrevious)))
                throw new IOException("O arquivo mudou fora desta sessão. Recarregue antes de continuar.");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    stream.Write(checkpoint, 0, checkpoint.Length); stream.Flush(true);
                }
                if (expectedPrevious == null) File.Move(temporary, path);
                else File.Replace(temporary, path, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static FileStream Lock(string path) => new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        private static byte[] ReadChecked(string path)
        {
            if (new FileInfo(path).Length > ClientCheckpoint.MaxBytes) throw new InvalidDataException("Checkpoint acima do limite.");
            var bytes = File.ReadAllBytes(path); ClientCheckpoint.Decode(bytes); return bytes;
        }
    }
}
