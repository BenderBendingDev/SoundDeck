using System.IO.Compression;
using System.Security.Cryptography;
using SoundDeck.Core;

namespace SoundDeck.Infrastructure;

public sealed class LibraryService : ILibraryService
{
    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aac"
        };

    public async Task<string> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("No se encontró el archivo seleccionado.", sourcePath);

        var extension = Path.GetExtension(sourcePath);
        if (!SupportedExtensions.Contains(extension))
            throw new NotSupportedException($"El formato {extension} no es compatible.");

        Directory.CreateDirectory(AppPaths.Library);
        var hash = await GetHashAsync(sourcePath, cancellationToken);
        var safeName = string.Concat(Path.GetFileNameWithoutExtension(sourcePath)
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var destination = Path.Combine(AppPaths.Library, $"{safeName}-{hash[..10]}{extension.ToLowerInvariant()}");

        if (!File.Exists(destination))
        {
            await using var source = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var target = File.Create(destination);
            await source.CopyToAsync(target, cancellationToken);
        }
        return destination;
    }

    public Task<string> CreateBackupAsync(string destinationZip, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationZip)!);
        if (File.Exists(destinationZip))
            File.Delete(destinationZip);

        using var archive = ZipFile.Open(destinationZip, ZipArchiveMode.Create);
        if (File.Exists(AppPaths.DatabasePath))
            archive.CreateEntryFromFile(AppPaths.DatabasePath, "sounddeck.db", CompressionLevel.Optimal);
        if (Directory.Exists(AppPaths.Library))
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.Library))
            {
                cancellationToken.ThrowIfCancellationRequested();
                archive.CreateEntryFromFile(file, $"Library/{Path.GetFileName(file)}", CompressionLevel.Optimal);
            }
        }
        return Task.FromResult(destinationZip);
    }

    public Task RestoreBackupAsync(string sourceZip, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceZip))
            throw new FileNotFoundException("No se encontró la copia de seguridad.", sourceZip);

        Directory.CreateDirectory(AppPaths.Root);
        using var archive = ZipFile.OpenRead(sourceZip);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(Path.Combine(AppPaths.Root, entry.FullName));
            if (!destination.StartsWith(Path.GetFullPath(AppPaths.Root), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La copia contiene una ruta no válida.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
        return Task.CompletedTask;
    }

    private static async Task<string> GetHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
