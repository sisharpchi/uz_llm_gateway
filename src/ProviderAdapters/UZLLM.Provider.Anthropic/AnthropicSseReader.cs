using System.Runtime.CompilerServices;
using System.Text;

namespace UZLLM.Provider.Anthropic;

internal sealed record AnthropicSseFrame(string? EventType, string Data);

internal static class AnthropicSseReader
{
    private const int MaximumFrameCharacters = 1_048_576;

    public static async IAsyncEnumerable<AnthropicSseFrame> ReadAsync(Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 8192, leaveOpen: true);
        var data = new StringBuilder();
        string? eventType = null;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length > MaximumFrameCharacters || data.Length + line.Length > MaximumFrameCharacters)
                throw new InvalidDataException("Provider stream frame exceeded its limit.");
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    yield return new AnthropicSseFrame(eventType, data.ToString());
                    data.Clear();
                    eventType = null;
                }
                continue;
            }
            if (line[0] == ':') continue;
            if (line.StartsWith("event:", StringComparison.Ordinal))
                eventType = line[6..].TrimStart();
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line[5..].TrimStart());
            }
        }
        if (data.Length > 0) yield return new AnthropicSseFrame(eventType, data.ToString());
    }
}
