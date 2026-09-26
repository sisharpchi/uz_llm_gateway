using System.Runtime.CompilerServices;
using System.Text;

namespace UZLLM.Provider.DeepSeek;

internal sealed record DeepSeekSseFrame(string? EventType, string Data);

internal static class DeepSeekSseReader
{
    private const int MaximumFrameCharacters = 1_048_576;

    public static async IAsyncEnumerable<DeepSeekSseFrame> ReadAsync(Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true);
        var data = new StringBuilder();
        var line = new StringBuilder();
        string? eventType = null;
        var buffer = new char[8192];
        var afterCarriageReturn = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            for (var index = 0; index < count; index++)
            {
                var character = buffer[index];
                if (afterCarriageReturn && character == '\n')
                {
                    afterCarriageReturn = false;
                    continue;
                }
                afterCarriageReturn = character == '\r';
                if (character is '\r' or '\n')
                {
                    var frame = ConsumeLine(line.ToString(), data, ref eventType);
                    line.Clear();
                    if (frame is not null) yield return frame;
                }
                else
                {
                    if (line.Length == MaximumFrameCharacters)
                        throw new InvalidDataException("DeepSeek stream line exceeded its limit.");
                    line.Append(character);
                }
            }
        }
        if (line.Length != 0)
        {
            var frame = ConsumeLine(line.ToString(), data, ref eventType);
            if (frame is not null) yield return frame;
        }
        if (data.Length != 0) yield return new DeepSeekSseFrame(eventType, data.ToString());
    }

    private static DeepSeekSseFrame? ConsumeLine(string line, StringBuilder data,
        ref string? eventType)
    {
        if (line.Length == 0)
        {
            if (data.Length == 0) return null;
            var frame = new DeepSeekSseFrame(eventType, data.ToString());
            data.Clear();
            eventType = null;
            return frame;
        }
        if (line[0] == ':') return null;
        if (line.StartsWith("event:", StringComparison.Ordinal))
            eventType = line[6..].TrimStart();
        else if (line.StartsWith("data:", StringComparison.Ordinal))
        {
            var fragment = line[5..].TrimStart();
            if (data.Length + fragment.Length + (data.Length == 0 ? 0 : 1) > MaximumFrameCharacters)
                throw new InvalidDataException("DeepSeek stream frame exceeded its limit.");
            if (data.Length != 0) data.Append('\n');
            data.Append(fragment);
        }
        return null;
    }
}
