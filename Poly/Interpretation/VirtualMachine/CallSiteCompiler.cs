using System.Linq.Expressions;
using System.Reflection;

namespace Poly.Interpretation.VirtualMachine;

internal delegate void CallSiteDelegate(VmState state);

internal static class CallSiteCompiler {
    public static CallSiteDelegate Compile(MethodInfo method, bool isStatic) {
        var paramInfos = method.GetParameters();
        var returnType = method.ReturnType;
        bool hasReturn = returnType != typeof(void);

        var s = Expression.Parameter(typeof(VmState), "s");
        var stack = Expression.Property(s, "Stack");
        var popInt = typeof(ValueStack).GetMethod("PopInt")!;
        var pushInt = typeof(ValueStack).GetMethod("Push", [typeof(int)])!;

        var argSlotsV = Expression.Variable(typeof(int), "argSlots");
        var hasRetV = Expression.Variable(typeof(int), "hasRet");
        var baseOffV = Expression.Variable(typeof(int), "baseOff");

        var body = new List<Expression>();

        body.Add(Expression.Assign(hasRetV, Expression.Call(stack, popInt)));
        body.Add(Expression.Assign(argSlotsV, Expression.Call(stack, popInt)));
        body.Add(Expression.Assign(baseOffV,
            Expression.Subtract(Expression.Property(stack, "SP"), argSlotsV)));

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

        if (hasReturn) {
            var resultV = Expression.Variable(returnType, "result");
            body.Add(Expression.Assign(resultV, callExpr));

            body.Add(Expression.IfThen(
                Expression.GreaterThan(argSlotsV, Expression.Constant(0)),
                Expression.Call(stack, "Drop", Type.EmptyTypes, argSlotsV)));

            body.Add(Expression.IfThen(
                Expression.NotEqual(hasRetV, Expression.Constant(0)),
                Expression.Call(stack, pushInt, ConvertToStackInt(resultV, returnType, s))));

            var vars = new[] { hasRetV, argSlotsV, baseOffV, resultV };
            return Expression.Lambda<CallSiteDelegate>(Expression.Block(vars, body), s).Compile();
        }

        body.Add(callExpr);
        body.Add(Expression.IfThen(
            Expression.GreaterThan(argSlotsV, Expression.Constant(0)),
            Expression.Call(stack, "Drop", Type.EmptyTypes, argSlotsV)));

        var svars = new[] { hasRetV, argSlotsV, baseOffV };
        return Expression.Lambda<CallSiteDelegate>(Expression.Block(svars, body), s).Compile();
    }

    public static CallSiteDelegate CompileConstructor(ConstructorInfo ctor) {
        var paramInfos = ctor.GetParameters();
        var returnType = ctor.DeclaringType ?? typeof(object);

        var s = Expression.Parameter(typeof(VmState), "s");
        var stack = Expression.Property(s, "Stack");
        var popInt = typeof(ValueStack).GetMethod("PopInt")!;
        var pushInt = typeof(ValueStack).GetMethod("Push", [typeof(int)])!;

        var argSlotsV = Expression.Variable(typeof(int), "argSlots");
        var hasRetV = Expression.Variable(typeof(int), "hasRet");
        var baseOffV = Expression.Variable(typeof(int), "baseOff");

        var body = new List<Expression>();

        body.Add(Expression.Assign(hasRetV, Expression.Call(stack, popInt)));
        body.Add(Expression.Assign(argSlotsV, Expression.Call(stack, popInt)));
        body.Add(Expression.Assign(baseOffV,
            Expression.Subtract(Expression.Property(stack, "SP"), argSlotsV)));

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

        body.Add(Expression.IfThen(
            Expression.GreaterThan(argSlotsV, Expression.Constant(0)),
            Expression.Call(stack, "Drop", Type.EmptyTypes, argSlotsV)));

        body.Add(Expression.IfThen(
            Expression.NotEqual(hasRetV, Expression.Constant(0)),
            Expression.Call(stack, pushInt, ConvertToStackInt(resultV, returnType, s))));

        var vars = new[] { hasRetV, argSlotsV, baseOffV, resultV };
        return Expression.Lambda<CallSiteDelegate>(Expression.Block(vars, body), s).Compile();
    }

    public static CallSiteDelegate CompileFieldGetter(FieldInfo field, bool isStatic) {
        var returnType = field.FieldType;

        var s = Expression.Parameter(typeof(VmState), "s");
        var stack = Expression.Property(s, "Stack");
        var popInt = typeof(ValueStack).GetMethod("PopInt")!;
        var pushInt = typeof(ValueStack).GetMethod("Push", [typeof(int)])!;

        var argSlotsV = Expression.Variable(typeof(int), "argSlots");
        var hasRetV = Expression.Variable(typeof(int), "hasRet");
        var baseOffV = Expression.Variable(typeof(int), "baseOff");

        var body = new List<Expression>();

        body.Add(Expression.Assign(hasRetV, Expression.Call(stack, popInt)));
        body.Add(Expression.Assign(argSlotsV, Expression.Call(stack, popInt)));
        body.Add(Expression.Assign(baseOffV,
            Expression.Subtract(Expression.Property(stack, "SP"), argSlotsV)));

        Expression resultExpr;
        if (isStatic) {
            resultExpr = Expression.Field(null, field);
        }
        else {
            var owner = ReadSpanInt(stack, baseOffV, 0);
            var resolvedOwner = ResolveArg(owner, field.DeclaringType!, s);
            resultExpr = Expression.Field(resolvedOwner, field);
        }

        var resultV = Expression.Variable(returnType, "result");
        body.Add(Expression.Assign(resultV, resultExpr));

        body.Add(Expression.IfThen(
            Expression.GreaterThan(argSlotsV, Expression.Constant(0)),
            Expression.Call(stack, "Drop", Type.EmptyTypes, argSlotsV)));

        body.Add(Expression.IfThen(
            Expression.NotEqual(hasRetV, Expression.Constant(0)),
            Expression.Call(stack, pushInt, ConvertToStackInt(resultV, returnType, s))));

        var vars = new[] { hasRetV, argSlotsV, baseOffV, resultV };
        return Expression.Lambda<CallSiteDelegate>(Expression.Block(vars, body), s).Compile();
    }

    private static Expression ReadSpanInt(Expression stack, Expression baseOff, int slotOffset) {
        var idx = Expression.Add(baseOff, Expression.Constant(slotOffset));
        return Expression.Call(stack, "ReadSlot", Type.EmptyTypes, idx);
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

        var heap = Expression.Property(s, "Heap");
        var count = Expression.Property(heap, "Count");
        var inBounds = Expression.AndAlso(
            Expression.GreaterThanOrEqual(rawInt, Expression.Constant(0)),
            Expression.LessThan(rawInt, count));

        var get = Expression.Call(heap, "Get", Type.EmptyTypes, rawInt);
        return Expression.Condition(inBounds,
            Expression.Convert(get, targetType),
            targetType == typeof(object) ? Expression.Convert(rawInt, typeof(object)) : Expression.Default(targetType));
    }

    public static CallSiteDelegate CompileFieldSetter(FieldInfo field, bool isStatic) {
        var s = Expression.Parameter(typeof(VmState), "s");
        var stack = Expression.Property(s, "Stack");
        var popInt = typeof(ValueStack).GetMethod("PopInt")!;

        var argSlotsV = Expression.Variable(typeof(int), "argSlots");
        var hasRetV = Expression.Variable(typeof(int), "hasRet");
        var baseOffV = Expression.Variable(typeof(int), "baseOff");

        var body = new List<Expression>();

        body.Add(Expression.Assign(hasRetV, Expression.Call(stack, popInt)));
        body.Add(Expression.Assign(argSlotsV, Expression.Call(stack, popInt)));
        body.Add(Expression.Assign(baseOffV,
            Expression.Subtract(Expression.Property(stack, "SP"), argSlotsV)));

        var setValueMethod = typeof(FieldInfo).GetMethod("SetValue", [typeof(object), typeof(object)])!;

        if (isStatic) {
            var rawValue = ReadSpanInt(stack, baseOffV, 0);
            var typedValue = ResolveArg(rawValue, field.FieldType, s);
            body.Add(Expression.Call(
                Expression.Constant(field),
                setValueMethod,
                Expression.Constant(null, typeof(object)),
                Expression.Convert(typedValue, typeof(object))));
        }
        else {
            var rawTarget = ReadSpanInt(stack, baseOffV, 0);
            var rawValue = ReadSpanInt(stack, baseOffV, 1);
            var typedTarget = ResolveArg(rawTarget, field.DeclaringType!, s);
            var typedValue = ResolveArg(rawValue, field.FieldType, s);
            body.Add(Expression.Call(
                Expression.Constant(field),
                setValueMethod,
                Expression.Convert(typedTarget, typeof(object)),
                Expression.Convert(typedValue, typeof(object))));
        }

        body.Add(Expression.IfThen(
            Expression.GreaterThan(argSlotsV, Expression.Constant(0)),
            Expression.Call(stack, "Drop", Type.EmptyTypes, argSlotsV)));

        var svars = new[] { hasRetV, argSlotsV, baseOffV };
        return Expression.Lambda<CallSiteDelegate>(Expression.Block(svars, body), s).Compile();
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
        var heap = Expression.Property(s, "Heap");
        return Expression.Call(heap, "Allocate", Type.EmptyTypes, Expression.Convert(value, typeof(object)));
    }
}