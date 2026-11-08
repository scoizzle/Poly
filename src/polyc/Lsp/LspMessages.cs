using System.Text;
using System.Text.Json.Nodes;

namespace Polyc.Lsp;

/// <summary>Content-Length framed JSON-RPC over a pair of streams.</summary>
internal sealed class LspTransport(Stream input, Stream output) {
    private readonly object _writeGate = new();

    /// <summary>Next message, or null at end of stream.</summary>
    public JsonNode? Read() {
        var length = -1;
        while (true) {
            var line = ReadLine();
            if (line is null)
                return null;
            if (line.Length == 0)
                break;
            var colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(line[(colon + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
        if (length < 0)
            throw new InvalidDataException("LSP message has no Content-Length header.");
        var body = new byte[length];
        var read = 0;
        while (read < length) {
            var n = input.Read(body, read, length - read);
            if (n == 0)
                return null;
            read += n;
        }
        return JsonNode.Parse(body);
    }

    public void Write(JsonNode message) {
        var body = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        lock (_writeGate) {
            output.Write(header);
            output.Write(body);
            output.Flush();
        }
    }

    private string? ReadLine() {
        var bytes = new List<byte>();
        while (true) {
            var b = input.ReadByte();
            if (b < 0)
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString([.. bytes]);
            if (b == '\n')
                return Encoding.ASCII.GetString([.. bytes]).TrimEnd('\r');
            bytes.Add((byte)b);
        }
    }
}