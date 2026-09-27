using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace PriceCheck.Collector.Services;

public sealed record JournalEntry(DateTimeOffset Time, string Level, string Profile, string Market,
    string Trader, string Message)
{
    public string LocalTime => Time.ToLocalTime().ToString("MM-dd HH:mm:ss");
    public string Display => $"{Time.ToLocalTime():HH:mm:ss} {Level} [{Profile}] {Message}";
}

// File IO never runs on the UI dispatcher. Persistent retention is independent
// of the bounded UI journal, and every completed write is flushed to disk.
public sealed class CollectorJournal
{
    private readonly Channel<object> _pending = Channel.CreateUnbounded<object>(new() { SingleReader = true });
    private readonly Task _writer;
    private readonly string _directory;
    public string? LastError { get; private set; }
    public CollectorJournal(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheckCollector", "logs");
        _writer = Task.Run(WriteLoopAsync);
    }
    public void Append(JournalEntry entry) => _pending.Writer.TryWrite(entry);
    public async Task FlushAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _pending.Writer.WriteAsync(completion);
        await completion.Task;
    }
    public static string Redact(string message)
    {
        message = Regex.Replace(message, @"(?i)(password|passwd|token|secret|authorization)(\s*[:=]\s*)([^\s,;]+)", "$1$2[REDACTED]");
        return Regex.Replace(message, @"(?i)(postgres(?:ql)?://[^:/\s]+:)[^@\s]+@", "$1[REDACTED]@");
    }
    private async Task WriteLoopAsync()
    {
        await foreach (var item in _pending.Reader.ReadAllAsync())
        {
            if (item is TaskCompletionSource completion) { completion.SetResult(); continue; }
            try
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, $"collector-{Environment.ProcessId}.log");
                if (File.Exists(path) && new FileInfo(path).Length >= 5 * 1024 * 1024)
                {
                    for (var index = 4; index >= 1; index--)
                    {
                        var from = index == 1 ? path : path + "." + (index - 1);
                        var to = path + "." + index;
                        if (File.Exists(from)) File.Move(from, to, true);
                    }
                }
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                    4096, FileOptions.WriteThrough);
                var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item) + "\n");
                stream.Write(bytes); stream.Flush(true);
                var obsolete = Directory.EnumerateFiles(_directory, "collector-*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc).Skip(10).ToArray();
                foreach (var old in obsolete)
                {
                    File.Delete(old);
                    for (var index = 1; index <= 4; index++) File.Delete(old + "." + index);
                }
                LastError = null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LastError = e.Message; }
        }
    }
}
