namespace Poly.Compile;

/// <summary>Converts character offsets to 1-based line/column positions for one text.</summary>
public sealed class LineMap {
    private readonly List<int> _starts = [0];
    private readonly int _length;

    public LineMap(string text) {
        _length = text.Length;
        for (var i = 0; i < text.Length; i++) {
            if (text[i] == '\n')
                _starts.Add(i + 1);
            else if (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))
                _starts.Add(i + 1);
        }
    }

    public (int Line, int Column) At(int offset) {
        offset = Math.Clamp(offset, 0, _length);
        var index = _starts.BinarySearch(offset);
        if (index < 0)
            index = ~index - 1;
        return (index + 1, offset - _starts[index] + 1);
    }

    public int OffsetOf(int line, int column) {
        line = Math.Clamp(line, 1, _starts.Count);
        return Math.Min(_length, _starts[line - 1] + Math.Max(column, 1) - 1);
    }
}