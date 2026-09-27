using System.Text.Json;

namespace PriceCheck.Windows.Storage;

// Each caller owns serialization of writes for its path. Never publish a JSON
// rename before its content has been flushed through the Windows file cache.
public static class DurableJsonFile
{
    public static void Write<T>(string path, T value, JsonSerializerOptions? options = null, bool keepBackup = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                   64 * 1024, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, value, options);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, keepBackup ? path + ".bak" : null);
        else File.Move(temporary, path);
    }

    public static T? ReadRecoverable<T>(string path, JsonSerializerOptions? options = null, Func<T, bool>? validate = null) where T : class
    {
        var damaged = false;
        foreach (var candidate in new[] { path, path + ".bak", path + ".tmp" })
        {
            if (!File.Exists(candidate)) continue;
            T value;
            try
            {
                using var stream = File.OpenRead(candidate);
                value = JsonSerializer.Deserialize<T>(stream, options)
                    ?? throw new InvalidDataException("JSON value is empty.");
                if (validate is not null && !validate(value)) throw new InvalidDataException("JSON configuration is not valid.");
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or ArgumentException)
            {
                File.Move(candidate, candidate + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
                damaged = true;
                continue;
            }
            if (candidate != path) Write(path, value, options);
            return value;
        }
        // A second launch must not interpret the quarantined originals as a
        // first launch and silently replace the user's settings with defaults.
        var archived = Directory.Exists(Path.GetDirectoryName(path)) &&
            Directory.EnumerateFiles(Path.GetDirectoryName(path)!,Path.GetFileName(path)+"*.corrupt-*").Any();
        if (damaged || archived) throw new InvalidDataException($"{Path.GetFileName(path)} and its recovery copies are damaged. " +
            "The original files were preserved with .corrupt names. Settings were not reset.");
        return null;
    }
}
