namespace Poly.Compile;

/// <summary>Builds <see cref="CompileDiagnostic"/> values for one file and text.</summary>
internal sealed class DiagnosticBuilder {
    private readonly string _file;
    private readonly string _text;
    private readonly LineMap _lines;

    public DiagnosticBuilder(string file, string text) {
        _file = file;
        _text = text;
        _lines = new LineMap(text);
    }

    public LineMap Lines => _lines;

    public string TextAt(int offset, int length) =>
        offset < 0 || offset + length > _text.Length ? "" : _text.Substring(offset, length);

    public CompileDiagnostic Span(
        int offset, int length, string severity, string code, string message, string path) {
        var start = Math.Clamp(offset, 0, _text.Length);
        var end = Math.Clamp(start + Math.Max(length, 1), start, Math.Max(_text.Length, start));
        if (end == start)
            end = start;
        var (line, column) = _lines.At(start);
        var (endLine, endColumn) = end == start ? (line, column + 1) : _lines.At(end);
        return new CompileDiagnostic(_file, line, column, endLine, endColumn, severity, code, message, path, Placed: true);
    }

    public CompileDiagnostic Unplaced(string severity, string code, string message, string path = "") =>
        new(_file, 1, 1, 1, 2, severity, code, message, path, Placed: false);

    /// <summary>Length of the word starting at <paramref name="offset"/>, at least 1.</summary>
    public int WordLength(int offset) {
        var i = offset;
        while (i >= 0 && i < _text.Length && (char.IsLetterOrDigit(_text[i]) || _text[i] == '_'))
            i++;
        return Math.Max(i - offset, 1);
    }
}