using System.Security.Cryptography;

namespace Poly.DomainModeling.V2.Core;

public sealed record SemanticId {
    private const string UlidAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public string Value { get; }

    public SemanticId()
        : this(GenerateUlid())
    {
    }

    public SemanticId(string value)
    {
        if (string.IsNullOrEmpty(value)) {
            throw new ArgumentException("SemanticId cannot be null or empty.", nameof(value));
        }

        if (value.Length > 128) {
            throw new ArgumentException("SemanticId cannot exceed 128 characters.", nameof(value));
        }

        if (!IsPrintableAsciiWithoutWhitespace(value)) {
            throw new ArgumentException("SemanticId must contain only printable ASCII characters with no whitespace.", nameof(value));
        }

        Value = value;
    }

    public override string ToString() => Value;

    private static bool IsPrintableAsciiWithoutWhitespace(string input)
    {
        foreach (var ch in input) {
            if (char.IsWhiteSpace(ch)) {
                return false;
            }

            if (ch < 33 || ch > 126) {
                return false;
            }
        }

        return true;
    }

    private static string GenerateUlid()
    {
        var bytes = new byte[16];
        var timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        bytes[0] = (byte)(timestamp >> 40);
        bytes[1] = (byte)(timestamp >> 32);
        bytes[2] = (byte)(timestamp >> 24);
        bytes[3] = (byte)(timestamp >> 16);
        bytes[4] = (byte)(timestamp >> 8);
        bytes[5] = (byte)timestamp;

        RandomNumberGenerator.Fill(bytes.AsSpan(6, 10));

        var output = new char[26];
        var buffer = 0;
        var bitsInBuffer = 0;
        var outputIndex = 0;

        foreach (var b in bytes) {
            buffer = (buffer << 8) | b;
            bitsInBuffer += 8;

            while (bitsInBuffer >= 5) {
                var index = (buffer >> (bitsInBuffer - 5)) & 31;
                output[outputIndex++] = UlidAlphabet[index];
                bitsInBuffer -= 5;
            }
        }

        if (bitsInBuffer > 0) {
            var index = (buffer << (5 - bitsInBuffer)) & 31;
            output[outputIndex++] = UlidAlphabet[index];
        }

        while (outputIndex < output.Length) {
            output[outputIndex++] = UlidAlphabet[0];
        }

        return new string(output);
    }
}