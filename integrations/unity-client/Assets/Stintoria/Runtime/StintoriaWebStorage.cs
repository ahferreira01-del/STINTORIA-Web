using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Stintoria.ClientBridge;
using UnityEngine;

namespace Stintoria.UnityClient
{
    /// <summary>Interop somente WebGL. Nunca espera de forma bloqueante pela resposta JS.</summary>
    public sealed class StintoriaWebStorage : MonoBehaviour, IBrowserCheckpointTransport
    {
        private readonly Dictionary<int, TaskCompletionSource<string>> requests = new Dictionary<int, TaskCompletionSource<string>>();
        private int serial;
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void StintoriaStorage_Read(string receiver, int request, int slot);
        [DllImport("__Internal")] private static extern void StintoriaStorage_Write(string receiver, int request, int slot,
            int bankA, string record, string expectedA, string expectedB);
        [DllImport("__Internal")] private static extern void StintoriaStorage_Download(string receiver, int request, string checkpoint);
        [DllImport("__Internal")] private static extern void StintoriaStorage_Choose(string receiver, int request);
#endif
        private Task<string> Request(Action<int> invoke)
        {
            var id = checked(++serial); var pending = new TaskCompletionSource<string>();
            requests.Add(id, pending);
            try { invoke(id); }
            catch (Exception e) { requests.Remove(id); pending.TrySetException(e); }
            return pending.Task;
        }
        public async Task<BrowserSlotPair> ReadPairAsync(int slot)
        {
            var reply = await Request(id => {
#if UNITY_WEBGL && !UNITY_EDITOR
                StintoriaStorage_Read(gameObject.name, id, slot);
#else
                throw new BrowserStorageException("unavailable");
#endif
            });
            var parts = reply.Split(';');
            if (parts.Length != 2) throw new BrowserStorageException("unavailable");
            return BrowserSlotPair.FromEncoded(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1]));
        }
        public async Task WriteBankAsync(int slot, bool bankA, byte[] record, BrowserSlotPair expected)
        {
            await Request(id => {
#if UNITY_WEBGL && !UNITY_EDITOR
                StintoriaStorage_Write(gameObject.name, id, slot, bankA ? 1 : 0,
                    Convert.ToBase64String(record), expected.EncodedA, expected.EncodedB);
#else
                throw new BrowserStorageException("unavailable");
#endif
            });
        }
        public async Task DownloadAsync(byte[] checkpoint)
        {
            ClientCheckpoint.Decode(checkpoint);
            await Request(id => {
#if UNITY_WEBGL && !UNITY_EDITOR
                StintoriaStorage_Download(gameObject.name, id, Convert.ToBase64String(checkpoint));
#else
                throw new BrowserStorageException("unavailable");
#endif
            });
        }
        public async Task<byte[]> ChooseAsync()
        {
            var text = await Request(id => {
#if UNITY_WEBGL && !UNITY_EDITOR
                StintoriaStorage_Choose(gameObject.name, id);
#else
                throw new BrowserStorageException("unavailable");
#endif
            });
            var bytes = Convert.FromBase64String(text); ClientCheckpoint.Decode(bytes); return bytes;
        }
        // Método público usado por SendMessage. Respostas tardias/duplicadas são ignoradas.
        [UnityEngine.Scripting.Preserve]
        public void OnStorageReply(string reply)
        {
            var parts = reply.Split(new[] { '|' }, 3);
            if (parts.Length != 3 || !int.TryParse(parts[0], out var id) || !requests.TryGetValue(id, out var pending)) return;
            requests.Remove(id);
            if (parts[1] == "ok") pending.TrySetResult(parts[2]);
            else pending.TrySetException(new BrowserStorageException(parts[2]));
        }
        private void OnDestroy()
        {
            var outstanding = new List<TaskCompletionSource<string>>(requests.Values); requests.Clear();
            foreach (var pending in outstanding) pending.TrySetException(new BrowserStorageException("uncertain"));
        }
    }
}
