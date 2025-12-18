namespace Poly.DataModeling.Mutations.Effects;

public abstract record Effect;

public sealed record AssignEffect(DataPropertyPath DestinationPath, ValueSource Value) : Effect;

public sealed record IncrementEffect(DataPropertyPath DestinationPath, ValueSource Amount) : Effect;

public sealed record AddToEffect(DataPropertyPath DestinationPath, ValueSource Item) : Effect;

public sealed record RemoveFromEffect(DataPropertyPath DestinationPath, ValueSource Item) : Effect;