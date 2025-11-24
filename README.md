# Poly - Fluent Domain Modeling for .NET

Poly provides a fluent, strongly-typed API for defining domain models that can be used for validation, code generation, schema generation, and API creation.

## Quick Start

```csharp
using Poly.DataModeling;
using Poly.DataModeling.Builders;
using Poly.Validation;

var model = new DataModelBuilder();

// Define types with fluent API
model.AddDataType(type => {
    type.SetName("Customer")
        .AddProperty("Id", p => p.OfType<Guid>())
        .AddProperty("Email", p => p
            .OfType<string>()
            .WithConstraint(new NotNullConstraint())
            .WithConstraint(new LengthConstraint(5, 255)))
        .AddProperty("Name", p => p
            .OfType<string>()
            .WithConstraint(new NotNullConstraint())
            .WithConstraint(new LengthConstraint(1, 100)))
        // Define relationships inline
        .HasMany("Order", "Orders")
            .WithOne("Order", "Customer");
});

var dataModel = model.Build();
```

## Features

### 🔧 Fluent Property Definition

Define properties with type safety and inline constraints:

```csharp
type.AddProperty("Email", p => p
    .OfType<string>()
    .WithConstraint(new NotNullConstraint())
    .WithConstraint(new LengthConstraint(5, 255))
);
```

**Supported Types:**
- `string`, `int`, `long`, `double`, `bool`
- `Guid`, `DateTime`, `DateOnly`, `TimeOnly`

### 🔗 Intuitive Relationship Syntax

Define relationships naturally with `HasOne`, `HasMany`, `WithOne`, `WithMany`:

```csharp
// One-to-Many: Customer has many Orders
type.HasMany("Order", "Orders")
    .WithOne("Order", "Customer");

// Many-to-Many: Order has many Products
type.HasMany("Product", "Products")
    .WithMany("Product", "Orders");

// One-to-One: User has one Profile
type.HasOne("Profile", "Profile")
    .WithOne("User", "User");
```

### ✅ Built-in Constraints

**Property-level constraints:**
- `NotNullConstraint()` - Value cannot be null
- `LengthConstraint(min, max)` - String/collection length validation
- `RangeConstraint(min, max)` - Numeric range validation
- `EqualityConstraint(value)` - Must equal specific value

**Type-level rules:**
- `ConditionalRule` - If condition, then apply rule
- `MutualExclusionRule` - Only N properties can have values
- `PropertyDependencyRule` - Property A requires Property B
- `ComparisonRule` - Compare two properties
- `ComputedValueRule` - Calculate derived values

### 🎯 Relationship Constraints

Add validation to relationship ends:

```csharp
type.HasMany("Pet", "Pets")
    .WithOne("Customer", "Owner")
    .WithTargetConstraint(new NotNullConstraint()); // Pet must have owner
```

## Complete Example

See `FluentBuilderExample.cs` for a full order management system with:
- 4 types (Customer, Order, Product, Address)
- 3 relationships (Customer→Orders, Customer→Addresses, Order↔Products)
- Property constraints (length, range, not-null)

Run it:
```bash
cd Poly.Benchmarks
dotnet run
```

## Architecture

**DataModelBuilder** → Collection of types and relationships  
**DataTypeBuilder** → Type with properties and rules  
**PropertyBuilder** → Property with type and constraints  
**RelationshipBuilder** → Source + Target ends with constraints

### JSON Serialization

Models serialize to clean, portable JSON:

```json
{
  "Types": [
    {
      "Name": "Customer",
      "Properties": [
        {
          "Type": "string",
          "Name": "Email",
          "Constraints": [
            { "ConstraintType": "NotNull" },
            { "ConstraintType": "Length", "MinLength": 5, "MaxLength": 255 }
          ]
        }
      ]
    }
  ],
  "Relationships": [
    {
      "Type": "ManyToOne",
      "Name": "Customer.Orders_Order.Customer",
      "Source": { "TypeName": "Customer", "PropertyName": "Orders" },
      "Target": { "TypeName": "Order", "PropertyName": "Customer" }
    }
  ]
}
```

## Roadmap

- ✅ Fluent type and property builders
- ✅ Relationship definitions with constraints
- ✅ Property and type-level validation rules
- 🚧 Runtime validation engine
- 🚧 SQL schema generation
- 🚧 Migration diff engine
- 🚧 Query/filter DSL
- 🚧 API code generation (Minimal APIs + OpenAPI)
- 🚧 Authorization model

## API Reference

### DataModelBuilder

```csharp
DataModelBuilder AddDataType(Action<DataTypeBuilder> configure)
DataModelBuilder AddDataType(DataType dataType)
DataModelBuilder AddRelationship(Relationship relationship)
DataModel Build()
```

### DataTypeBuilder

```csharp
DataTypeBuilder SetName(string name)
DataTypeBuilder AddProperty(string name, Action<PropertyBuilder> configure)
DataTypeBuilder AddProperty(DataProperty property)
DataTypeBuilder AddRule(Rule rule)
RelationshipBuilder HasOne(string targetTypeName, string? propertyName = null)
RelationshipBuilder HasMany(string targetTypeName, string? propertyName = null)
DataType Build()
```

### PropertyBuilder

```csharp
PropertyBuilder OfType<T>()
PropertyBuilder OfType(Type type)
PropertyBuilder WithConstraint(Constraint constraint)
PropertyBuilder WithConstraints(params Constraint[] constraints)
DataProperty Build()
```

### RelationshipBuilder

```csharp
RelationshipBuilder WithOne(string targetTypeName, string? targetPropertyName = null)
RelationshipBuilder WithMany(string targetTypeName, string? targetPropertyName = null)
RelationshipBuilder WithSourceConstraint(Constraint constraint)
RelationshipBuilder WithSourceConstraints(params Constraint[] constraints)
RelationshipBuilder WithTargetConstraint(Constraint constraint)
RelationshipBuilder WithTargetConstraints(params Constraint[] constraints)
Relationship Build()
```

## License

See LICENSE.txt
