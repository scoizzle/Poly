// namespace Poly.Parsing;

// public interface ITokenParsingResult
// {
//     bool Success { get; }
//     SequencePosition Begin { get; }
//     SequencePosition End { get; }
// }

// public record struct TokenParsingResult(
//     bool Success,
//     SequencePosition Begin,
//     SequencePosition End) : ITokenParsingResult
// {
//     public static readonly TokenParsingResult Failure = new(false, default, default);
// }

// public record struct TokenParsingResult<TResult>(
//     bool Success,
//     SequencePosition Begin,
//     SequencePosition End,
//     TResult Value) : ITokenParsingResult
// {
//     public static readonly TokenParsingResult<TResult> Failure = new(false, default, default, default);
// }