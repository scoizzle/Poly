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
        MutationApply.AddType(domain, stringType);

        var customer = new Entity(domain, "Customer");
        MutationApply.AddProperty(customer, new Property(domain, "Name", stringType));
        MutationApply.AddType(domain, customer);

        var order = new Entity(domain, "Order");
        MutationApply.AddProperty(order, new Property(domain, "Total", stringType));
        MutationApply.AddType(domain, order);

        var rel = new Relationship(domain, "CustomerOrders", customer, order, RelationshipCardinality.OneToMany, true);
        MutationApply.AddRelationship(domain, rel);
        MutationApply.AddRelationship(customer, rel);

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
        MutationApply.AddType(domain, stringType);

        var task = new Entity(domain, "Task");
        MutationApply.AddProperty(task, new Property(domain, "Title", stringType));
        MutationApply.AddStage(task, new Stage(domain, "Todo"));
        MutationApply.AddStage(task, new Stage(domain, "Done"));
        MutationApply.AddType(domain, task);

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
        MutationApply.AddType(domain, stringType);

        var task = new Entity(domain, "Task");
        var parentStage = new Stage(domain, "InProgress");
        var childStage = new Stage(domain, "Blocked") { Parent = parentStage };
        MutationApply.AddStage(task, parentStage);
        MutationApply.AddStage(task, childStage);
        MutationApply.AddType(domain, task);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("Blocked (InProgress)");
    }

    [Test]
    public async Task Generate_Policies_EmittedAsNote() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        MutationApply.AddType(domain, stringType);

        var item = new Entity(domain, "Item");
        var nameProp = new Property(domain, "Name", stringType);
        MutationApply.AddProperty(item, nameProp);
        MutationApply.AddType(domain, item);

        var policy = new Policy(domain, "RequireName");
        MutationApply.AddRule(policy, new PropertyRule { Value = nameProp, Constraints = new RequiredConstraint() });
        MutationApply.AddPolicy(item, policy);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("note for Item");
        await Assert.That(result).Contains("RequireName");
    }

    [Test]
    public async Task Generate_PropertyTypeReference_EmitsAssociationArrow() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        MutationApply.AddType(domain, stringType);

        var author = new Entity(domain, "Author");
        MutationApply.AddProperty(author, new Property(domain, "Name", stringType));
        MutationApply.AddType(domain, author);

        var book = new Entity(domain, "Book");
        MutationApply.AddProperty(book, new Property(domain, "Title", stringType));
        MutationApply.AddProperty(book, new Property(domain, "Author", author));
        MutationApply.AddType(domain, book);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("Book --> Author : Author");
    }

    [Test]
    public async Task Generate_RichRelationship_EmittedAsClass_WithSourceAndTargetLinks() {
        var domain = DomainTestFactory.CreateDomain();
        var instantType = new Primitive(domain, "instant", TypeCategory.Instant);
        MutationApply.AddType(domain, instantType);

        var agent = new Entity(domain, "Agent");
        MutationApply.AddType(domain, agent);
        var project = new Entity(domain, "Project");
        MutationApply.AddType(domain, project);

        var membership = new Relationship(
            domain,
            "AgentProjects",
            agent,
            project,
            RelationshipCardinality.ManyToMany,
            false
        );
        MutationApply.AddProperty(membership, new Property(domain, "JoinedAt", instantType));
        MutationApply.AddStage(membership, new Stage(domain, "Active"));
        MutationApply.AddRelationship(domain, membership);
        MutationApply.AddRelationship(agent, membership);

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
        MutationApply.AddType(domain, a);
        var b = new Entity(domain, "B");
        MutationApply.AddType(domain, b);

        var rel = new Relationship(
            domain,
            "AtoB",
            a,
            b,
            RelationshipCardinality.OneToOne,
            false);

        MutationApply.AddRelationship(domain, rel);
        MutationApply.AddRelationship(a, rel);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("-->");
        await Assert.That(result).DoesNotContain("*--");
    }

    [Test]
    public async Task Generate_Events_EmittedAsEventClass() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        MutationApply.AddType(domain, stringType);

        var entity = new Entity(domain, "Order");
        MutationApply.AddType(domain, entity);

        var @event = new Event(domain, "OrderPlaced");
        MutationApply.AddProperty(@event, new Property(domain, "OrderId", stringType));
        MutationApply.AddEvent(entity, @event);
        MutationApply.AddType(domain, @event);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("class OrderPlaced");
        await Assert.That(result).Contains("<<event>>");
        await Assert.That(result).Contains("+string OrderId");
    }

    [Test]
    public async Task Generate_InheritedEntity_EmitsInheritanceArrow() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        MutationApply.AddType(domain, stringType);

        var user = new Entity(domain, "User");
        MutationApply.AddProperty(user, new Property(domain, "Name", stringType));
        MutationApply.AddType(domain, user);

        var agent = new Entity(domain, "Agent", user);
        MutationApply.AddType(domain, agent);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("User <|-- Agent");
    }

    [Test]
    public async Task Generate_Actions_AppearAsMethodsOnEntity() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        MutationApply.AddType(domain, stringType);

        var ticket = new Entity(domain, "Ticket");
        var openStage = new Stage(domain, "Open");
        var closeAction = new DomainAction(domain, "Close", ticket);
        MutationApply.AddParameter(closeAction, new Property(domain, "Reason", stringType));
        MutationApply.AddAction(openStage, closeAction);
        MutationApply.AddStage(ticket, openStage);
        MutationApply.AddType(domain, ticket);

        var result = new MermaidDomainDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("+Close(string)");
    }
}