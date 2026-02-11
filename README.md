# Poly - Fluent Domain Modeling & Code Generation for .NET

Poly provides a comprehensive, strongly-typed framework for domain modeling, abstract syntax tree (AST) analysis, semantic validation, and LINQ expression code generation. Build complex data models with fluent APIs, analyze and transform code at runtime, and generate optimized executable expressions.

> **Note:** This project is under active development. APIs may evolve as we expand functionality.

## Quick Start

```csharp
using Poly.DataModeling;
using Poly.DataModeling.Builders;
using Poly.Validation;

// Define domain models with fluent API
var model = new DataModelBuilder();

model.AddDataType("Customer", type => {
    type.AddProperty("Id", p => p.OfType<Guid>())
        .AddProperty("Email", p => p
            .OfType<string>()
            .WithConstraint(new NotNullConstraint())
            .WithConstraint(new LengthConstraint(5, 255)))
        .AddProperty("Orders", p => p
            .OfType("Order")  // Reference to another type
            .AsList())        // Collection type
        .HasMany("Order", "Orders")
            .WithOne("Order", "Customer");
});

var dataModel = model.Build();

// Convert to AST for analysis and code generation
var astNodes = dataModel.ToAst();

// Analyze and compile expressions
var analyzer = new AnalyzerBuilder()
    .UseTypeResolver()
    .UseMemberResolver()
    .UseVariableScopeValidator()
    .UseDataModelTransforms()
    .Build();

var result = analyzer.Analyze(astNodes);

// Generate LINQ expressions
var generator = new LinqExpressionGenerator(result);
var compiledLambda = generator.CompileAsLambda(someAstNode, parameter);
```

## Features

### 🔧 Fluent Domain Modeling

Define complex data models with a strongly-typed, fluent API:

```csharp
model.AddDataType("Order", type => {
    type.AddProperty("Id", p => p.OfType<int>())
        .AddProperty("Items", p => p
            .OfType("Product")  // Reference type
            .AsList()           // Collection
            .Optional())        // Nullable
        .AddProperty("Metadata", p => p
            .OfTypeExpression(new MapType(  // Complex type expressions
                new PrimitiveType(PrimitiveTypeId.String),
                new PrimitiveType(PrimitiveTypeId.Json))))
        .HasMutation("AddItem", preconditions => preconditions
            .WithCondition(new PropertyValue("Status"), new EqualityConstraint("Active")),
            effects => effects
                .WithEffect(new PropertyEffect("Items", new AppendEffect(new ParameterValue("item")))));
});
```

### 🎯 Advanced Type System

**Primitive Types:**
- All .NET primitives: `bool`, `int8`/`int16`/`int32`/`int64`, `uint8`/`uint16`/`uint32`/`uint64`
- `float32`/`float64`, `decimal`, `string`, `char`
- Temporal: `DateTime`, `DateOnly`, `TimeOnly`, `TimeSpan`
- Special: `Guid`, `byte[]`, `object` (JSON)

**Composite Types:**
- **Optional**: `Type?` - Nullable types
- **Collections**: `Type[]`, `List<Type>`, `Set<Type>` - Arrays, lists, and sets
- **Maps**: `Dictionary<TKey, TValue>` - Key-value mappings
- **References**: References to other model types
- **Tuples**: Fixed-size heterogeneous collections
- **Unions**: Type-safe discriminated unions
- **Enums**: Named value sets

### 🔗 Relationship Modeling

Define complex relationships with full cardinality support:

```csharp
// One-to-One
type.HasOne("Profile", "Profile")
    .WithOne("User", "User");

// One-to-Many
type.HasMany("Order", "Orders")
    .WithOne("Customer", "Customer");

// Many-to-Many
type.HasMany("Product", "Products")
    .WithMany("Orders", "OrderItems");

// Inheritance
type.HasBase("BaseEntity");

// Association
type.HasAssociation("AuditLog", "Logs");
```

### ✅ Comprehensive Validation

**Property Constraints:**
- `NotNullConstraint()` - Required fields
- `LengthConstraint(min, max)` - String/collection length
- `RangeConstraint(min, max)` - Numeric bounds
- `EqualityConstraint(value)` - Exact value matching
- `ValueSourceComparisonConstraint` - Cross-property comparisons

**Type Rules:**
- `ConditionalRule` - Conditional validation logic
- `MutualExclusionRule` - XOR relationships
- `PropertyDependencyRule` - Required combinations
- `ComparisonRule` - Property comparisons
- `ComputedValueRule` - Derived calculations

**Mutation Preconditions:**
- Validate state before allowing changes
- Cross-property validation
- Business rule enforcement

### 🚀 AST Analysis & Code Generation

Transform models into executable code through multi-phase analysis:

```csharp
// Phase 1: Semantic Analysis
var analyzer = new AnalyzerBuilder()
    .UseTypeResolver()              // Infer types
    .UseMemberResolver()            // Resolve properties/methods
    .UseVariableScopeValidator()    // Validate scoping
    .UseControlFlowAnalysis()       // Analyze control flow
    .UseConstantFolding()           // Optimize constants
    .Build();

// Phase 2: Code Generation
var generator = new LinqExpressionGenerator(analysisResult)
    .RegisterCompiler(new DataModelPropertyAccessorCompiler());

Expression compiledExpr = generator.Compile(astNode);
```

### 🔄 Runtime Mutations

Define and execute complex state changes with preconditions and effects:

```csharp
type.HasMutation("ProcessOrder", 
    preconditions => preconditions
        .WithCondition(new PropertyValue("Status"), new EqualityConstraint("Pending")),
    effects => effects
        .WithEffect(new PropertyEffect("Status", new ConstantValue("Processing")))
        .WithEffect(new PropertyEffect("ProcessedAt", new CurrentTimeEffect())));
```

### 📊 JSON Serialization

Full polymorphic serialization with clean, portable JSON:

```json
{
  "Types": [
    {
      "Name": "Customer",
      "Properties": [
        {
          "Name": "Email",
          "Type": { "$type": "Primitive", "Id": "String" },
          "Constraints": [
            { "Type": "NotNull" },
            { "Type": "Length", "MinLength": 5, "MaxLength": 255 }
          ]
        },
        {
          "Name": "Orders",
          "Type": {
            "$type": "Collection",
            "Element": { "$type": "Reference", "TypeName": "Order" },
            "Kind": "List"
          }
        }
      ],
      "Mutations": [
        {
          "Name": "UpdateEmail",
          "Parameters": [{ "Name": "newEmail", "Type": "String" }],
          "Preconditions": [
            {
              "ValueSource": { "$type": "Property", "PropertyName": "IsActive" },
              "Constraint": { "Type": "Equality", "Value": true }
            }
          ]
        }
      ]
    }
  ]
}
```

## Examples & Benchmarks

### Running Examples

Explore the fluent API with complete examples:

```bash
# Run the main benchmark suite
cd Poly.Benchmarks
dotnet run

# View available examples
ls *.cs
# FluentApiExample.cs - Basic fluent API usage
# FluentBuilderExample.cs - Complete order management system
# FunctionCalling.cs - Advanced expression compilation
```

### Benchmark Results

The framework includes comprehensive benchmarks for performance validation:

```bash
# Run benchmarks
cd Poly.Benchmarks
dotnet run -- --filter "*"

# Key benchmark categories:
# - DataModel construction and serialization
# - AST analysis and transformation
# - LINQ expression compilation
# - Type resolution performance
# - Validation rule evaluation
```

### Example: Complete Order System

```csharp
var model = new DataModelBuilder();

model.AddDataType("Customer", customer => {
    customer.AddProperty("Id", p => p.OfType<Guid>())
            .AddProperty("Email", p => p.OfType<string>().WithConstraint(new NotNullConstraint()))
            .AddProperty("Name", p => p.OfType<string>().WithConstraint(new NotNullConstraint()))
            .HasMany("Order", "Orders").WithOne("Customer", "Customer");
});

model.AddDataType("Order", order => {
    order.AddProperty("Id", p => p.OfType<int>())
         .AddProperty("Total", p => p.OfType<decimal>())
         .AddProperty("Items", p => p.OfType("OrderItem").AsList())
         .HasMutation("AddItem", mutation => mutation
             .AddParameter("item", new ReferenceType("OrderItem"))
             .WithPrecondition(pre => pre.WithCondition(
                 new PropertyValue("Status"), 
                 new EqualityConstraint("Active")))
             .HasEffect(effect => effect.WithEffect(
                 new PropertyEffect("Items", new AppendEffect(new ParameterValue("item"))))));
});

var dataModel = model.Build();
```

## Architecture

### Core Systems

**DataModeling** → Fluent API for defining domain models with types, properties, relationships, and mutations  
**Interpretation** → AST analysis, semantic validation, and LINQ expression code generation  
**Introspection** → Type system abstraction and reflection bridge  
**Validation** → Constraint and rule evaluation engine

### DataModeling Pipeline

```
DataModelBuilder → DataTypeBuilder → PropertyBuilder → TypeExpression
                      ↓
                RelationshipBuilder → Relationship
                      ↓
                MutationBuilder → Mutation
                      ↓
DataModel (Types + Relationships + Mutations)
```

### Interpretation Pipeline

```
AST Construction → [Analysis Phase] → AnalysisResult → [Generation Phase] → Compiled Delegate
                      ↓
            TypeResolver → MemberResolver → ScopeValidator → ControlFlowAnalysis
                      ↓
            LinqExpressionGenerator → Expression<T> → Compile()
```

### Key Components

- **TypeExpression**: Composable type system (primitives, collections, maps, references, unions)
- **AST Nodes**: Abstract syntax tree for code representation and transformation
- **Analysis Passes**: Semantic validation, type inference, member resolution, control flow
- **Code Generation**: LINQ Expression tree compilation for optimal runtime performance
- **Introspection Bridge**: Seamless integration between static models and dynamic execution

### JSON Serialization

Models serialize to clean, portable JSON with full type information:

```json
{
  "Types": [
    {
      "Name": "Customer",
      "Properties": [
        {
          "Name": "Email",
          "Type": { "$type": "Primitive", "Id": "String" },
          "Constraints": [
            { "Type": "NotNull" },
            { "Type": "Length", "MinLength": 5, "MaxLength": 255 }
          ]
        },
        {
          "Name": "Orders",
          "Type": {
            "$type": "Collection",
            "Element": { "$type": "Reference", "TypeName": "Order" },
            "Kind": "List"
          }
        }
      ],
      "Mutations": [
        {
          "Name": "UpdateEmail",
          "Parameters": [{ "Name": "newEmail", "Type": "String" }],
          "Preconditions": [
            {
              "ValueSource": { "$type": "Property", "PropertyName": "IsActive" },
              "Constraint": { "Type": "Equality", "Value": true }
            }
          ]
        }
      ]
    }
  ],
  "Relationships": [
    {
      "Name": "Customer.Orders_Order.Customer",
      "Source": { "TypeName": "Customer", "PropertyName": "Orders" },
      "Target": { "TypeName": "Order", "PropertyName": "Customer" }
    }
  ]
}
```

## Roadmap

### ✅ Completed Features

- ✅ Fluent type and property builders with complex type expressions
- ✅ Relationship definitions with full cardinality support (1:1, 1:N, N:M, inheritance, associations)
- ✅ Comprehensive property and type-level validation rules
- ✅ Advanced type system (primitives, collections, maps, unions, tuples, enums)
- ✅ Runtime mutation system with preconditions and effects
- ✅ AST-based interpretation system with semantic analysis
- ✅ LINQ Expression code generation and compilation
- ✅ Control flow analysis and optimization passes
- ✅ Introspection bridge for dynamic execution
- ✅ Polymorphic JSON serialization
- ✅ Benchmark suite and performance testing

### 🚧 In Development

- 🚧 Runtime validation engine integration
- 🚧 SQL schema generation from data models
- 🚧 Migration diff engine for schema evolution
- 🚧 Query/filter DSL for data access
- 🚧 API code generation (Minimal APIs + OpenAPI)
- 🚧 Authorization model integration
- 🚧 GraphQL schema generation
- 🚧 Code generation for multiple target languages

## API Reference

### DataModeling API

#### DataModelBuilder

```csharp
DataModelBuilder AddDataType(string name, Action<DataTypeBuilder> configure)
DataModelBuilder AddDataType(DataType dataType)
DataModelBuilder AddRelationship(Relationship relationship)
DataModel Build()
IReadOnlyList<TypeDefinitionNode> ToAst()  // Convert to AST
```

#### DataTypeBuilder

```csharp
DataTypeBuilder AddProperty(string name, Action<PropertyBuilder> configure)
DataTypeBuilder AddRule(Rule rule)
RelationshipBuilder HasOne(string targetType, string? propertyName = null)
RelationshipBuilder HasMany(string targetType, string? propertyName = null)
RelationshipBuilder HasBase(string baseType)  // Inheritance
RelationshipBuilder HasAssociation(string targetType, string? propertyName = null)
DataTypeBuilder HasMutation(string name, Action<MutationBuilder> configure)
DataType Build()
```

#### PropertyBuilder

```csharp
PropertyBuilder OfType<T>()
PropertyBuilder OfType(Type type)
PropertyBuilder OfType(string typeName)  // Reference to model type
PropertyBuilder OfTypeExpression(TypeExpression typeExpr)
PropertyBuilder Optional()  // Make nullable
PropertyBuilder AsList() / AsArray() / AsSet()  // Collections
PropertyBuilder WithConstraint(Constraint constraint)
PropertyBuilder WithDefault(object? value)
DataProperty Build()
```

### Interpretation API

#### AnalyzerBuilder

```csharp
AnalyzerBuilder AddTypeDefinitionProvider(ITypeDefinitionProvider provider)
AnalyzerBuilder UseTypeResolver()
AnalyzerBuilder UseMemberResolver()
AnalyzerBuilder UseVariableScopeValidator()
AnalyzerBuilder UseControlFlowAnalysis()
AnalyzerBuilder UseConstantFolding()
AnalyzerBuilder UseDataModelTransforms()
Analyzer Build()
```

#### LinqExpressionGenerator

```csharp
LinqExpressionGenerator RegisterCompiler(INodeCompiler compiler)
Expression Compile(Node node)
LambdaExpression CompileAsLambda(Node node, Parameter parameter)
LambdaExpression CompileAsLambda(Node node, params Parameter[] parameters)
```

#### Analysis Extensions

```csharp
// Get analysis results
ITypeDefinition? GetResolvedType(this AnalysisResult result, Node node)
ITypeMember? GetResolvedMember(this AnalysisResult result, Node node)
DataModelPropertyAccessor? GetDataModelReplacement(this AnalysisResult result, Node node)
```

### Type Expressions

```csharp
// Primitives
new PrimitiveType(PrimitiveTypeId.String)

// Composites
new OptionalType(innerType)
new CollectionType(elementType, CollectionKind.List)
new MapType(keyType, valueType)
new ReferenceType("TypeName")
new UnionType([case1, case2, ...])
new TupleType([type1, type2, ...])
new EnumType("EnumName", [value1, value2, ...])
```

### Validation API

#### Constraints

```csharp
new NotNullConstraint()
new LengthConstraint(min, max)
new RangeConstraint<T>(min, max)
new EqualityConstraint(expectedValue)
new ValueSourceComparisonConstraint(leftSource, rightSource, ComparisonOperator.Equal)
```

#### Rules

```csharp
new ConditionalRule(condition, consequence)
new MutualExclusionRule(propertyNames)
new PropertyDependencyRule(dependentProp, requiredProp)
new ComparisonRule(leftProp, rightProp, ComparisonOperator.GreaterThan)
new ComputedValueRule(targetProp, computation)
```

### Mutation API

#### MutationBuilder

```csharp
MutationBuilder WithPrecondition(Action<PreconditionBuilder> configure)
MutationBuilder HasEffect(Action<EffectBuilder> configure)
MutationBuilder AddParameter(string name, TypeExpression type)
Mutation Build()
```

#### Effects

```csharp
new PropertyEffect("PropertyName", new ConstantValue(value))
new PropertyEffect("PropertyName", new AppendEffect(item))
new PropertyEffect("PropertyName", new CurrentTimeEffect())
```

## License

See LICENSE.txt
