
namespace Poly.DataModeling;

[DebuggerDisplay("{_fullPath.Value}")]
public sealed record DataPropertyPath {
    private readonly Lazy<string> _fullPath;
    private readonly Lazy<IEnumerable<string>> _segments;

    public DataPropertyPath(string fullPath) {
        ArgumentException.ThrowIfNullOrEmpty(fullPath);
        (_fullPath, _segments) = (new(fullPath), new(GetSegments));
    }

    public DataPropertyPath(params IEnumerable<string> segments) {
        if (segments == null || !segments.Any()) {
            throw new ArgumentException("Segments cannot be null or empty.", nameof(segments));
        }

        (_fullPath, _segments) = (new(GetFullPath), new(segments));
    }
    
    public string FullPath {
        get => _fullPath.Value;
        init {
            ArgumentException.ThrowIfNullOrEmpty(value);
            (_fullPath, _segments) = (new(value), new(GetSegments));
        }
    }
    
    public IEnumerable<string> Segments {
        get => _segments.Value;
        init {
            if (value == null || !value.Any()) {
                throw new ArgumentException("Segments cannot be null or empty.", nameof(value));
            }
            
            (_fullPath, _segments) = (new(GetFullPath), new(value));
        }
    }
    
    public override string ToString() => _fullPath.Value;

    public static implicit operator DataPropertyPath(string fullPath) => new(fullPath);

    private IEnumerable<string> GetSegments() => _fullPath.Value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private string GetFullPath() => string.Join('.', _segments.Value);
}