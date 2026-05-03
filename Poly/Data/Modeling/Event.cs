using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

/// <summary>
/// Represents an event that can be published as part of a model's effects. 
/// Events are used to trigger side effects in the system, such as notifying other components or updating external systems. 
/// Each event has a name and can have associated properties that provide additional context about the event.
/// </summary>
/// <remarks>
/// Effectively, within the Domain's type system, an Event is equivalent to the following in C#:
/// <code>
/// public partial class Domain {
///     public partial class Model {
///         public record `EventName`(Type1 Property1, Type2 Property2, ...);
/// 
///         internal void On`EventName`(On`EventName` eventArgs) {
///             // Handle the event, e.g., by invoking side effects or updating state.
///         }
///     }
/// }
/// </code>
/// </remarks>
public sealed partial record Event(Domain Domain, string Name) : DomainType(Domain, Name) {
}