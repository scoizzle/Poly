global using System;
global using System.Buffers;
global using System.Collections;
global using System.Collections.Generic;
global using System.Diagnostics;
global using System.Diagnostics.CodeAnalysis;
global using System.Linq;
global using System.Runtime.CompilerServices;
global using System.Text;
global using System.Text.RegularExpressions;

global using Poly.Extensions;
global using Poly.Syntax;

global using static Poly.Syntax.NodeExtensions;
// Resolve 'Expression' ambiguity: the LINQ Expression is used in 36+ compiler
// files.  AST files that need Poly.Syntax.Nodes.Expression use the full name.
global using Expression = System.Linq.Expressions.Expression;

[assembly: InternalsVisibleTo("Poly.Tests")]
[assembly: InternalsVisibleTo("Poly.Benchmarks")]