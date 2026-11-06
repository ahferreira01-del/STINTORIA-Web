using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Stintoria.ClientBridge
{
    /// <summary>Cache confirmado + pares A/B IndexedDB. Nunca bloqueia esperando JavaScript.</summary>
    public sealed class BrowserCheckpointStore : IAsyncClientCheckpointStore
    {
        private readonly IBrowserCheckpointTransport transport;
        private BrowserSlotPair[] pairs = { new BrowserSlotPair(null, null), new BrowserSlotPair(null, null), new BrowserSlotPair(null, null) };
        private bool ready, busy;
        private byte[]? recovery;
        public BrowserSaveState State { get; private set; }
        public string Message { get; private set; } = "Aguardando leitura do navegador.";
        public byte[]? RecoveryCheckpoint => recovery == null ? null : (byte[])recovery.Clone();
        public byte[]? ExportRawBank(int slot, bool bankA) => bankA ? pairs[Index(slot)].A : pairs[Index(slot)].B;
        public string ExportRawBankEncoded(int slot, bool bankA) => bankA ? pairs[Index(slot)].EncodedA : pairs[Index(slot)].EncodedB;
        public BrowserCheckpointStore(IBrowserCheckpointTransport transport)
        { this.transport = transport ?? throw new ArgumentNullException(nameof(transport)); }
        private static int Index(int slot) => slot >= 1 && slot <= 3 ? slot - 1 :
            throw new ArgumentOutOfRangeException(nameof(slot), "Slot deve estar entre 1 e 3.");
        private void Ready()
        { if (!ready) throw new InvalidOperationException("Atualize os slots do navegador antes de continuar."); }
        public bool Exists(int slot)
        {
            var i = Index(slot); Ready();
            return pairs[i].A != null || pairs[i].B != null;
        }
        public byte[] Read(int slot)
        {
            var i = Index(slot); Ready();
            var found = Select(pairs[i]);
            if (found.Record == null) throw new InvalidDataException("Slot vazio ou sem cópia válida. Dados originais preservados.");
            if (found.Recovered) {
                State = BrowserSaveState.Recovered;
                Message = "Cópia A/B inválida: carregada a cópia válida disponível. Progresso mais recente pode estar ausente; exporte e confira a etapa/setor.";
            }
            return (byte[])found.Record.Checkpoint.Clone();
        }
        public void Write(int slot, byte[] checkpoint, byte[]? expectedPrevious) =>
            throw new InvalidOperationException("Persistência Web exige confirmação assíncrona. Use a operação Async.");
        public async Task RefreshAsync()
        {
            Enter(); State = BrowserSaveState.Reading; Message = "Lendo slots do navegador...";
            ready = false;
            try {
                var next = new BrowserSlotPair[3];
                for (var i = 0; i < 3; i++) next[i] = await transport.ReadPairAsync(i + 1);
                pairs = next; ready = true; State = BrowserSaveState.Idle;
                Message = "Slots lidos. Gravações anteriores não foram reescritas.";
            }
            catch (Exception e) { State = BrowserSaveState.Failed; Message = e.Message; throw; }
            finally { busy = false; }
        }
        public async Task WriteAsync(int slot, byte[] checkpoint, byte[]? expectedPrevious)
        {
            var i = Index(slot); Ready();
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            var proposed = (byte[])checkpoint.Clone(); ClientCheckpoint.Decode(proposed);
            var found = Select(pairs[i]);
            if (expectedPrevious == null ? Exists(slot) :
                found.Record == null || !found.Record.Checkpoint.SequenceEqual(expectedPrevious))
                throw new BrowserStorageException("conflict");
            Enter(); recovery = proposed; State = BrowserSaveState.Writing;
            Message = "Gravando... aguarde a confirmação. Não feche a aba.";
            var expected = pairs[i];
            try {
                var generation = checked((found.Record?.Generation ?? 0) + 1);
                var record = BrowserCheckpointRecord.Encode(generation, proposed);
                var targetA = !found.BankA;
                await transport.WriteBankAsync(slot, targetA, record, expected);
                // A confirmação vem da transação, não de uma cópia apenas em memória.
                pairs[i] = targetA ? new BrowserSlotPair(record, expected.B, null, expected.EncodedB) :
                    new BrowserSlotPair(expected.A, record, expected.EncodedA, null);
                State = BrowserSaveState.Confirmed; recovery = null;
                Message = "Gravação confirmada pelo armazenamento do navegador. Exporte uma cópia externa; o navegador pode apagar seus dados.";
            }
            catch (Exception e) {
                ready = false; // obriga reler após falha, conflito ou confirmação perdida
                State = e is BrowserStorageException b && b.Code == "uncertain" ? BrowserSaveState.Uncertain : BrowserSaveState.Failed;
                Message = e.Message + " A proposta não foi adotada pela sessão. Uma cópia para exportação permanece em memória.";
                throw;
            }
            finally { busy = false; }
        }
        private void Enter()
        { if (busy) throw new InvalidOperationException("Há uma operação de armazenamento pendente."); busy = true; }
        private static (BrowserCheckpointRecord? Record, bool BankA, bool Recovered) Select(BrowserSlotPair pair)
        {
            var a = BrowserCheckpointRecord.TryRead(pair.A); var b = BrowserCheckpointRecord.TryRead(pair.B);
            if (a != null && b != null && a.Generation == b.Generation && !a.Checkpoint.SequenceEqual(b.Checkpoint))
                throw new InvalidDataException("Cópias A/B ambíguas. Nenhuma foi escolhida ou apagada; preserve e recupere uma exportação.");
            if (a != null && (b == null || a.Generation >= b.Generation))
                return (a, true, pair.B != null && b == null);
            return (b, false, pair.A != null && a == null);
        }
    }
}
