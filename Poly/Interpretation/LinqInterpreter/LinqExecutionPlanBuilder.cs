using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.LinqInterpreter {
    public sealed class LinqExecutionPlanBuilder : IExecutionPlanBuilder<Expression, Expression, ParameterExpression> {
        private readonly Dictionary<string, ParameterExpression> _parameters = new(StringComparer.Ordinal);

        public ITypeDefinition GetTypeDefinition(string typeName)
        {
            var resolved = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeName)
                ?? throw new TypeLoadException($"Unable to resolve CLR type '{typeName}'");
            return resolved;
        }

        // Expressions
        public Expression Constant<TValue>(TValue value) => Expression.Constant(value, value?.GetType() ?? typeof(object));
        public Expression Constant(object value) => Expression.Constant(value, value?.GetType() ?? typeof(object));
        public Expression Null() => Expression.Constant(null);
        public Expression Default(ITypeDefinition type)
        {
            var clr = type as ClrTypeDefinition;
            var sysType = clr?.Type ?? typeof(void);
            return Expression.Default(sysType);
        }
        public ParameterExpression Parameter(string name, ITypeDefinition type)
        {
            if (_parameters.TryGetValue(name, out var existing))
                return existing;

            var clr = type as ClrTypeDefinition;
            var sysType = clr?.Type ?? typeof(object);
            var parameter = Expression.Parameter(sysType, name);
            _parameters[name] = parameter;
            return parameter;
        }

        public ParameterExpression GetParameter(string name)
            => _parameters.TryGetValue(name, out var parameter)
                ? parameter
                : throw new KeyNotFoundException($"Parameter '{name}' has not been declared.");

        public Expression ParameterRef(string name) => ParameterRef(GetParameter(name));

        public Expression ParameterRef(ParameterExpression parameter) => parameter;
        public Expression Variable(string name, ITypeDefinition type)
        {
            var sysType = type is ClrTypeDefinition clr ? clr.Type : typeof(object);
            return Expression.Parameter(sysType, name);
        }

        public Expression VariableRef(string name) => GetParameter(name);

        // Arithmetic
        public Expression Add(Expression left, Expression right) => Expression.Add(left, right);
        public Expression Subtract(Expression left, Expression right) => Expression.Subtract(left, right);
        public Expression Multiply(Expression left, Expression right) => Expression.Multiply(left, right);
        public Expression Divide(Expression left, Expression right) => Expression.Divide(left, right);
        public Expression Modulus(Expression left, Expression right) => Expression.Modulo(left, right);
        public Expression Power(Expression left, Expression right)
        {
            var pow = typeof(Math).GetMethod(nameof(Math.Pow), [typeof(double), typeof(double)])!;
            var l = Expression.Convert(left, typeof(double));
            var r = Expression.Convert(right, typeof(double));
            return Expression.Call(pow, l, r);
        }

        // Comparison
        public Expression Equal(Expression left, Expression right) => Expression.Equal(left, right);
        public Expression NotEqual(Expression left, Expression right) => Expression.NotEqual(left, right);
        public Expression LessThan(Expression left, Expression right) => Expression.LessThan(left, right);
        public Expression LessThanOrEqual(Expression left, Expression right) => Expression.LessThanOrEqual(left, right);
        public Expression GreaterThan(Expression left, Expression right) => Expression.GreaterThan(left, right);
        public Expression GreaterThanOrEqual(Expression left, Expression right) => Expression.GreaterThanOrEqual(left, right);
        public Expression LogicalAnd(Expression left, Expression right) => Expression.AndAlso(left, right);
        public Expression LogicalOr(Expression left, Expression right) => Expression.OrElse(left, right);
        public Expression LogicalNot(Expression operand) => Expression.Not(operand);
        public Expression Negate(Expression operand) => Expression.Negate(operand);

        // Casting and checks
        public Expression TypeCast(Expression value, ITypeDefinition type, bool checkedCast = false)
        {
            var clr = type as ClrTypeDefinition;
            var sysType = clr?.Type ?? typeof(object);
            return checkedCast ? Expression.ConvertChecked(value, sysType) : Expression.Convert(value, sysType);
        }
        public Expression TypeIs(Expression value, ITypeDefinition type)
        {
            var clr = type as ClrTypeDefinition;
            var sysType = clr?.Type ?? typeof(object);
            return Expression.TypeIs(value, sysType);
        }

        // Members and indexing
        public Expression MemberGet(Expression instance, ITypeMember member)
        {
            switch (member) {
                case ClrTypeProperty p:
                    return Expression.Property(instance, p.PropertyInfo);
                case ClrTypeField f:
                    return Expression.Field(instance, f.FieldInfo);
                case ClrMethod m:
                    return Expression.Call(instance, m.MethodInfo, Array.Empty<Expression>());
                default:
                    throw new NotSupportedException("Unsupported member type for MemberGet");
            }
        }
        public Expression MemberGet(Expression instance, string memberName)
            => Expression.PropertyOrField(instance, memberName);
        public Expression IndexGet(Expression instance, IEnumerable<Expression> indices)
            => Expression.MakeIndex(instance, instance.Type.GetDefaultMembers().OfType<System.Reflection.PropertyInfo>().FirstOrDefault()!, indices.ToArray());

        // Invocation and lambdas
        public Expression Call(Expression target, ITypeMethod method, IEnumerable<Expression> arguments)
            => Expression.Call(target, (method as ClrMethod)!.MethodInfo, arguments);
        public Expression Call(Expression target, string methodName, IEnumerable<Expression> arguments)
        {
            var mi = target.Type.GetMethods().FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == arguments.Count());
            if (mi == null) throw new MissingMethodException(target.Type.FullName, methodName);
            return Expression.Call(target, mi, arguments);
        }
        public Expression Invoke(Expression callable, IEnumerable<Expression> arguments)
            => Expression.Invoke(callable, arguments);
        public Expression Lambda(IEnumerable<(string name, ITypeDefinition type)> parameters, Expression body)
        {
            var parms = parameters.Select(p => Expression.Parameter((p.type as ClrTypeDefinition)?.Type ?? typeof(object), p.name));
            return Expression.Lambda(body, parms);
        }

        // Construction
        public Expression NewObject(ITypeDefinition type, IEnumerable<(ITypeMember member, Expression value)> initializers)
        {
            var clr = (type as ClrTypeDefinition)!;
            var newExpr = Expression.New(clr.Type);
            if (initializers != null && initializers.Any()) {
                var bindings = initializers.Select(init => {
                    switch (init.member) {
                        case ClrTypeProperty p:
                            return Expression.Bind(p.PropertyInfo, init.value);
                        case ClrTypeField f:
                            return Expression.Bind(f.FieldInfo, init.value);
                        default:
                            throw new NotSupportedException("Unsupported initializer member type");
                    }
                }).Cast<MemberBinding>().ToArray();
                return Expression.MemberInit(newExpr, bindings);
            }
            return newExpr;
        }
        public Expression NewArray(ITypeDefinition elementType, IEnumerable<Expression> elements)
        {
            var clr = (elementType as ClrTypeDefinition)!;
            return Expression.NewArrayInit(clr.Type, elements);
        }
        public Expression Coalesce(Expression left, Expression right) => Expression.Coalesce(left, right);

        // Statements
        public Expression NoOp() => Expression.Empty();
        public Expression Block(IEnumerable<Expression> statements) => Expression.Block(statements);
        public Expression ExprBlock(IEnumerable<Expression> expressions) => Expression.Block(expressions);

        public Expression DeclareVariable(string name, ITypeDefinition type, Expression? initialValue = default)
        {
            var variable = Variable(name, type);
            if (initialValue == null) return variable;
            return Expression.Block([(ParameterExpression)variable], Expression.Assign(variable, initialValue));
        }
        public Expression Assign(Expression destination, Expression value) => Expression.Assign(destination, value);
        public Expression AssignExpr(Expression destination, Expression value) => Expression.Assign(destination, value);
        public Expression AssignVariable(string name, Expression value)
        {
            var variable = Expression.Parameter(value.Type, name);
            return Expression.Assign(variable, value);
        }
        public Expression AssignMember(Expression instance, ITypeMember member, Expression value)
        {
            switch (member) {
                case ClrTypeProperty p:
                    return Expression.Assign(Expression.Property(instance, p.PropertyInfo), value);
                case ClrTypeField f:
                    return Expression.Assign(Expression.Field(instance, f.FieldInfo), value);
                default:
                    throw new NotSupportedException("Unsupported member type for AssignMember");
            }
        }
        public Expression AssignIndex(Expression instance, IEnumerable<Expression> indices, Expression value)
        {
            var indexer = instance.Type.GetDefaultMembers().OfType<System.Reflection.PropertyInfo>().FirstOrDefault();
            if (indexer == null) throw new NotSupportedException("No indexer found on type");
            return Expression.Assign(Expression.MakeIndex(instance, indexer, indices.ToArray()), value);
        }

        // Control flow
        public Expression If(Expression condition, Expression thenBranch, Expression? elseBranch = default)
            => Expression.Condition(condition, thenBranch, elseBranch ?? Expression.Empty());
        public Expression Switch(Expression selector, IEnumerable<(Expression caseValue, Expression body)> cases, Expression? defaultBody = default)
        {
            var switchCases = cases.Select(c => Expression.SwitchCase(c.body, c.caseValue));
            return Expression.Switch(selector, defaultBody ?? Expression.Empty(), null, switchCases);
        }
        public Expression While(Expression condition, Expression body)
        {
            var breakLabel = Expression.Label("break");
            var loop = Expression.Loop(
                Expression.IfThenElse(condition, body, Expression.Break(breakLabel)),
                breakLabel);
            return loop;
        }
        public Expression For(Expression init, Expression condition, Expression increment, Expression body)
        {
            var breakLabel = Expression.Label("break");
            var loop = Expression.Block(
                init,
                Expression.Loop(
                    Expression.Block(
                        Expression.IfThen(Expression.Not(condition), Expression.Break(breakLabel)),
                        body,
                        increment
                    ),
                    breakLabel
                ));
            return loop;
        }
        public Expression ForEach(Expression enumerable, (string name, ITypeDefinition type) item, Expression body)
        {
            var enumeratorVar = Expression.Parameter(typeof(System.Collections.IEnumerator), "enumerator");
            var getEnumerator = Expression.Call(enumerable, typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!);
            var moveNext = Expression.Call(enumeratorVar, typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!);
            var current = Expression.Property(enumeratorVar, "Current");
            var itemVar = Expression.Parameter((item.type as ClrTypeDefinition)?.Type ?? typeof(object), item.name);
            var assignItem = Expression.Assign(itemVar, Expression.Convert(current, itemVar.Type));
            var loop = Expression.Block([enumeratorVar, itemVar],
                Expression.Assign(enumeratorVar, getEnumerator),
                Expression.Loop(
                    Expression.IfThenElse(
                        Expression.Not(moveNext),
                        Expression.Break(Expression.Label("break")),
                        Expression.Block(assignItem, body)
                    )
                ));
            return loop;
        }

        public Expression TryCatchFinally(Expression tryBody, IEnumerable<(ITypeDefinition exceptionType, Expression catchBody)> catches, Expression? finallyBody = default)
        {
            var catchBlocks = catches.Select(c => Expression.Catch((c.exceptionType as ClrTypeDefinition)!.Type, c.catchBody)).ToArray();
            var handler = Expression.TryCatch(tryBody, catchBlocks);
            return finallyBody is null ? handler : Expression.TryFinally(handler, finallyBody);
        }

        public Expression Return(Expression value)
        {
            var returnLabel = Expression.Label(value.Type, "return");
            return Expression.Block(Expression.Label(returnLabel, value));
        }
        public Expression Break() => Expression.Break(Expression.Label("break"));
        public Expression Continue() => Expression.Continue(Expression.Label("continue"));
        public Expression Yield(Expression value) => Expression.Block(value);

        public Expression UsingScope(string? name, IEnumerable<Expression> statements) => Expression.Block(statements);
        public Expression UsingVariables(IEnumerable<(string name, ITypeDefinition type, Expression? initialValue)> declarations, Expression body)
        {
            var variables = declarations.Select(d => Expression.Parameter((d.type as ClrTypeDefinition)?.Type ?? typeof(object), d.name)).ToArray();
            var assigns = declarations.Where(d => d.initialValue != null).Select(d => Expression.Assign(variables.First(v => v.Name == d.name), d.initialValue!));
            return Expression.Block(variables, assigns.Concat([body]));
        }

        public Expression AnnotateExpr(Expression expr, string key, object value) => expr;
        public Expression AnnotateStmt(Expression stmt, string key, object value) => stmt;
        public Expression IntrinsicExpr(string name, IEnumerable<Expression> args)
        {
            var mi = typeof(Math).GetMethod(name, args.Select(a => a.Type).ToArray());
            if (mi == null) throw new NotSupportedException($"No intrinsic '{name}' found");
            return Expression.Call(mi!, args);
        }
        public Expression IntrinsicStmt(string name, IEnumerable<Expression> args) => IntrinsicExpr(name, args);

        public Expression Ternary(Expression condition, Expression thenExpr, Expression elseExpr)
            => Expression.Condition(condition, thenExpr, elseExpr);
    }
}