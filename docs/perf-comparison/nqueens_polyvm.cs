using System;
using System.Linq;
using Poly.Syntax;
using Poly.Syntax.Nodes;
using Poly.Interpretation;
using Prim = Poly.Syntax.Primitives;
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

var prepSw = System.Diagnostics.Stopwatch.StartNew();
var program = InterpretationAnalyzer.Compile(body, CompilationMode.NoDebug);
prepSw.Stop();

var sw = System.Diagnostics.Stopwatch.StartNew();
using var exec = Vm.Execute(program);
long result = exec.RawValue;
sw.Stop();
Console.WriteLine($"Poly VM,{boardSize},{result},{sw.ElapsedMilliseconds},{prepSw.ElapsedMilliseconds}");
