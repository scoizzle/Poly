using System;
using System.Linq;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.VirtualMachine;

int limit = args.Length > 0 ? int.Parse(args[0]) : 1000000;
int wordCnt = (limit + 64) / 64;
var bits = new Variable("bits");
var i = new Variable("i");
var j = new Variable("j");
var cnt = new Variable("cnt");

Node Wi(Node x) => new ShiftRight(x, new Constant(6));
Node Bi(Node x) => new BitwiseAnd(x, new Constant(63L));
Node Bit(Node x) => new ShiftLeft(new Constant(1L), Bi(x));
Node IsPrime(Node x) => new Equal(
    new BitwiseAnd(new ShiftRight(new IndexAccess(bits, Wi(x)), Bi(x)), new Constant(1L)),
    new Constant(0L));

var body = new Block(
    [new Assignment(bits, new NewArray(TypeReference.To<long>(), new Constant(wordCnt))),
     new Assignment(i, new Constant(2)),
     new WhileLoop(new LessThanOrEqual(new Multiply(i, i), new Constant(limit)),
         new Block([
             new IfStatement(IsPrime(i),
                 new Block([
                     new Assignment(j, new Multiply(i, i)),
                     new WhileLoop(new LessThanOrEqual(j, new Constant(limit)),
                         new Block([
                             new Assignment(new IndexAccess(bits, Wi(j)),
                                 new BitwiseOr(new IndexAccess(bits, Wi(j)), Bit(j))),
                             new Assignment(j, new Add(j, i))
                         ]))
                 ])),
             new Assignment(i, new Add(i, new Constant(1)))
         ])),
     new Assignment(cnt, new Constant(0L)),
     new Assignment(i, new Constant(2)),
     new WhileLoop(new LessThanOrEqual(i, new Constant(limit)),
         new Block([
             new Assignment(cnt, new Add(cnt, new Conditional(IsPrime(i),
                 new Constant(1L), new Constant(0L)))),
             new Assignment(i, new Add(i, new Constant(1)))
         ])),
     cnt],
    [bits, i, j, cnt]);

var analysisResult = new AnalyzerBuilder()
    .UseTypeAndMemberResolver()
    .UseConstantFolding()
    .UseSideEffectAnalysis()
    .UseThisReferenceContext()
    .UseControlFlowAnalysis()
    .UseVariableScopeValidator()
    .Build()
    .Analyze(body, setup: ctx => {
        var t = ctx.TypeDefinitions;
        ctx.SetResolvedType(bits, t.GetTypeDefinition(typeof(long[])));
        ctx.SetResolvedType(i, t.GetTypeDefinition(typeof(int)));
        ctx.SetResolvedType(j, t.GetTypeDefinition(typeof(int)));
        ctx.SetResolvedType(cnt, t.GetTypeDefinition(typeof(long)));
    });

var compiler = new VmCompiler(analysisResult);
var sieve = compiler.CompileAsDelegate<Func<long>>(body);

var sw = System.Diagnostics.Stopwatch.StartNew();
long result = sieve();
sw.Stop();
Console.WriteLine($"Poly VM,{limit},{result},{sw.ElapsedMilliseconds}");
