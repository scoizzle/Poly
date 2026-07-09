using System;
using System.Linq;
using Poly.Syntax;
using Poly.Syntax.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;

int limit = args.Length > 0 ? int.Parse(args[0]) : 1000;
bool debug = args.Length > 1 && args[1] == "--debug";

var n = new Variable("n"); var i = new Variable("i");
var len = new Variable("len"); var maxLen = new Variable("maxLen");
var bestN = new Variable("bestN");

var body = new Invoke(new Lambda([], new Block(
    [new Assignment(maxLen, new Constant(0L)),
     new Assignment(bestN, new Constant(0L)),
     new Assignment(n, new Constant(1L)),
     new WhileLoop(new LessThanOrEqual(n, new Constant(limit)),
         new Block([
             new Assignment(len, new Constant(0L)),
             new Assignment(i, n),
             new WhileLoop(new NotEqual(i, new Constant(1L)),
                 new Block([
                     new Assignment(i, new Conditional(
                         new Equal(new Modulo(i, new Constant(2L)), new Constant(0L)),
                         new ShiftRight(i, new Constant(1)),
                         new Add(new Multiply(i, new Constant(3L)), new Constant(1L)))),
                     new Assignment(len, new Add(len, new Constant(1L)))
                 ])),
             new IfStatement(
                 new GreaterThan(len, maxLen),
                 new Block([
                     new Assignment(maxLen, len),
                     new Assignment(bestN, n)
                 ])),
              new Assignment(n, new Add(n, new Constant(1L)))
          ])),
      new BitwiseOr(new ShiftLeft(bestN, new Constant(32L)), maxLen)],
    [n, i, len, maxLen, bestN])));

var prepSw = System.Diagnostics.Stopwatch.StartNew();
var program = Interpreter.Compile(body, CompilationMode.NoDebug);
prepSw.Stop();

long result;
var sw = System.Diagnostics.Stopwatch.StartNew();
if (debug) {
    using var exec = Interpreter.Execute(program, s => s.Trace = Console.Error);
    result = exec.RawValue;
} else {
    using var exec = Interpreter.Execute(program);
    result = exec.RawValue;
}
sw.Stop();
long bestNVal = result >> 32;
long maxLenVal = result & 0xFFFFFFFFL;
Console.WriteLine($"Poly VM,{limit},{bestNVal}:{maxLenVal},{sw.Elapsed.TotalMicroseconds:F0},{prepSw.ElapsedMilliseconds}");
