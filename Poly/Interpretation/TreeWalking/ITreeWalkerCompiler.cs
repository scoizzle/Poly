using System;
using System.Collections.Generic;

using Poly.Syntax;

namespace Poly.Interpretation.TreeWalking;

/// <summary>
/// Pluggable compiler interface for the tree-walking VM.
/// This is the key extensibility point that allows hybrid execution:
/// some nodes can be executed via pre-compiled Linq delegates while others
/// are interpreted by the tree walker.
/// </summary>
public interface ITreeWalkerCompiler {
    bool TryEvaluate(
        Node node,
        Func<Node, InterpreterState, InterpreterResult> evaluateChild,
        InterpreterState state,
        out InterpreterResult result);
}