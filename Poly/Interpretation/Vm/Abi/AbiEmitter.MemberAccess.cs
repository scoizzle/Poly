using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

public static partial class AbiEmitter {
    /// <summary>
    /// How this VM stores a member: on the CLR object itself, or in the property bag a
    /// modeled <c>AstTypeDefinition</c> is. A storage fact, so it lives in the ABI; the
    /// declaration states what the member is (<see cref="ITypeMember.Kind"/>) and never
    /// how to read it.
    /// </summary>
    private enum MemberStorage { Clr, Bag }

    /// <summary>
    /// The one read entry point for a resolved member. The switch selects the storage
    /// policy from facts only: the member's <see cref="MemberKind"/> (what it is) and
    /// the declaring definition's storage (where this VM keeps it).
    /// </summary>
    internal static Expression? EmitMemberRead(ITypeMember member, Expression? instance) =>
        (member.Kind, StorageOf(member.DeclaringTypeDefinition)) switch {
            (MemberKind.Field, MemberStorage.Clr) => ClrFieldRead(member, instance),
            (MemberKind.Property or MemberKind.Indexer, MemberStorage.Clr) => ClrPropertyRead(member, instance),
            (MemberKind.Field, MemberStorage.Bag) => BagFieldRead(member, instance),
            (MemberKind.Property or MemberKind.Indexer, MemberStorage.Bag) => BagPropertyRead(member, instance),
            _ => null,
        };

    /// <summary>
    /// The one write entry point for a resolved member, on the same two facts as
    /// <see cref="EmitMemberRead"/>. Returns the instance so a write is an expression.
    /// </summary>
    internal static Expression? EmitMemberWrite(ITypeMember member, Expression? instance, Expression value) =>
        (member.Kind, StorageOf(member.DeclaringTypeDefinition)) switch {
            (MemberKind.Field, MemberStorage.Clr) => ClrFieldWrite(member, instance, value),
            (MemberKind.Property or MemberKind.Indexer, MemberStorage.Clr) => ClrPropertyWrite(member, instance, value),
            (MemberKind.Field or MemberKind.Property or MemberKind.Indexer, MemberStorage.Bag) =>
                BagWrite(member, instance, value),
            _ => null,
        };

    /// <summary>
    /// A modeled definition's instance is its property bag; every other platform's is the
    /// object itself. The platform decides: the bag's own CLR type is a type some model may
    /// also name, so the runtime type alone cannot.
    /// </summary>
    private static MemberStorage? StorageOf(ITypeDefinition declaring) =>
        declaring is AstTypeDefinition
            ? MemberStorage.Bag
            : declaring.GetRuntimeType() is null ? null : MemberStorage.Clr;

    private static Expression? ClrFieldRead(ITypeMember member, Expression? instance) {
        if (member is not ClrTypeField { FieldInfo: var info }) return null;
        if (!info.IsStatic && instance is null) return null;
        var typedInst = info.IsStatic ? null : Convert(instance!, info.DeclaringType!);
        var access = Field(typedInst, info);
        return info.FieldType.IsValueType ? Convert(access, typeof(object)) : access;
    }

    private static Expression? ClrFieldWrite(ITypeMember member, Expression? instance, Expression value) {
        if (member is not ClrTypeField { FieldInfo: var info }) return null;
        if (info.IsInitOnly || info.IsLiteral) return null;
        if (!info.IsStatic && instance is null) return null;
        var typedInst = info.IsStatic ? null : Convert(instance!, info.DeclaringType!);
        var assign = Assign(Field(typedInst, info), Convert(value, info.FieldType));
        return info.IsStatic ? assign : Block(assign, instance!);
    }

    private static Expression? ClrPropertyRead(ITypeMember member, Expression? instance) {
        if (member is not ClrTypeProperty { PropertyInfo: var info }) return null;
        if (!member.IsStatic && instance is null) return null;
        var typedInst = member.IsStatic ? null : Convert(instance!, info.DeclaringType!);
        var access = Property(typedInst, info);
        return info.PropertyType.IsValueType ? Convert(access, typeof(object)) : access;
    }

    private static Expression? ClrPropertyWrite(ITypeMember member, Expression? instance, Expression value) {
        if (member is not ClrTypeProperty { PropertyInfo: var info }) return null;
        if (info.GetSetMethod(nonPublic: true) is null) return null;
        if (!member.IsStatic && instance is null) return null;
        var typedInst = member.IsStatic ? null : Convert(instance!, info.DeclaringType!);
        var assign = Assign(Property(typedInst, info), Convert(value, info.PropertyType));
        return member.IsStatic ? assign : Block(assign, instance!);
    }

    /// <summary>A modeled field: a declared const is its value; otherwise a keyed bag read.</summary>
    private static Expression? BagFieldRead(ITypeMember member, Expression? instance) {
        if (member is not AstFieldDefinition field) return null;
        var definition = field.Definition;
        if ((definition.IsStatic || definition.IsConst)
            && definition.DefaultValue is Poly.Ast.Nodes.Constant constant)
            return Constant(constant.Value, typeof(object));
        if (instance is null) return null;
        return BagKeyRead(
            instance, field.Name, field.MemberTypeDefinition, definition.DefaultValue,
            definition.FieldType is OptionalTypeReference);
    }

    /// <summary>
    /// A modeled property: a keyed bag read. An answered property key wins; a getter
    /// whose body is <c>this.&lt;field&gt;</c> reads that field when the key is absent.
    /// A store-backed dictionary synthesizes the property name and exposes the field
    /// name as a non-enumerable write view.
    /// </summary>
    private static Expression? BagPropertyRead(ITypeMember member, Expression? instance) {
        if (member is not AstPropertyDefinition property || instance is null) return null;
        var stored = BagKeyRead(
            instance, property.Name, property.MemberTypeDefinition, property.Definition.DefaultValue);
        if (GetterBackingField(property) is not { } getter
            || EmitMemberRead(getter.Field, instance) is not Expression fieldRead)
            return stored;
        // A copy getter (#556, #541) hands out a snapshot of its field. The bag read must answer
        // the same way, or the simulator would iterate the live list while printed C# iterates a copy.
        if (getter.Copies)
            fieldRead = Call(null, CopyCollectionInfo, Constant((int)CollectionKind.List), fieldRead);
        var hasProperty = Call(
            Convert(instance, typeof(IDictionary<string, object?>)),
            DictionaryBackedValue.DictContainsKey,
            Constant(property.Name));
        return Condition(hasProperty, stored, fieldRead);
    }

    /// <summary>The field a getter reads, and whether the getter hands out a copy of it.</summary>
    private readonly record struct GetterBacking(ITypeField Field, bool Copies);

    /// <summary>
    /// The field a <c>get =&gt; this.field</c> body reads, when that field exists, and the same for the
    /// copy getter <c>get =&gt; new List&lt;T&gt;(this.field)</c> a collection navigation prints (#556, #541):
    /// the bag read answers from the backing field, copying it for a copy getter.
    /// </summary>
    private static GetterBacking? GetterBackingField(AstPropertyDefinition property) {
        var (name, copies) = property.Definition.Getter?.Body switch {
            Member { Value: ThisReference, MemberName: var fieldName } => (fieldName, false),
            New { Arguments: [Member { Value: ThisReference, MemberName: var copiedName }] } => (copiedName, true),
            _ => ((string?)null, false),
        };
        if (name is null)
            return null;
        foreach (var field in property.DeclaringTypeDefinition.Fields) {
            if (field is AstFieldDefinition
                && string.Equals(field.Name, name, StringComparison.Ordinal))
                return new GetterBacking(field, copies);
        }
        return null;
    }

    /// <summary>
    /// A keyed read: the stored value coerced to the member's target type, else the
    /// member's <c>DefaultValue</c> when the key is not present. A value-type member
    /// with no default reads as that type's zero value; a reference member with no
    /// default reads as <see cref="System.Reflection.Missing.Value"/>.
    /// </summary>
    private static Expression BagKeyRead(
        Expression instance, string key, ITypeDefinition target, Node? defaultValue, bool optional = false) {
        var typed = Convert(instance, typeof(IDictionary<string, object?>));
        var rawValue = Call(typed, DictionaryBackedValue.DictGetItem, Constant(key));
        var fallback = defaultValue is Poly.Ast.Nodes.Constant constant
            ? Convert(Constant(constant.Value), typeof(object))
            // An unset T? is null. Missing is not null, so `== null` would skip init.
            : optional
                ? Constant(null, typeof(object))
                : DictionaryBackedValue.MissingKeyFallback(target);
        var value = DictionaryBackedValue.CoerceRead(rawValue, target);
        return Condition(
            Call(typed, DictionaryBackedValue.DictContainsKey, Constant(key)),
            value,
            fallback);
    }

    /// <summary>
    /// A modeled write. Only a declared field or property has one: a collection's
    /// synthetic <c>Item</c> member states no storage, so it stays unwritable.
    /// </summary>
    private static Expression? BagWrite(ITypeMember member, Expression? instance, Expression value) {
        if (instance is null || member is not (AstFieldDefinition or AstPropertyDefinition)) return null;
        var typed = Convert(instance, typeof(IDictionary<string, object?>));
        return Block(
            Call(typed, DictionaryBackedValue.DictSetItem, Constant(member.Name), value),
            instance);
    }
}