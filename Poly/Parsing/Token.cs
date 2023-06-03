// #pragma warning disable IDE1006 // Naming rule violation: Missing prefix: 'I'

// using System.Buffers;

// namespace Poly.Parsing;

// public interface Token {
//     SequencePosition Begin { get; }
//     SequencePosition End { get; }
// }

// public struct Token<T> : Token where T : unmanaged, IEquatable<T>
// {
//     public required ReadOnlySequence<T> Value { get; init; }

//     public required SequencePosition Begin { get; init; }

//     public required SequencePosition End { get; init; }

//     public override string ToString()
//         => string.Concat(Value);
// }

// public struct StaticToken<T> : Token where T : unmanaged, IEquatable<T> 
// { 
//     public required ReadOnlySequence<T> Value { get; init; }

//     public required SequencePosition Begin { get; init; }

//     public required SequencePosition End { get; init; }

//     public override string ToString()
//         => string.Concat(Value);
// }

// public class VariantToken<T> : Token where T : unmanaged, IEquatable<T> { 
//     public required ReadOnlySequence<T> Value { get; init; }

//     public required SequencePosition Begin { get; init; }

//     public required SequencePosition End { get; init; }

//     public override string ToString()
//         => string.Concat(Value);
// }