using System.Linq.Expressions;
using System.Reflection;

namespace Poly.Interpretation.VirtualMachine;

internal delegate void CallSiteDelegate(VmState state);

internal static class CallSiteCompiler {
    // ── Cached MethodInfo / PropertyInfo via compile-time-safe expression trees ──
    private static readonly MethodInfo DropMethod = MemberHelper.MethodOf(() => default(ValueStack)!.Drop(0));
    private static readonly MethodInfo PushIntMethod = MemberHelper.MethodOf(() => default(ValueStack)!.Push(0));
    private static readonly MethodInfo UnsafeGetMethod = MemberHelper.MethodOf(() => default(Heap)!.UnsafeGet(0));
    private static readonly MethodInfo AllocateMethod = MemberHelper.MethodOf(() => default(Heap)!.Allocate(null));
    private static readonly PropertyInfo StackProp = MemberHelper.PropertyOf(() => default(VmState)!.Stack);
    private static readonly PropertyInfo SpProp = MemberHelper.PropertyOf(() => default(ValueStack)!.SP);
    private static readonly PropertyInfo RawSlotsProp = MemberHelper.PropertyOf(() => default(ValueStack)!.RawSlots);
    private static readonly PropertyInfo HeapProp = MemberHelper.PropertyOf(() => default(VmState)!.Heap);
    private static readonly PropertyInfo CountProp = MemberHelper.PropertyOf(() => default(Heap)!.Count);

    public static CallSiteDelegate Compile(MethodInfo method, bool isStatic) {
        var paramInfos = method.GetParameters();
        var returnType = method.ReturnType;
        int argSlots = paramInfos.Length + (isStatic ? 0 : 1);
        int hasRet = returnType != typeof(void) ? 1 : 0;

        var s = Expression.Parameter(typeof(VmState), "s");
        var stack = Expression.Property(s, StackProp);

        var baseOffV = Expression.Variable(typeof(int), "baseOff");

        var body = new List<Expression>
        {
            Expression.Assign(baseOffV,
            Expression.Subtract(Expression.Property(stack, SpProp),
                Expression.Constant(argSlots)))
        };

        int paramCount = paramInfos.Length + (isStatic ? 0 : 1);
        var rawArgs = new Expression[paramCount];
        int off = 0;

        if (!isStatic) {
            rawArgs[0] = ReadSpanInt(stack, baseOffV, off);
            off += 1;
        }

        for (int i = 0; i < paramInfos.Length; i++) {
            rawArgs[isStatic ? i : i + 1] = ReadSpanInt(stack, baseOffV, off);
            off += 1;
        }

        var typedArgs = new Expression[paramCount];
        if (!isStatic) {
            typedArgs[0] = ResolveArg(rawArgs[0], method.DeclaringType!, s);
        }

        for (int i = 0; i < paramInfos.Length; i++) {
            int idx = isStatic ? i : i + 1;
            typedArgs[idx] = ResolveArg(rawArgs[idx], paramInfos[i].ParameterType, s);
        }

        Expression callExpr = isStatic
            ? Expression.Call(method, typedArgs)
            : Expression.Call(typedArgs[0], method, typedArgs.Skip(1));

        if (returnType != typeof(void)) {
            var resultV = Expression.Variable(returnType, "result");
            body.Add(Expression.Assign(resultV, callExpr));

            if (argSlots > 0)
                body.Add(Expression.Call(stack, DropMethod, Expression.Constant(argSlots)));

            if (hasRet != 0)
                body.Add(Expression.Call(stack, PushIntMethod, ConvertToStackInt(resultV, returnType, s)));

            return Expression.Lambda<CallSiteDelegate>(
                Expression.Block([baseOffV, resultV], body), s).Compile();
        }

        body.Add(callExpr);
        if (argSlots > 0)
            body.Add(Expression.Call(stack, DropMethod, Expression.Constant(argSlots)));

        return Expression.Lambda<CallSiteDelegate>(
            Expression.Block([baseOffV], body), s).Compile();
    }

    public static CallSiteDelegate CompileConstructor(ConstructorInfo ctor) {
        var paramInfos = ctor.GetParameters();
        var returnType = ctor.DeclaringType ?? typeof(object);
        int argSlots = paramInfos.Length;

        var s = Expression.Parameter(typeof(VmState), "s");
        var stack = Expression.Property(s, StackProp);

        var baseOffV = Expression.Variable(typeof(int), "baseOff");

        var body = new List<Expression>();
        body.Add(Expression.Assign(baseOffV,
            Expression.Subtract(Expression.Property(stack, SpProp),
                Expression.Constant(argSlots))));

        var rawArgs = new Expression[paramInfos.Length];
        for (int i = 0; i < paramInfos.Length; i++) {
            rawArgs[i] = ReadSpanInt(stack, baseOffV, i);
        }

        var typedArgs = new Expression[paramInfos.Length];
        for (int i = 0; i < paramInfos.Length; i++) {
            typedArgs[i] = ResolveArg(rawArgs[i], paramInfos[i].ParameterType, s);
        }

        Expression callExpr = Expression.New(ctor, typedArgs);
        var resultV = Expression.Variable(returnType, "result");
        body.Add(Expression.Assign(resultV, callExpr));

        if (argSlots > 0)
            body.Add(Expression.Call(stack, DropMethod, Expression.Constant(argSlots)));

        body.Add(Expression.Call(stack, PushIntMethod, ConvertToStackInt(resultV, returnType, s)));

        return Expression.Lambda<CallSiteDelegate>(
            Expression.Block([baseOffV, resultV], body), s).Compile();
    }

    private static Expression ReadSpanInt(Expression stack, Expression baseOff, int slotOffset) {
        var rawSlots = Expression.Property(stack, RawSlotsProp);
        var idx = Expression.Add(baseOff, Expression.Constant(slotOffset));
        var val = Expression.ArrayIndex(rawSlots, idx);
        return Expression.Convert(val, typeof(int));
    }

    private static Expression ResolveArg(Expression rawInt, Type targetType, Expression s) {
        if (targetType == typeof(int)) return rawInt;
        if (targetType == typeof(long)) return Expression.Convert(rawInt, typeof(long));
        if (targetType == typeof(short)) return Expression.Convert(rawInt, typeof(short));
        if (targetType == typeof(byte)) return Expression.Convert(rawInt, typeof(byte));
        if (targetType == typeof(bool)) return Expression.NotEqual(rawInt, Expression.Constant(0));
        if (targetType == typeof(double)) return Expression.Convert(rawInt, typeof(double));
        if (targetType == typeof(float)) return Expression.Convert(rawInt, typeof(float));
        if (targetType == typeof(uint)) return Expression.Convert(rawInt, typeof(uint));
        if (targetType == typeof(ushort)) return Expression.Convert(rawInt, typeof(ushort));
        if (targetType == typeof(sbyte)) return Expression.Convert(rawInt, typeof(sbyte));

        var heap = Expression.Property(s, HeapProp);
        var count = Expression.Property(heap, CountProp);
        var inBounds = Expression.AndAlso(
            Expression.GreaterThanOrEqual(rawInt, Expression.Constant(0)),
            Expression.LessThan(rawInt, count));

        var get = Expression.Call(heap, UnsafeGetMethod, rawInt);
        return Expression.Condition(inBounds,
            Expression.Convert(get, targetType),
            targetType == typeof(object) ? Expression.Convert(rawInt, typeof(object)) : Expression.Default(targetType));
    }

    private static Expression ConvertToStackInt(Expression value, Type returnType, Expression s) {
        if (returnType == typeof(int)) return value;
        if (returnType == typeof(long)) return Expression.Convert(value, typeof(int));
        if (returnType == typeof(short)) return Expression.Convert(value, typeof(int));
        if (returnType == typeof(byte)) return Expression.Convert(value, typeof(int));
        if (returnType == typeof(sbyte)) return Expression.Convert(value, typeof(int));
        if (returnType == typeof(uint)) return Expression.Convert(value, typeof(int));
        if (returnType == typeof(ushort)) return Expression.Convert(value, typeof(int));
        if (returnType == typeof(bool)) return Expression.Condition(value, Expression.Constant(1), Expression.Constant(0));
        if (returnType == typeof(double)) return Expression.Convert(value, typeof(int));
        if (returnType == typeof(float)) return Expression.Convert(value, typeof(int));
        var heap = Expression.Property(s, HeapProp);
        return Expression.Call(heap, AllocateMethod, Expression.Convert(value, typeof(object)));
    }
}