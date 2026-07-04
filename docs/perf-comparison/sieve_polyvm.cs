using System;
using System.Linq;
using Poly.Syntax;
using Poly.Syntax.Nodes;
using Poly.Interpretation;
using Prim = Poly.Syntax.Primitives;
using Poly.Interpretation.Vm;

int limit = args.Length > 0 ? int.Parse(args[0]) : 1000000;
int wordCnt = (limit + 64) / 64;
var bits = new Variable("bits");
var i = new Variable("i");
var cnt = new Variable("cnt");
var w = new Variable("w");

Node IsPrime(Node x) => new Equal(
    new BitwiseAnd(new ShiftRight(new IndexAccess(bits,
        new ShiftRight(x, new Constant(6))),
        new BitwiseAnd(x, new Constant(63L))), new Constant(1L)),
    new Constant(0L));

var body = new Block(
    [new Assignment(bits, new NewArray(TypeReference.To<long>(), new Constant(wordCnt))),
     new Assignment(i, new Constant(2)),
     new WhileLoop(new LessThanOrEqual(new Multiply(i, i), new Constant(limit)),
         new Block([
             new IfStatement(IsPrime(i),
                 new Block([
                     new StridedSetBits(bits, new Multiply(i, i), i, new Constant(limit))
                 ])),
             new Assignment(i, new Add(i, new Constant(1)))
         ])),
     // Count primes via word-level PopCount (hardware POPCNT)
     new Assignment(cnt, new Constant(0L)),
     new Assignment(w, new Constant(0L)),
     new WhileLoop(new LessThan(w, new Constant(wordCnt - 1)),
         new Block([
             new Assignment(cnt, new Add(cnt, new PopCount(
                 new BitwiseNot(new IndexAccess(bits, w))))),
             new Assignment(w, new Add(w, new Constant(1L)))
         ])),
     // Last word: mask out bits beyond limit
     new Assignment(cnt, new Add(cnt, new PopCount(
         new BitwiseAnd(
             new BitwiseNot(new IndexAccess(bits, new Constant(wordCnt - 1))),
             new Constant((limit % 64) == 63 ? -1L : (1L << ((limit & 63) + 1)) - 1L))))),
     // Subtract phantom primes at positions 0 and 1
     new Assignment(cnt, new Subtract(cnt, new Constant(2L))),
     cnt],
    [bits, i, cnt, w]);

var prepSw = System.Diagnostics.Stopwatch.StartNew();
var program = InterpretationAnalyzer.Compile(body, CompilationMode.NoDebug);
prepSw.Stop();

var sw = System.Diagnostics.Stopwatch.StartNew();
using var exec = Vm.Execute(program);
long result = exec.RawValue;
sw.Stop();
Console.WriteLine($"Poly VM,{limit},{result},{sw.ElapsedMilliseconds},{prepSw.ElapsedMilliseconds}");
