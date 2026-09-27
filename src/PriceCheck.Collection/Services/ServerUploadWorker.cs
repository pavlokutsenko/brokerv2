using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PriceCheck.Windows.Storage;

namespace PriceCheck.Collector.Services;

public static class ServerUploadWorker
{
    private const int ParallelPriceUploads = 8;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        using var mutex = new Mutex(false, MutexName(directory));
        try
        {
            if (!mutex.WaitOne(0)) return;
        }
        catch (AbandonedMutexException) { }

        try
        {
            RecoverInterruptedFiles(directory);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var idleSince = DateTimeOffset.UtcNow;
            while (!cancellationToken.IsCancellationRequested)
            {
                var claimed = ClaimReady(directory, ParallelPriceUploads);
                if (claimed.Count == 0)
                {
                    if (DateTimeOffset.UtcNow - idleSince >= TimeSpan.FromMinutes(2)) return;
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                idleSince = DateTimeOffset.UtcNow;
                try
                {
                    await Task.WhenAll(claimed.Select(path => DeliverAsync(http, path, cancellationToken)))
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    RecoverInterruptedFiles(directory);
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
    }

    private static IReadOnlyList<string> ClaimReady(string directory, int maximumPriceUploads)
    {
        var claimed = new List<string>(maximumPriceUploads);
        var ingestionClaimed = false;
        var priceClaims = 0;
        foreach (var ready in Directory.EnumerateFiles(directory, "*.ready").OrderBy(path => path, StringComparer.Ordinal))
        {
            ServerUploadEnvelope? envelope = null;
            try
            {
                envelope = JsonSerializer.Deserialize<ServerUploadEnvelope>(File.ReadAllText(ready), Json);
                if (envelope is not null && envelope.NextAttemptAtUtc > DateTimeOffset.UtcNow) continue;
            }
            catch (Exception exception) when (exception is JsonException or IOException) { }

            var isIngestion = envelope?.Kind is "radar" or "broker";
            if (isIngestion && ingestionClaimed) continue;
            if (!isIngestion && priceClaims >= maximumPriceUploads) continue;

            var sending = Path.ChangeExtension(ready, $"sending.{Environment.ProcessId}");
            try
            {
                File.Move(ready, sending);
                claimed.Add(sending);
                if (isIngestion) ingestionClaimed = true;
                else priceClaims++;
                if (ingestionClaimed && priceClaims >= maximumPriceUploads) break;
            }
            catch (IOException) { }
        }
        return claimed;
    }

    private static async Task DeliverAsync(HttpClient http, string path, CancellationToken cancellationToken)
    {
        ServerUploadEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ServerUploadEnvelope>(await File.ReadAllTextAsync(path, cancellationToken), Json)
                ?? throw new InvalidDataException("Upload envelope is empty.");
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            MoveTo(path, "rejected", exception.Message);
            return;
        }

        if (envelope.NextAttemptAtUtc > DateTimeOffset.UtcNow)
        {
            Release(path, envelope);
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            using var content = new StringContent(envelope.Body.GetRawText(), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(envelope.Url, content, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                File.Delete(path);
                return;
            }

            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (IsPermanent(response.StatusCode))
            {
                MoveTo(path, "rejected", $"HTTP {(int)response.StatusCode}: {detail}");
                return;
            }
            Retry(path, envelope, $"HTTP {(int)response.StatusCode}: {detail}");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            Retry(path, envelope, exception.Message);
        }
    }

    private static bool IsPermanent(HttpStatusCode status) =>
        status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity;

    private static void Retry(string path, ServerUploadEnvelope envelope, string error)
    {
        envelope.Attempts++;
        envelope.LastError = Trim(error);
        var seconds = Math.Min(30, Math.Pow(2, Math.Min(envelope.Attempts, 5)));
        envelope.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(seconds);
        Release(path, envelope);
    }

    private static void Release(string path, ServerUploadEnvelope envelope)
    {
        var ready = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path))}.ready");
        // Keep the claimed payload until the retry copy is durable. If both
        // survive a crash, their stable batch/snapshot ID makes replay harmless.
        DurableJsonFile.Write(ready, envelope, Json, keepBackup: false);
        File.Delete(path);
    }

    private static void MoveTo(string path, string folderName, string error)
    {
        var folder = Path.Combine(Path.GetDirectoryName(path)!, folderName);
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, $"{Path.GetFileName(path)}.json");
        File.Move(path, destination, true);
        File.WriteAllText($"{destination}.error.txt", Trim(error));
    }

    private static void RecoverInterruptedFiles(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*.sending.*"))
        {
            var fileName = Path.GetFileName(path);
            var marker = fileName.IndexOf(".sending.", StringComparison.Ordinal);
            if (marker < 0) continue;
            var ready = Path.Combine(directory, $"{fileName[..marker]}.ready");
            try { File.Move(path, ready); } catch (IOException) { }
        }
        foreach (var path in Directory.EnumerateFiles(directory, "*.ready.tmp"))
        {
            var ready = path[..^4];
            if (File.Exists(ready)) continue;
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var saved = JsonSerializer.Deserialize<ServerUploadEnvelope>(stream, Json);
                    if (saved is null || saved.Id == Guid.Empty || saved.Body.ValueKind != JsonValueKind.Object ||
                        !Uri.TryCreate(saved.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
                        throw new InvalidDataException("Interrupted upload has no valid payload.");
                }
                File.Move(path, ready);
            }
            catch (Exception e) when (e is JsonException or InvalidDataException) { MoveTo(path,"rejected",e.Message); }
            catch (IOException) { } // A producer may still own its temporary file.
        }
    }

    private static string MutexName(string directory)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToUpperInvariant())));
        return $"Local\\PriceCheckUpload-{hash[..24]}";
    }

    private static string Trim(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= 2000 ? normalized : normalized[..2000];
    }
}
