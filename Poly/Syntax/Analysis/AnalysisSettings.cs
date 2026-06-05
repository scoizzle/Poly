namespace Poly.Syntax.Analysis;

/// <summary>
/// Run-level analysis settings carried by <see cref="AnalysisContext"/>.
/// These settings are global to a single analysis execution and are distinct
/// from per-node metadata stored in <see cref="NodeMetadataStore"/>.
/// </summary>
public sealed record AnalysisSettings {
    private readonly IReadOnlyDictionary<Type, object> _values;

    public static AnalysisSettings Default { get; } = new();

    public AnalysisSettings()
        : this(new Dictionary<Type, object> {
            [typeof(AnalysisDiagnosticConfiguration)] = AnalysisDiagnosticConfiguration.Default
        }) {
    }

    private AnalysisSettings(IReadOnlyDictionary<Type, object> values) {
        _values = values;
    }

    public TSetting? Get<TSetting>() where TSetting : class {
        return _values.TryGetValue(typeof(TSetting), out var value)
            ? (TSetting)value
            : null;
    }

    public AnalysisSettings With<TSetting>(TSetting setting) where TSetting : class {
        ArgumentNullException.ThrowIfNull(setting);

        var clone = new Dictionary<Type, object>(_values) {
            [typeof(TSetting)] = setting
        };

        return new AnalysisSettings(clone);
    }
}