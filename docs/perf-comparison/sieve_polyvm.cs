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
using Poly.Interpretation.Analysis.LoweringPrep;
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

var analysisResult = new AnalyzerBuilder()
    .UseTypeAndMemberResolver()
    .UseConstantFolding()
    .UseSideEffectAnalysis()
    .UseThisReferenceContext()
    .UseControlFlowAnalysis()
    .UseVariableScopeValidator()
            .UseLoweringPreparation()
            .UseUopGeneration()
    .Build()
    .Analyze(body, setup: ctx => {
        var t = ctx.TypeDefinitions;
        ctx.SetResolvedType(bits, t.GetTypeDefinition(typeof(long[])));
        ctx.SetResolvedType(i, t.GetTypeDefinition(typeof(int)));
        ctx.SetResolvedType(cnt, t.GetTypeDefinition(typeof(long)));
    });

var prepSw = System.Diagnostics.Stopwatch.StartNew();
var lowered = Lowering.Lower(body, analysisResult);
var program = ProgramCompiler.Compile(lowered, mode: CompilationMode.NoDebug);
prepSw.Stop();

var state = new VmState(program);
var sw = System.Diagnostics.Stopwatch.StartNew();
Vm.Execute(state);
long result = state.Stack.Pop();
sw.Stop();
Console.WriteLine($"Poly VM,{limit},{result},{sw.ElapsedMilliseconds},{prepSw.ElapsedMilliseconds}");
