global using Poly.Analysis;
global using Poly.Ast;
global using Poly.Ast.Nodes;
global using Poly.Interpretation.Analysis;

global using TUnit.Assertions;
global using TUnit.Assertions.Extensions;
global using TUnit.Core;

global using static Poly.Ast.NodeExtensions;
// Resolve same ambiguity as the Poly project (Expression = LINQ, not AST)
global using Expression = System.Linq.Expressions.Expression;