namespace Poly.Validation;

public sealed record ValidationError(string Path, string Code, string Message) {
    public override string ToString() => $"{Path}: {Message} ({Code})";
}