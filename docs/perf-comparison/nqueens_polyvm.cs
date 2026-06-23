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

int boardSize = args.Length > 0 ? int.Parse(args[0]) : 8;
bool debug = args.Length > 1 && args[1] == "--debug";

var stack = new Variable("stack");
var sp = new Variable("sp");
var total = new Variable("total");
var ld = new Variable("ld"); var cols = new Variable("cols");
var rd = new Variable("rd");
var avail = new Variable("avail"); var bit = new Variable("bit");

long allBits = (1L << boardSize) - 1;
// Stack depth for bitboard n-queens: worst-case ~boardSize! placements
// Each state uses 3 longs.  Give plenty of room.
int stackSize = boardSize * boardSize * boardSize * 3;

Node StackAt(Node idx) => new IndexAccess(stack, idx);
Node Long(long v) => new Constant(v);

var body = new Invoke(new Lambda([], new Block(
    [new Assignment(stack, new NewArray(TypeReference.To<long>(), new Constant(stackSize))),
     new Assignment(sp, Long(0)),
     new Assignment(total, Long(0)),
     new Assignment(StackAt(sp), Long(0)),
     new Assignment(StackAt(new Add(sp, Long(1))), Long(0)),
     new Assignment(StackAt(new Add(sp, Long(2))), Long(0)),
     new Assignment(sp, new Add(sp, Long(3))),
     new WhileLoop(new GreaterThan(sp, Long(0)), new Block([
         new Assignment(sp, new Subtract(sp, Long(3))),
         new Assignment(ld, StackAt(sp)),
         new Assignment(cols, StackAt(new Add(sp, Long(1)))),
         new Assignment(rd, StackAt(new Add(sp, Long(2)))),
         new IfStatement(new Equal(cols, Long(allBits)),
             new Assignment(total, new Add(total, Long(1)))),
         new Assignment(avail, new BitwiseAnd(
             new BitwiseNot(new BitwiseOr(new BitwiseOr(ld, cols), rd)),
             Long(allBits))),
         new WhileLoop(new NotEqual(avail, Long(0)), new Block([
             new Assignment(bit, new BitwiseAnd(new UnaryMinus(avail), avail)),
             new Assignment(avail, new BitwiseXor(avail, bit)),
             new Assignment(StackAt(sp),
                 new ShiftLeft(new BitwiseOr(ld, bit), Long(1))),
             new Assignment(StackAt(new Add(sp, Long(1))),
                 new BitwiseOr(cols, bit)),
             new Assignment(StackAt(new Add(sp, Long(2))),
                 new ShiftRight(new BitwiseOr(rd, bit), Long(1))),
             new Assignment(sp, new Add(sp, Long(3))),
         ])),
     ])),
     total],
    [stack, sp, total, ld, cols, rd, avail, bit])));

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
        ctx.SetResolvedType(stack, t.GetTypeDefinition(typeof(long[])));
        ctx.SetResolvedType(sp, t.GetTypeDefinition(typeof(long)));
        ctx.SetResolvedType(total, t.GetTypeDefinition(typeof(long)));
        ctx.SetResolvedType(ld, t.GetTypeDefinition(typeof(long)));
        ctx.SetResolvedType(cols, t.GetTypeDefinition(typeof(long)));
        ctx.SetResolvedType(rd, t.GetTypeDefinition(typeof(long)));
        ctx.SetResolvedType(avail, t.GetTypeDefinition(typeof(long)));
        ctx.SetResolvedType(bit, t.GetTypeDefinition(typeof(long)));
    });

var prepSw = System.Diagnostics.Stopwatch.StartNew();
var lowered = Lowering.Lower(body, analysisResult);
var program = ProgramCompiler.Compile(lowered, mode: CompilationMode.NoDebug);
prepSw.Stop();

using var state = new VmState(program);
if (debug)
    state.Trace = Console.Error;
var sw = System.Diagnostics.Stopwatch.StartNew();
Vm.Execute(state);
long result = state.Stack.Pop();
sw.Stop();
Console.WriteLine($"Poly VM,{boardSize},{result},{sw.ElapsedMilliseconds},{prepSw.ElapsedMilliseconds}");
