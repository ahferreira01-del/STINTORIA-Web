using System;
using System.Threading.Tasks;

namespace Stintoria.ClientBridge
{
    public enum BrowserSaveState { Idle, Reading, Writing, Confirmed, Recovered, Failed, Uncertain }

    public sealed class BrowserStorageException : Exception
    {
        public string Code { get; }
        public BrowserStorageException(string code) : base(MessageFor(code)) { Code = code; }
        private static string MessageFor(string code) => code == "quota"
            ? "Espaço do navegador esgotado. Exporte uma cópia e libere espaço antes de tentar novamente."
            : code == "conflict" ? "Outro contexto alterou o slot. Atualize e carregue antes de continuar."
            : code == "uncertain" ? "Confirmação não recebida. A gravação pode ter ocorrido; atualize e carregue antes de continuar."
            : code == "cancelled" ? "Seleção de arquivo cancelada."
            : "Armazenamento do navegador indisponível ou sem permissão. Preserve uma exportação e tente novamente.";
    }

    /// <summary>Valores opacos lidos juntos; o transporte deve comparar o par numa transação.</summary>
    public sealed class BrowserSlotPair
    {
        private readonly byte[]? a, b;
        private readonly string? encodedA, encodedB;
        public byte[]? A => a == null ? null : (byte[])a.Clone();
        public byte[]? B => b == null ? null : (byte[])b.Clone();
        public string EncodedA => encodedA ?? (a == null ? "" : Convert.ToBase64String(a));
        public string EncodedB => encodedB ?? (b == null ? "" : Convert.ToBase64String(b));
        public BrowserSlotPair(byte[]? a, byte[]? b, string? encodedA = null, string? encodedB = null)
        {
            this.a = a == null ? null : (byte[])a.Clone(); this.b = b == null ? null : (byte[])b.Clone();
            this.encodedA = encodedA; this.encodedB = encodedB;
        }
        public static BrowserSlotPair FromEncoded(string a, string b) =>
            new BrowserSlotPair(Decode(a), Decode(b), a, b);
        private static byte[]? Decode(string text)
        {
            if (text.Length == 0) return null;
            if (text.Length > 88000) return new byte[] { 0 };
            try { return Convert.FromBase64String(text); }
            catch (FormatException) { return new byte[] { 0 }; } // inválido/ocupado, não vazio
        }
    }

    public interface IBrowserCheckpointTransport
    {
        Task<BrowserSlotPair> ReadPairAsync(int slot);
        // Só completar com sucesso APÓS transaction.oncomplete, nunca em put.onsuccess.
        Task WriteBankAsync(int slot, bool bankA, byte[] record, BrowserSlotPair expected);
    }

    public interface IAsyncClientCheckpointStore : IClientCheckpointStore
    {
        Task RefreshAsync();
        Task WriteAsync(int slot, byte[] checkpoint, byte[]? expectedPrevious);
    }
}
