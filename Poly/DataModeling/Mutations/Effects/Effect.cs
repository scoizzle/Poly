namespace Poly.DataModeling.Mutations.Effects;

public abstract record Effect;

public sealed record SetEffect(string PropertyName, Poly.DataModeling.Mutations.ValueSource Value) : Effect;

public sealed record IncrementEffect(string PropertyName, Poly.DataModeling.Mutations.ValueSource Amount) : Effect;

public sealed record AddToEffect(string PropertyName, Poly.DataModeling.Mutations.ValueSource Item) : Effect;

public sealed record RemoveFromEffect(string PropertyName, Poly.DataModeling.Mutations.ValueSource Item) : Effect;
