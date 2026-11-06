using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stintoria.Core;

namespace Stintoria.ClientBridge
{
    /// <summary>Mesmo controlador usado pela fonte Unity e pelos testes headless; ações transacionais.</summary>
    public sealed class KartClientController
    {
        private readonly IClientCheckpointStore store;
        private CareerBridge? client;
        private byte[]? saved;
        private ChampionshipHistory? history;
        private RaceReviewFrame? review;
        private bool pending;
        public bool IsSaving => pending;
        public int ActiveSlot { get; private set; }
        public KartClientController(IClientCheckpointStore store)
        { this.store = store ?? throw new ArgumentNullException(nameof(store)); }
        public bool HasCareer => client != null;
        public bool SlotExists(int slot) => store.Exists(slot);
        public ClientOverview InspectSlot(int slot) => CareerBridge.LoadCheckpoint(store.Read(slot)).ReadOverview();
        public IReadOnlyList<Driver> DriverRoster() => Current().ReadDrivers();
        public ClientOverview Overview() => Current().ReadOverview();
        public ClientRaceSession? ActiveRace() => Current().ReadActiveRace();
        public void StartRace(int expectedRound) => Change(copy => copy.StartRace(expectedRound));
        public void AdvanceRace(int expectedRound, int expectedSector, int count = 1, Pace? pace = null)
            => Change(copy => copy.AdvanceRace(expectedRound, expectedSector, count, pace));
        public ChampionshipHistory History() => history ??= Current().ReadHistory();
        public SegmentReport Report(int round, string driverId) => SegmentReportQuery.ForRound(History().GetRound(round), driverId);
        public RaceReviewFrame Review(int round, int sector)
        {
            // OnGUI pode consultar várias vezes por frame. Não reprocessar os eventos a cada repaint.
            if (review == null || review.Round != round || review.Sector != sector)
                review = RaceReviewQuery.ForRound(History().GetRound(round), sector);
            return review;
        }
        public void Create(int slot, uint seed, string name, string nationality, CareerOrigin origin, string teamId, bool confirmedVacancy)
        {
            EnsureIdle();
            if (!confirmedVacancy) throw new InvalidOperationException("Confirme explicitamente a vaga antes de criar a carreira.");
            var next = CareerBridge.Create(seed, name, nationality, origin, teamId);
            var bytes = next.ExportCheckpoint();
            store.Write(slot, bytes, null);
            client = next; saved = bytes; ActiveSlot = slot;
            Invalidate();
        }
        public void Load(int slot)
        {
            EnsureIdle();
            var bytes = store.Read(slot); var next = CareerBridge.LoadCheckpoint(bytes);
            client = next; saved = (byte[])bytes.Clone(); ActiveSlot = slot;
            Invalidate();
        }
        public HistoryRound Preview(Training training, RaceStrategy plan, bool tactical)
        {
            var copy = CareerBridge.LoadCheckpoint(Current().ExportCheckpoint());
            return copy.PreparePlan(copy.ReadOverview().NextRound, training, plan, tactical);
        }
        public void Prepare(Training training, RaceStrategy plan, bool tactical)
            => Change(copy => copy.PreparePlan(copy.ReadOverview().NextRound, training, plan, tactical));
        public void Run() => Change(copy => copy.Run(copy.ReadOverview().NextRound));
        public void Save() => Change(copy => { });
        public byte[] ExportCheckpoint() => Current().ExportCheckpoint();
        public Task StartRaceAsync(int expectedRound) => ChangeAsync(copy => copy.StartRace(expectedRound));
        public Task AdvanceRaceAsync(int expectedRound, int expectedSector, int count = 1, Pace? pace = null)
            => ChangeAsync(copy => copy.AdvanceRace(expectedRound, expectedSector, count, pace));
        public Task PrepareAsync(Training training, RaceStrategy plan, bool tactical)
            => ChangeAsync(copy => copy.PreparePlan(copy.ReadOverview().NextRound, training, plan, tactical));
        public Task RunAsync() => ChangeAsync(copy => copy.Run(copy.ReadOverview().NextRound));
        public Task SaveAsync() => ChangeAsync(copy => { });
        public async Task CreateAsync(int slot, uint seed, string name, string nationality, CareerOrigin origin, string teamId, bool confirmedVacancy)
        {
            EnsureIdle();
            if (!confirmedVacancy) throw new InvalidOperationException("Confirme explicitamente a vaga antes de criar a carreira.");
            pending = true;
            try {
                var next = CareerBridge.Create(seed, name, nationality, origin, teamId);
                var bytes = next.ExportCheckpoint(); await PersistAsync(slot, bytes, null);
                client = next; saved = bytes; ActiveSlot = slot; Invalidate();
            }
            finally { pending = false; }
        }
        public async Task LoadAsync(int slot)
        {
            EnsureIdle(); pending = true;
            try {
                if (store is IAsyncClientCheckpointStore asyncStore) await asyncStore.RefreshAsync();
                var bytes = store.Read(slot); var next = CareerBridge.LoadCheckpoint(bytes);
                client = next; saved = (byte[])bytes.Clone(); ActiveSlot = slot; Invalidate();
            }
            finally { pending = false; }
        }
        public async Task ImportAsync(int slot, byte[] checkpoint, bool confirmed)
        {
            EnsureIdle();
            if (!confirmed) throw new InvalidOperationException("Confirme piloto/progresso e destino antes de importar.");
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            pending = true;
            try {
                var bytes = (byte[])checkpoint.Clone(); var next = CareerBridge.LoadCheckpoint(bytes);
                await PersistAsync(slot, bytes, null); // somente vazio, sem substituição
                client = next; saved = bytes; ActiveSlot = slot; Invalidate();
            }
            finally { pending = false; }
        }
        private async Task ChangeAsync(Action<CareerBridge> action)
        {
            EnsureIdle(); pending = true;
            try {
                var next = CareerBridge.LoadCheckpoint(Current().ExportCheckpoint()); action(next);
                var bytes = next.ExportCheckpoint(); await PersistAsync(ActiveSlot, bytes, saved);
                client = next; saved = bytes; Invalidate();
            }
            finally { pending = false; }
        }
        private Task PersistAsync(int slot, byte[] bytes, byte[]? previous)
        {
            if (store is IAsyncClientCheckpointStore asyncStore) return asyncStore.WriteAsync(slot, bytes, previous);
            store.Write(slot, bytes, previous); return Task.CompletedTask;
        }
        private void EnsureIdle()
        { if (pending) throw new InvalidOperationException("Aguarde confirmação da operação de armazenamento."); }
        private void Change(Action<CareerBridge> action)
        {
            EnsureIdle();
            var next = CareerBridge.LoadCheckpoint(Current().ExportCheckpoint());
            action(next);
            var bytes = next.ExportCheckpoint();
            store.Write(ActiveSlot, bytes, saved);
            client = next; saved = bytes;
            Invalidate();
        }
        private void Invalidate() { history = null; review = null; }
        private CareerBridge Current() => client ?? throw new InvalidOperationException("Crie ou selecione um piloto primeiro.");
    }
}
