using System.Text;
using System.Text.Json;
using Woodword.Models;

namespace Woodword.Services;

public sealed class TranslationHistoryService : IDisposable
{
    private const int ReadBlockSize = 8192;
    private readonly string historyPath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private bool disposed;

    public TranslationHistoryService(string historyPath) => this.historyPath = historyPath;

    public async Task AppendAsync(
        TranslationDirection direction,
        string input,
        string output,
        int maximumMegabytes,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var entry = new HistoryEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Direction = direction,
            Input = input,
            Output = output,
        };

        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
            await using (var stream = new FileStream(
                             historyPath, FileMode.Append, FileAccess.Write, FileShare.Read,
                             8192, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, entry, jsonOptions, cancellationToken);
                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
            }

            await PruneIfNeededAsync(Math.Clamp(maximumMegabytes, 1, 1024), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task EnforceLimitAsync(int maximumMegabytes, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await PruneIfNeededAsync(Math.Clamp(maximumMegabytes, 1, 1024), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<HistoryPage> ReadPageAsync(
        long? beforeOffset,
        int maximumEntries,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(historyPath)) return new HistoryPage([], null);

            await using var stream = new FileStream(
                historyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                ReadBlockSize, FileOptions.Asynchronous | FileOptions.RandomAccess);
            var cursor = Math.Clamp(beforeOffset ?? stream.Length, 0, stream.Length);
            var entries = new List<HistoryEntry>(maximumEntries);
            var reversedLine = new List<byte>(4096);
            var buffer = new byte[ReadBlockSize];
            long? olderOffset = null;
            var pageComplete = false;

            while (cursor > 0 && !pageComplete)
            {
                var readStart = Math.Max(0, cursor - buffer.Length);
                var readLength = (int)(cursor - readStart);
                stream.Position = readStart;
                await stream.ReadExactlyAsync(buffer.AsMemory(0, readLength), cancellationToken);

                for (var index = readLength - 1; index >= 0; index--)
                {
                    if (buffer[index] != (byte)'\n')
                    {
                        reversedLine.Add(buffer[index]);
                        continue;
                    }

                    if (reversedLine.Count == 0) continue;
                    TryAddReversedLine(reversedLine, entries);
                    reversedLine.Clear();
                    if (entries.Count < maximumEntries) continue;

                    olderOffset = readStart + index;
                    pageComplete = true;
                    break;
                }

                cursor = readStart;
            }

            if (!pageComplete && reversedLine.Count > 0)
                TryAddReversedLine(reversedLine, entries);

            return new HistoryPage(entries, pageComplete && olderOffset > 0 ? olderOffset : null);
        }
        finally
        {
            gate.Release();
        }
    }

    private void TryAddReversedLine(List<byte> reversedLine, List<HistoryEntry> entries)
    {
        var bytes = reversedLine.ToArray();
        Array.Reverse(bytes);
        var line = Encoding.UTF8.GetString(bytes).TrimEnd('\r');
        try
        {
            var entry = JsonSerializer.Deserialize<HistoryEntry>(line, jsonOptions);
            if (entry is not null) entries.Add(entry);
        }
        catch (JsonException)
        {
            // A partial or damaged line should not prevent access to the remaining local history.
        }
    }

    private async Task PruneIfNeededAsync(int maximumMegabytes, CancellationToken cancellationToken)
    {
        if (!File.Exists(historyPath)) return;
        var maximumBytes = maximumMegabytes * 1024L * 1024L;
        var length = new FileInfo(historyPath).Length;
        if (length <= maximumBytes) return;

        var bytesToKeep = (long)(maximumBytes * 0.9);
        var temporaryPath = historyPath + ".trim";
        try
        {
            await using (var source = new FileStream(
                             historyPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                source.Position = Math.Max(0, source.Length - bytesToKeep);
                if (source.Position > 0)
                {
                    var singleByte = new byte[1];
                    while (await source.ReadAsync(singleByte, cancellationToken) == 1 &&
                           singleByte[0] != (byte)'\n')
                    {
                    }
                }

                await using var destination = new FileStream(
                    temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(destination, cancellationToken);
            }

            File.Move(temporaryPath, historyPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public void Dispose()
    {
        disposed = true;
    }
}
