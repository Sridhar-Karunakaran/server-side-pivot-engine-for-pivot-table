using System.Text.Json;

namespace PivotController.Core.Persistence;

/// <summary>
/// Atomic JSON file persistence: serialize → temp file in the same directory → rename.
/// Corrupt files are quarantined as *.corrupt.{timestamp} and defaults are returned.
/// </summary>
public static class AtomicJsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static T Load<T>(string path) where T : class, new()
    {
        if (!File.Exists(path))
        {
            return new T();
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            T? value = JsonSerializer.Deserialize<T>(stream, Options);
            return value ?? new T();
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Quarantine(path);
            return new T();
        }
    }

    public static void Save<T>(string path, T value)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
        }

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static void Quarantine(string path)
    {
        try
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            string quarantined = Path.Combine(directory, $"{Path.GetFileName(path)}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}");
            File.Move(path, quarantined, overwrite: true);
        }
        catch (IOException)
        {
            // Quarantine is best-effort; the corrupt file is replaced on next save.
        }
    }
}
