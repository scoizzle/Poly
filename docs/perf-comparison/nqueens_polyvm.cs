using System;
using System.Linq;
using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;

int boardSize = args.Length > 0 ? int.Parse(args[0]) : 8;
bool debug = args.Length > 1 && args[1] == "--debug";

// Per-row arrays for the classic iterative N-Queens algorithm
var colsArr = new Variable("colsArr");
var ldArr = new Variable("ldArr");
var rdArr = new Variable("rdArr");
var triedArr = new Variable("triedArr");
var row = new Variable("row");
var total = new Variable("total");
var avail = new Variable("avail");
var bit = new Variable("bit");

// Helper: IndexAccess into an array
Node At(Variable arr, Node idx) => new IndexAccess(arr, idx);
Node Long(long v) => new Constant(v);

int allBits = (1 << boardSize) - 1;

var body = new Invoke(new Lambda([], new Block(
    [new Assignment(colsArr, new NewArray(TypeReference.To<long>(), new Constant(8))),
     new Assignment(ldArr, new NewArray(TypeReference.To<long>(), new Constant(8))),
     new Assignment(rdArr, new NewArray(TypeReference.To<long>(), new Constant(8))),
     new Assignment(triedArr, new NewArray(TypeReference.To<long>(), new Constant(8))),
     new Assignment(row, Long(0)),
     new Assignment(total, Long(0)),
     // Initialize row 0
     new Assignment(At(colsArr, Long(0)), Long(0)),
     new Assignment(At(ldArr, Long(0)), Long(0)),
     new Assignment(At(rdArr, Long(0)), Long(0)),
     new Assignment(At(triedArr, Long(0)), Long(0)),
     // Main backtracking loop
     new WhileLoop(new GreaterThanOrEqual(row, Long(0)), new Block([
         new Assignment(avail, new BitwiseAnd(
             new BitwiseAnd(
                 new BitwiseNot(new BitwiseOr(
                     new BitwiseOr(At(ldArr, row), At(colsArr, row)),
                     At(rdArr, row))),
                 Long(allBits)),
             new BitwiseNot(At(triedArr, row)))),
         new IfStatement(new Equal(avail, Long(0)), new Block([
             new Assignment(At(triedArr, row), Long(0)),
             new Assignment(row, new Subtract(row, Long(1)))
         ]), new Block([
             new Assignment(bit, new BitwiseAnd(new UnaryMinus(avail), avail)),
             new Assignment(At(triedArr, row),
                 new BitwiseOr(At(triedArr, row), bit)),
             new IfStatement(new Equal(row, Long(7)),
                 new Assignment(total, new Add(total, Long(1))),
                 new Block([
                     new Assignment(row, new Add(row, Long(1))),
                     new Assignment(At(colsArr, row),
                         new BitwiseOr(At(colsArr, new Subtract(row, Long(1))), bit)),
                     new Assignment(At(ldArr, row),
                         new ShiftLeft(
                             new BitwiseOr(At(ldArr, new Subtract(row, Long(1))), bit),
                             Long(1))),
                     new Assignment(At(rdArr, row),
                         new ShiftRight(
                             new BitwiseOr(At(rdArr, new Subtract(row, Long(1))), bit),
                             Long(1))),
                     new Assignment(At(triedArr, row), Long(0)),
                 ]))
         ])),
     ])),
     total],
    [colsArr, ldArr, rdArr, triedArr, row, total, avail, bit])));

var prepSw = System.Diagnostics.Stopwatch.StartNew();
var program = Interpreter.Compile(body, CompilationMode.NoDebug);
// Absorb first-call JIT of the Expression.Compile() delegate into prep.
using (var _ = Interpreter.Execute(program)) { }
prepSw.Stop();

var sw = System.Diagnostics.Stopwatch.StartNew();
using var exec = Interpreter.Execute(program);
long result = exec.RawValue;
sw.Stop();
Console.WriteLine($"Poly VM,{boardSize},{result},{sw.Elapsed.TotalMicroseconds:F0},{prepSw.ElapsedMilliseconds}");
