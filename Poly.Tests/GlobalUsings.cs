global using Poly.Interpretation.Analysis;
global using Poly.Syntax;
global using Poly.Syntax.Analysis;
global using Poly.Syntax.Nodes;

global using TUnit.Assertions;
global using TUnit.Assertions.Extensions;
global using TUnit.Core;

global using static Poly.Syntax.NodeExtensions;
// Resolve same ambiguity as the Poly project (Expression = LINQ, not AST)
global using Expression = System.Linq.Expressions.Expression;