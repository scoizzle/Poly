namespace Poly.Serialization;

public record struct JsonWriterOptions(
    bool PrettyPrint = false,
    int MaxDepth = 256
);
