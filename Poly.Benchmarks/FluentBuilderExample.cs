using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Poly.DataModeling;
using Poly.DataModeling.Builders;
using Poly.Validation;
using Poly.DataModeling.Interpretation;
using Poly.Interpretation;
using System.Linq.Expressions;

namespace Poly.Benchmarks;

/// <summary>
/// Demonstrates the fluent builder API for creating domain models.
/// This shows how .NET developers can define types and relationships naturally.
/// </summary>
public static class FluentBuilderExample {
    public static DataModel CreateOrderManagementModel() {
        var model = new DataModelBuilder();

        // Define Customer type with fluent property definitions
        model.AddDataType("Customer", customer => {
            customer.AddProperty("Id", p => p.OfType<Guid>())
                .AddProperty("Email", p => p
                    .OfType<string>()
                    .WithConstraint(new NotNullConstraint())
                    .WithConstraint(new LengthConstraint(5, 255)))
                .AddProperty("Name", p => p
                    .OfType<string>()
                    .WithConstraint(new NotNullConstraint())
                    .WithConstraint(new LengthConstraint(1, 100)))
                .AddProperty("CreatedAt", p => p.OfType<DateTime>())
                .AddProperty("IsActive", p => p.OfType<bool>());

            // Define one-to-many relationship: Customer has many Orders
            customer.HasMany("Orders").OfType("Order")
                .WithOne("Customer");

            // Define one-to-many: Customer has many Addresses
            customer.HasMany("Addresses").OfType("Address")
                .WithOne("Customer");

            // Mutation: Activate customer (no params)
            customer.DefineMutation("Activate", m => m
                .Effects(e => e.SetConst("IsActive", true))
            );
        });

        // Define Order type
        model.AddDataType("Order", type => {
            type.AddProperty("Id", p => p.OfType<Guid>())
                .AddProperty("OrderNumber", p => p
                    .OfType<string>()
                    .WithConstraint(new NotNullConstraint()))
                .AddProperty("OrderDate", p => p.OfType<DateTime>())
                .AddProperty("TotalAmount", p => p
                    .OfType<double>()
                    .WithConstraint(new RangeConstraint(0.01, null)))
                .AddProperty("Status", p => p.OfType<string>());

            // Many-to-many: Order has many Products
            type.HasMany("Products").OfType("Product")
                .WithMany("Orders");
        });

        // Define Product type
        model.AddDataType("Product", type => {
            type.AddProperty("Id", p => p.OfType<Guid>())
                .AddProperty("SKU", p => p
                    .OfType<string>()
                    .WithConstraint(new NotNullConstraint())
                    .WithConstraint(new LengthConstraint(1, 50)))
                .AddProperty("Name", p => p
                    .OfType<string>()
                    .WithConstraint(new NotNullConstraint())
                    .WithConstraint(new LengthConstraint(1, 200)))
                .AddProperty("Price", p => p
                    .OfType<double>()
                    .WithConstraint(new RangeConstraint(0.0, null)))
                .AddProperty("StockQuantity", p => p
                    .OfType<int>()
                    .WithConstraint(new RangeConstraint(0, null)));

            // Mutation: Adjust stock with delta parameter
            type.DefineMutation("AdjustStock", m => m
                .Param("delta", p => p.OfType<int>())
                .Effects(e => e.IncrementFromParam("StockQuantity", "delta"))
            );
        });

        // Define Address type
        model.AddDataType("Address", type => {
            type.AddProperty("Id", p => p.OfType<Guid>())
                .AddProperty("Street", p => p
                    .OfType<string>()
                    .WithConstraint(new NotNullConstraint()))
                .AddProperty("City", p => p
                    .OfType<string>()
                    .WithConstraint(new NotNullConstraint()))
                .AddProperty("PostalCode", p => p
                    .OfType<string>()
                    .WithConstraint(new LengthConstraint(3, 10)))
                .AddProperty("Country", p => p
                    .OfType<string>()
                    .WithConstraint(new NotNullConstraint()));
        });

        return model.Build();
    }

    public static void Run() {        
        Console.WriteLine("=== Fluent Builder API Example ===\n");
        
        var model = CreateOrderManagementModel();
        
        // Register model types into the Interpretation type system
        var ctx = new InterpretationContext();
        model.RegisterIn(ctx);
        var customerDef = ctx.GetTypeDefinition("Customer");
        if (customerDef is not null) {
            Console.WriteLine("\nRegistered type in Interpretation system: Customer");
            Console.WriteLine("Members: " + string.Join(", ", customerDef.Members.Select(m => m.Name)));
            
            // Build and execute a simple accessor: @obj.Email
            var param = ctx.AddParameter("@obj", customerDef);
            var emailValue = param.GetMember("Email");
            var body = emailValue.BuildExpression(ctx);
            var lambda = System.Linq.Expressions.Expression.Lambda<Func<IDictionary<string, object>, string>>(
                body,
                (System.Linq.Expressions.ParameterExpression)param.BuildExpression(ctx)
            );
            var fn = lambda.Compile();
            var sample = new Dictionary<string, object> { ["Email"] = "dev@example.com" };
            Console.WriteLine($"Accessor test (@obj.Email) => {fn(sample)}");

        // Validation Examples
        Console.WriteLine("\n=== Validation Examples ===");
        var validator = new Validator(model);
        
        // Valid customer
        var validCustomer = new Dictionary<string, object?> {
            ["Id"] = Guid.NewGuid(),
            ["Email"] = "john.doe@example.com",
            ["Name"] = "John Doe",
            ["CreatedAt"] = DateTime.UtcNow,
            ["IsActive"] = true
        };
        
        var result1 = validator.Validate("Customer", validCustomer);
        Console.WriteLine($"\nValid customer: {result1}");
        
        // Invalid customer - missing required field
        var invalidCustomer1 = new Dictionary<string, object?> {
            ["Id"] = Guid.NewGuid(),
            ["Name"] = "Jane Doe"
            // Email is missing (required by NotNull constraint)
        };
        
        var result2 = validator.Validate("Customer", invalidCustomer1);
        Console.WriteLine($"\nMissing email: {result2}");
        if (!result2.IsValid) {
            foreach (var error in result2.Errors) {
                Console.WriteLine($"  - {error}");
            }
        }
        
        // Invalid customer - email too short
        var invalidCustomer2 = new Dictionary<string, object?> {
            ["Id"] = Guid.NewGuid(),
            ["Email"] = "a@b", // Too short (min 5 chars)
            ["Name"] = "Bob Smith",
            ["CreatedAt"] = DateTime.UtcNow,
            ["IsActive"] = false
        };
        
        var result3 = validator.Validate("Customer", invalidCustomer2);
        Console.WriteLine($"\nEmail too short: {result3}");
        if (!result3.IsValid) {
            foreach (var error in result3.Errors) {
                Console.WriteLine($"  - {error}");
            }
        }
        
        // Invalid product - negative price
        var invalidProduct = new Dictionary<string, object?> {
            ["Id"] = Guid.NewGuid(),
            ["SKU"] = "WIDGET-001",
            ["Name"] = "Test Widget",
            ["Price"] = -10.0, // Negative price (min 0.0)
            ["StockQuantity"] = 50
        };
        
        var result4 = validator.Validate("Product", invalidProduct);
        Console.WriteLine($"\nNegative price: {result4}");
        if (!result4.IsValid) {
            foreach (var error in result4.Errors) {
                Console.WriteLine($"  - {error}");
            }
        }
        }
        
        var options = new JsonSerializerOptions {
            WriteIndented = true,
            TypeInfoResolver = DataModelPropertyPolymorphicJsonTypeResolver.Shared
        };

        Console.WriteLine("Generated Order Management Domain Model:\n");
        Console.WriteLine(JsonSerializer.Serialize(model, options));
        
        Console.WriteLine("\n=== Model Summary ===");
        Console.WriteLine($"Types: {model.Types.Count()}");
        Console.WriteLine($"Relationships: {model.Relationships.Count()}");
        
        Console.WriteLine("\nTypes defined:");
        foreach (var type in model.Types) {
            Console.WriteLine($"  - {type.Name} ({type.Properties.Count()} properties)");
        }
        
        Console.WriteLine("\nRelationships defined:");
        foreach (var rel in model.Relationships) {
            Console.WriteLine($"  - {rel.Name}: {rel.Source.TypeName} → {rel.Target.TypeName}");
        }
    }
}
