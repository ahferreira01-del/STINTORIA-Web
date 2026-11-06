using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stintoria.ClientBridge;

namespace Stintoria.Local;

/// <summary>Conversão explícita .NET 8. Nunca abre o arquivo de origem para escrita.</summary>
public sealed class TerminalCareerMigration
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private readonly Func<string, IClientCheckpointStore> storeFactory;
    public TerminalCareerMigration() : this(directory => new DesktopCheckpointStore(directory)) { }
    // Permite exercitar falhas de armazenamento sem encher o disco/quota.
    public TerminalCareerMigration(Func<string, IClientCheckpointStore> storeFactory)
        => this.storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));

    public CareerMigrationPreview Preview(string sourcePath, string slotsDirectory, int slot)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(slotsDirectory))
            throw new ArgumentException("Informe arquivo JSON e pasta de slots explicitamente.");
        if (slot < 1 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot), "Slot deve estar entre 1 e 3.");
        var source = Path.GetFullPath(sourcePath); var directory = Path.GetFullPath(slotsDirectory);
        var destination = Path.Combine(directory, $"carreira-{slot}.stint");
        if (string.Equals(source, destination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Origem e destino não podem ser o mesmo arquivo.");
        var bytes = ReadSource(source);
        // BOM é aceito como no leitor de texto do terminal; o hash abrange os bytes originais, incluindo BOM.
        var start = bytes.AsSpan().StartsWith(new byte[] {0xef, 0xbb, 0xbf}) ? 3 : 0;
        var career = CareerArchive.Decode(Utf8.GetString(bytes, start, bytes.Length - start));
        var canonical = CareerArchive.Encode(career);
        var checkpoint = ClientCheckpoint.Encode(career);
        if (CareerArchive.Encode(ClientCheckpoint.Decode(checkpoint)) != canonical)
            throw new InvalidDataException("Conversão não equivalente. Nenhum checkpoint foi importado.");
        using var document = JsonDocument.Parse(canonical);
        return new CareerMigrationPreview(source, Hash(bytes), document.RootElement.GetProperty("Format").GetString()!,
            directory, slot, destination, Occupied(destination), career, checkpoint);
    }

    public void Confirm(CareerMigrationPreview preview, bool confirmed)
    {
        if (preview == null) throw new ArgumentNullException(nameof(preview));
        if (!confirmed) throw new OperationCanceledException("Importação cancelada; nenhum slot foi gravado.");
        if (Hash(ReadSource(preview.SourcePath)) != preview.SourceSha256)
            throw new InvalidOperationException("O JSON mudou desde a prévia. Gere nova prévia antes de confirmar.");
        if (Occupied(preview.DestinationPath))
            throw new InvalidOperationException("Slot ocupado. Escolha outro slot vazio; não há substituição nesta operação.");
        // Write(null) exige criação: lock, flush e publicação atômica sem substituir destino concorrente.
        storeFactory(preview.SlotsDirectory).Write(preview.Slot, preview.CopyCheckpoint(), null);
    }

    private static bool Occupied(string path) => File.Exists(path) || Directory.Exists(path);
    private static byte[] ReadSource(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > ChampionshipArchive.MaxBytes) throw TooLarge();
        using var data = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while ((read = file.Read(buffer, 0, (int)Math.Min(buffer.Length, ChampionshipArchive.MaxBytes + 1L - data.Length))) != 0) {
            data.Write(buffer, 0, read);
            if (data.Length > ChampionshipArchive.MaxBytes) throw TooLarge();
        }
        return data.ToArray();
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static InvalidDataException TooLarge() => new("JSON acima do limite de 8 MiB; origem preservada.");
}
