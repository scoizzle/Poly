using Poly.DataModeling;
using Poly.DataModeling.Builders;

namespace Poly.Tests.DataModeling;

public class DataTypeBuilderTests {
    [Test]
    public async Task CreateDataType_WithName() {
        var builder = new DataTypeBuilder("Person");
        var result = builder.Build();

        await Assert.That(result.Name).IsEqualTo("Person");
        await Assert.That(result.Properties).IsNotNull();
    }

    [Test]
    public async Task AddProperty_WithPropertyBuilder() {
        var builder = new DataTypeBuilder("Person");
        builder.AddProperty("Name", b => b.OfType<string>());
        
        var result = builder.Build();

        await Assert.That(result.Properties.Count()).IsEqualTo(1);
        var property = result.Properties.First();
        await Assert.That(property.Name).IsEqualTo("Name");
    }

    [Test]
    public async Task AddProperty_MultipleProperties() {
        var builder = new DataTypeBuilder("Person");
        builder.AddProperty("FirstName", b => b.OfType<string>());
        builder.AddProperty("LastName", b => b.OfType<string>());
        builder.AddProperty("Age", b => b.OfType<int>());
        
        var result = builder.Build();

        await Assert.That(result.Properties.Count()).IsEqualTo(3);
    }

    [Test]
    public async Task SetName_UpdatesName() {
        var builder = new DataTypeBuilder("Person");
        builder.SetName("Employee");
        
        var result = builder.Build();

        await Assert.That(result.Name).IsEqualTo("Employee");
    }

    [Test]
    public async Task FluentBuilding_ChainedCalls() {
        var result = new DataTypeBuilder("Product")
            .AddProperty("Name", b => b.OfType<string>())
            .AddProperty("Price", b => b.OfType<double>())
            .Build();

        await Assert.That(result.Name).IsEqualTo("Product");
        await Assert.That(result.Properties.Count()).IsEqualTo(2);
    }

    [Test]
    public async Task HasOne_CreatesRelationship() {
        var builder = new DataTypeBuilder("Person");
        builder.AddProperty("AddressId", b => b.OfType<Guid>());
        
        var relationshipBuilder = builder.HasOne("Address");

        await Assert.That(relationshipBuilder).IsNotNull();
    }

    [Test]
    public async Task HasMany_CreatesRelationship() {
        var builder = new DataTypeBuilder("Person");
        
        var relationshipBuilder = builder.HasMany("Orders");

        await Assert.That(relationshipBuilder).IsNotNull();
    }

    [Test]
    public async Task HasMutation_WithBuilder() {
        var builder = new DataTypeBuilder("Person");
        builder.AddProperty("Age", b => b.OfType<int>());
        
        builder.HasMutation("HaveBirthday", mutationBuilder => {
            // Configure mutation
        });
        
        var result = builder.Build();

        await Assert.That(result.Mutations.Count()).IsEqualTo(1);
        var mutation = result.Mutations.First();
        await Assert.That(mutation.Name).IsEqualTo("HaveBirthday");
    }

    [Test]
    public async Task HasMutation_WithActions() {
        var builder = new DataTypeBuilder("Person");
        builder.AddProperty("Age", b => b.OfType<int>());
        
        builder.HasMutation("Grow", 
            preconditions: [p => { }],
            effects: [e => { }]);
        
        var result = builder.Build();

        await Assert.That(result.Mutations.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task BuilderProperties_Accessible() {
        var builder = new DataTypeBuilder("Product");
        builder.AddProperty("Name", b => b.OfType<string>());
        
        await Assert.That(builder.Name).IsEqualTo("Product");
        await Assert.That(builder.Properties.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task ComplexDataType_WithMultipleElements() {
        var dataType = new DataTypeBuilder("Order")
            .AddProperty("OrderId", b => b.OfType<Guid>())
            .AddProperty("CustomerName", b => b.OfType<string>())
            .AddProperty("Total", b => b.OfType<double>())
            .AddProperty("OrderDate", b => b.OfType<DateTime>())
            .Build();

        await Assert.That(dataType.Name).IsEqualTo("Order");
        await Assert.That(dataType.Properties.Count()).IsEqualTo(4);
        
        var propertyNames = dataType.Properties.Select(p => p.Name).ToList();
        await Assert.That(propertyNames).Contains("OrderId");
        await Assert.That(propertyNames).Contains("CustomerName");
        await Assert.That(propertyNames).Contains("Total");
        await Assert.That(propertyNames).Contains("OrderDate");
    }

    [Test]
    public async Task NameIsRequired() {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => {
            new DataTypeBuilder(null!);
        });
    }

    [Test]
    public async Task AddProperty_WithNullProperty_Throws() {
        var builder = new DataTypeBuilder("Person");

        await Assert.ThrowsAsync<ArgumentNullException>(async () => {
            builder.AddProperty(null!);
        });
    }

    [Test]
    public async Task SetName_WithNull_Throws() {
        var builder = new DataTypeBuilder("Person");

        await Assert.ThrowsAsync<ArgumentNullException>(async () => {
            builder.SetName(null!);
        });
    }

    [Test]
    public async Task PropertyBuilder_Integration_String() {
        var builder = new DataTypeBuilder("Person");
        builder.AddProperty("Email", b => b.OfType<string>());
        
        var result = builder.Build();

        await Assert.That(result.Properties.Count()).IsEqualTo(1);
        var property = result.Properties.First();
        await Assert.That(property.Name).IsEqualTo("Email");
        await Assert.That(property).IsOfType(typeof(StringProperty));
    }

    [Test]
    public async Task PropertyBuilder_Integration_WithDefault() {
        var builder = new DataTypeBuilder("Settings");
        builder.AddProperty("IsActive", b => {
            b.OfType<bool>();
            b.WithDefault(true);
        });
        
        var result = builder.Build();

        await Assert.That(result.Properties.Count()).IsEqualTo(1);
        var property = result.Properties.First();
        await Assert.That(property.Name).IsEqualTo("IsActive");
        await Assert.That(property.DefaultValue).IsEqualTo(true);
    }

    [Test]
    public async Task PropertyBuilder_MultipleTypes() {
        var builder = new DataTypeBuilder("Product");
        builder.AddProperty("Name", b => b.OfType<string>());
        builder.AddProperty("Price", b => b.OfType<double>());
        builder.AddProperty("Stock", b => b.OfType<int>());
        builder.AddProperty("IsAvailable", b => b.OfType<bool>());
        
        var result = builder.Build();

        await Assert.That(result.Properties.Count()).IsEqualTo(4);
    }

    [Test]
    public async Task PropertyBuilder_ReferenceType() {
        var builder = new DataTypeBuilder("Order");
        builder.AddProperty("CustomerId", b => b.OfType("Customer"));
        
        var result = builder.Build();

        await Assert.That(result.Properties.Count()).IsEqualTo(1);
        var property = result.Properties.First();
        await Assert.That(property).IsOfType(typeof(ReferenceProperty));
    }

    [Test]
    public async Task HasRelationships_Accessible() {
        var builder = new DataTypeBuilder("User");
        builder.HasOne("Profile");
        builder.HasMany("Posts");
        
        await Assert.That(builder.Relationships.Count()).IsEqualTo(2);
    }

    [Test]
    public async Task HasMutations_Accessible() {
        var builder = new DataTypeBuilder("Article");
        builder.HasMutation("Publish", m => { });
        builder.HasMutation("Archive", m => { });
        
        await Assert.That(builder.Mutations.Count()).IsEqualTo(2);
    }
}
