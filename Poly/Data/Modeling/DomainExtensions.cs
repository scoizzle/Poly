using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public static class DomainExtensions {
    extension(Domain domain) {
        public IEnumerable<DomainType> Types => domain.Objects.OfType<DomainType>();
        public IEnumerable<Entity> Entities => domain.Objects.OfType<Entity>();
        public IEnumerable<Actor> Actors => domain.Objects.OfType<Actor>();
        public IEnumerable<Relationship> Relationships => domain.Objects.OfType<Relationship>();
    }
}