using System.Text.Json.Serialization;

using Poly.Interpretation;
using Poly.Introspection;
using Poly.Validation;

namespace Poly.DataModeling;

public abstract record DataProperty(string Name, IEnumerable<Constraint> Constraints) : ITypeMember {
    [JsonIgnore]
    public ITypeDefinition MemberTypeDefinition { get; }
    
    [JsonIgnore]
    public ITypeDefinition DeclaringTypeDefinition { get; }
    public IEnumerable<IParameter>? Parameters { get; }

    public Value GetMemberAccessor(Value instance, params IEnumerable<Value>? parameters) {
        throw new NotImplementedException();
    }
}