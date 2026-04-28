using Poly.Data.Modeling;
using Poly.Data.Modeling.Mermaid;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling;

public class MermaidDomainDiagramGeneratorTests {
    private static Domain BuildSimpleDomain() {
        var domain = DomainTestFactory.CreateDomain("Simple");

        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        domain.AddType(stringType);

        var customer = new Entity(domain, "Customer");
        customer.AddProperty(new Property(domain, "Name", stringType));
        domain.AddType(customer);

        var order = new Entity(domain, "Order");
        order.AddProperty(new Property(domain, "Total", stringType));
        domain.AddType(order);

        var rel = new Relationship(domain, "CustomerOrders", customer, order, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(rel);
        customer.AddRelationship(rel);

        return domain;
    }

    [Test]
    public async Task Generate_OutputStartsWithClassDiagram() {
        var domain = BuildSimpleDomain();
        var generator = new MermaidDomainDiagramGenerator();

        var result = generator.Generate(domain);

        await Assert.That(result).StartsWith("classDiagram");
    }

    [Test]
    public async Task Generate_EntityClassesAppear_ForNonPrimitiveTypes() {
        var domain = BuildSimpleDomain();
        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("class Customer");
        await Assert.That(result).Contains("class Order");
    }

    [Test]
    public async Task Generate_PrimitivesNotEmittedAsClasses() {
        var domain = BuildSimpleDomain();
        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).DoesNotContain("class string");
    }

    [Test]
    public async Task Generate_Properties_AppearInsideEntityClass() {
        var domain = BuildSimpleDomain();
        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("+string Name");
        await Assert.That(result).Contains("+string Total");
    }

    [Test]
    public async Task Generate_Relationship_EmitsOwnershipCompositionArrow() {
        var domain = BuildSimpleDomain();
        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("Customer");
        await Assert.That(result).Contains("*--");
        await Assert.That(result).Contains("Order");
        await Assert.That(result).Contains("CustomerOrders");
    }

    [Test]
    public async Task Generate_Stages_EmittedAsEnumerationClass() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        domain.AddType(stringType);

        var task = new Entity(domain, "Task");
        task.AddProperty(new Property(domain, "Title", stringType));
        task.AddStage(new Stage(domain, "Todo"));
        task.AddStage(new Stage(domain, "Done"));
        domain.AddType(task);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("class TaskStage");
        await Assert.That(result).Contains("<<enumeration>>");
        await Assert.That(result).Contains("Todo");
        await Assert.That(result).Contains("Done");
        await Assert.That(result).Contains("Task ..> TaskStage : stage");
    }

    [Test]
    public async Task Generate_SubStages_ShowParentAnnotation() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        domain.AddType(stringType);

        var task = new Entity(domain, "Task");
        var parentStage = new Stage(domain, "InProgress");
        var childStage = new Stage(domain, "Blocked") { Parent = parentStage };
        task.AddStage(parentStage);
        task.AddStage(childStage);
        domain.AddType(task);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("Blocked (InProgress)");
    }

    [Test]
    public async Task Generate_Policies_EmittedAsNote() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        domain.AddType(stringType);

        var item = new Entity(domain, "Item");
        var nameProp = new Property(domain, "Name", stringType);
        item.AddProperty(nameProp);
        domain.AddType(item);

        var policy = new Policy(domain, "RequireName");
        policy.AddRule(new PropertyRule { Value = nameProp, Constraints = new RequiredConstraint() });
        item.AddPolicy(policy);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("note for Item");
        await Assert.That(result).Contains("RequireName");
    }

    [Test]
    public async Task Generate_PropertyTypeReference_EmitsAssociationArrow() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        domain.AddType(stringType);

        var author = new Entity(domain, "Author");
        author.AddProperty(new Property(domain, "Name", stringType));
        domain.AddType(author);

        var book = new Entity(domain, "Book");
        book.AddProperty(new Property(domain, "Title", stringType));
        book.AddProperty(new Property(domain, "Author", author));
        domain.AddType(book);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("Book --> Author : Author");
    }

    [Test]
    public async Task Generate_RichRelationship_EmittedAsClass_WithSourceAndTargetLinks() {
        var domain = DomainTestFactory.CreateDomain();
        var instantType = new Primitive(domain, "instant", TypeCategory.Instant);
        domain.AddType(instantType);

        var agent = new Entity(domain, "Agent");
        domain.AddType(agent);
        var project = new Entity(domain, "Project");
        domain.AddType(project);

        var membership = new Relationship(
            domain,
            "AgentProjects",
            agent,
            project,
            RelationshipCardinality.ManyToMany,
            false
        );
        membership.AddProperty(new Property(domain, "JoinedAt", instantType));
        membership.AddStage(new Stage(domain, "Active"));
        domain.AddRelationship(membership);
        agent.AddRelationship(membership);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("class AgentProjects");
        await Assert.That(result).Contains("<<relationship>>");
        await Assert.That(result).Contains("+instant JoinedAt");
        await Assert.That(result).Contains("class AgentProjectsStage");
        await Assert.That(result).Contains("AgentProjects ..> Agent : source");
        await Assert.That(result).Contains("AgentProjects ..> Project : target");
    }

    [Test]
    public async Task Generate_NonOwnershipRelationship_UsesArrowNotComposition() {
        var domain = DomainTestFactory.CreateDomain();

        var a = new Entity(domain, "A");
        domain.AddType(a);
        var b = new Entity(domain, "B");
        domain.AddType(b);

        var rel = new Relationship(
            domain,
            "AtoB",
            a,
            b,
            RelationshipCardinality.OneToOne,
            false);

        domain.AddRelationship(rel);
        a.AddRelationship(rel);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("-->");
        await Assert.That(result).DoesNotContain("*--");
    }

    [Test]
    public async Task Generate_Events_EmittedAsEventClass() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        domain.AddType(stringType);

        var entity = new Entity(domain, "Order");
        domain.AddType(entity);

        var @event = new Event(domain, "OrderPlaced");
        @event.AddProperty(new Property(domain, "OrderId", stringType));
        entity.AddEvent(@event);
        domain.AddType(@event);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("class OrderPlaced");
        await Assert.That(result).Contains("<<event>>");
        await Assert.That(result).Contains("+string OrderId");
    }

    [Test]
    public async Task Generate_InheritedEntity_EmitsInheritanceArrow() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        domain.AddType(stringType);

        var user = new Entity(domain, "User");
        user.AddProperty(new Property(domain, "Name", stringType));
        domain.AddType(user);

        var agent = new Entity(domain, "Agent", user);
        domain.AddType(agent);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("User <|-- Agent");
    }

    [Test]
    public async Task Generate_Actions_AppearAsMethodsOnEntity() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        domain.AddType(stringType);

        var ticket = new Entity(domain, "Ticket");
        var openStage = new Stage(domain, "Open");
        var closeAction = new DomainAction(domain, "Close", ticket);
        closeAction.AddParameter(new Property(domain, "Reason", stringType));
        openStage.AddAction(closeAction);
        ticket.AddStage(openStage);
        domain.AddType(ticket);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("+Close(string)");
    }
}