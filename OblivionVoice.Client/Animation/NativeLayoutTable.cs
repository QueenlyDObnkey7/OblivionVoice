namespace OblivionVoice.Client.Animation;

internal sealed record NativeFunctionLayout(int Size, Dictionary<string, (int Offset, int Size)> Fields);

internal static class NativeLayoutTable
{
    internal static Dictionary<string, NativeFunctionLayout> Read(TextReader reader)
    {
        var result = new Dictionary<string, NativeFunctionLayout>(StringComparer.Ordinal);
        int lineNumber = 0;
        while (reader.ReadLine() is { } raw)
        {
            lineNumber++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('|');
            InvalidDataException Invalid() => new($"Invalid native contract at line {lineNumber}.");
            if (parts.Length < 2 || parts[0].Length == 0 || !int.TryParse(parts[1], out int size) || size < 0 || size > 65535)
                throw Invalid();
            var fields = new Dictionary<string, (int Offset, int Size)>(StringComparer.Ordinal);
            foreach (string part in parts.Skip(2))
            {
                var field = part.Split(':');
                if (field.Length != 3 || field[0].Length == 0 || !int.TryParse(field[1], out int offset) ||
                    !int.TryParse(field[2], out int width) || offset < 0 || width < 0 || offset > size ||
                    width > size - offset || !fields.TryAdd(field[0], (offset, width))) throw Invalid();
            }
            if (!result.TryAdd(parts[0], new(size, fields))) throw Invalid();
        }
        return result;
    }
}
