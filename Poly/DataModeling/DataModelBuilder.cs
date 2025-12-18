namespace Poly.DataModeling;

public sealed class DataModelBuilder {
    private readonly List<DataType> _dataTypes;
    private readonly List<Relationship> _relationships;

    public DataModelBuilder() {
        _dataTypes = new List<DataType>();
        _relationships = new List<Relationship>();
    }

    public IEnumerable<DataType> DataTypes => _dataTypes;
    public IEnumerable<Relationship> Relationships => _relationships;

    public DataModelBuilder(DataModel dataModel) {
        ArgumentNullException.ThrowIfNull(dataModel);
        _dataTypes = [.. dataModel.Types];
        _relationships = [.. dataModel.Relationships];
    }

    public DataModelBuilder AddDataType(DataType dataType) {
        ArgumentNullException.ThrowIfNull(dataType);
        _dataTypes.Add(dataType);
        return this;
    }

    public DataModelBuilder AddDataType(string name, Action<DataTypeBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        DataTypeBuilder builder = new(name);
        configure(builder);
        _dataTypes.Add(builder.Build());

        // Collect relationships from the builder
        foreach (var relationshipBuilder in builder.Relationships) {
            _relationships.Add(relationshipBuilder.Build());
        }

        return this;
    }

    public DataModelBuilder AddRelationship(Relationship relationship) {
        ArgumentNullException.ThrowIfNull(relationship);
        _relationships.Add(relationship);
        return this;
    }

    public DataModel Build() => new DataModel(_dataTypes, _relationships);
}