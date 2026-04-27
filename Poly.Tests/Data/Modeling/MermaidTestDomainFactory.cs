using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;
using DomainEvent = Poly.Data.Modeling.Event;

namespace Poly.Tests.Data.Modeling;

internal static class MermaidTestDomainFactory {
    internal static Domain BuildSupportCaseDomain() {
        var domain = DomainTestFactory.CreateDomain("Support Case Management");

        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var intType = new Primitive(domain, "int", TypeCategory.Integer);
        var boolType = new Primitive(domain, "bool", TypeCategory.Primitive);
        var instantType = new Primitive(domain, "instant", TypeCategory.Instant);
        domain.AddType(stringType);
        domain.AddType(intType);
        domain.AddType(boolType);
        domain.AddType(instantType);

        var user = new Entity(domain, "User");
        user.AddProperty(new Property(domain, "Name", stringType));
        user.AddProperty(new Property(domain, "Email", stringType));
        domain.AddType(user);

        var customer = new Entity(domain, "Customer", user);
        domain.AddType(customer);

        var agent = new Entity(domain, "Agent", user);
        domain.AddType(agent);

        var supportCase = new Entity(domain, "SupportCase");
        supportCase.AddProperty(new Property(domain, "Title", stringType));
        supportCase.AddProperty(new Property(domain, "Priority", intType));
        supportCase.AddProperty(new Property(domain, "IsEscalated", boolType));
        domain.AddType(supportCase);

        var newStage = new Stage(domain, "New");
        var inProgressStage = new Stage(domain, "InProgress");
        var assignedStage = new Stage(domain, "Assigned") { Parent = inProgressStage };
        var resolvedStage = new Stage(domain, "Resolved");

        var caseAssignedEvent = new DomainEvent(domain, "CaseAssigned");
        caseAssignedEvent.AddProperty(new Property(domain, "AssignedTo", agent));
        var caseResolvedEvent = new DomainEvent(domain, "CaseResolved");
        caseResolvedEvent.AddProperty(new Property(domain, "ResolutionSummary", stringType));
        supportCase.AddEvent(caseAssignedEvent);
        supportCase.AddEvent(caseResolvedEvent);

        var assignAction = new DomainAction(domain, "Assign", supportCase);
        var assignAgentParameter = new Property(domain, "Agent", agent);
        assignAction.AddParameter(assignAgentParameter);
        assignAction.AddEffect(new StageTransition { TargetStage = assignedStage });
        var publishAssigned = new PublishEvent { Event = caseAssignedEvent };
        var assignedToProperty = caseAssignedEvent.RequireProperty("AssignedTo");
        publishAssigned.BindProperty(assignedToProperty, assignAgentParameter);
        assignAction.AddEffect(publishAssigned);
        newStage.AddAction(assignAction);

        var addNoteAction = new DomainAction(domain, "AddNote", supportCase);
        addNoteAction.AddParameter(new Property(domain, "NoteText", stringType));
        inProgressStage.AddAction(addNoteAction);

        var resolveAction = new DomainAction(domain, "Resolve", supportCase);
        var resolutionSummaryParameter = new Property(domain, "ResolutionSummary", stringType);
        resolveAction.AddParameter(resolutionSummaryParameter);
        resolveAction.AddEffect(new StageTransition { TargetStage = resolvedStage });
        var publishResolved = new PublishEvent { Event = caseResolvedEvent };
        var resolvedSummaryProperty = caseResolvedEvent.RequireProperty("ResolutionSummary");
        publishResolved.BindProperty(resolvedSummaryProperty, resolutionSummaryParameter);
        resolveAction.AddEffect(publishResolved);
        inProgressStage.AddAction(resolveAction);

        supportCase.AddStage(newStage);
        supportCase.AddStage(inProgressStage);
        supportCase.AddStage(assignedStage);
        supportCase.AddStage(resolvedStage);

        var note = new Entity(domain, "Note", supportCase);
        note.AddProperty(new Property(domain, "Content", stringType));
        note.AddProperty(new Property(domain, "Author", user));
        var noteAuthor = note.RequireProperty("Author");
        var noteDraftStage = new Stage(domain, "Draft") { Parent = inProgressStage };
        note.AddStage(noteDraftStage);

        domain.AddType(note);

        var requireTitle = new Policy(domain, "RequireTitle") { AggregationStrategy = PolicyAggregationStrategy.All };
        var supportCaseTitle = supportCase.RequireProperty("Title");
        requireTitle.AddRule(new PropertyRule {
            Value = supportCaseTitle,
            Constraints = new RequiredConstraint()
        });
        supportCase.AddPolicy(requireTitle);

        var ownership = new Relationship(domain, "CustomerCases") {
            Source = customer,
            Target = supportCase,
            Cardinality = RelationshipCardinality.OneToMany,
            SourceOwnsTarget = true
        };
        domain.AddRelationship(ownership);
        customer.AddRelationship(ownership);

        var caseNotes = new Relationship(domain, "SupportCaseNotes") {
            Source = supportCase,
            Target = note,
            Cardinality = RelationshipCardinality.OneToMany,
            SourceOwnsTarget = true
        };
        domain.AddRelationship(caseNotes);
        supportCase.AddRelationship(caseNotes);

        addNoteAction.AddEffect(new CreateEntityInstance {
            EntityType = note,
            InitialStage = noteDraftStage
        });

        var customerNotes = new Relationship(domain, "CustomerNotes") {
            Source = customer,
            Target = note,
            Cardinality = RelationshipCardinality.OneToMany,
            SourceOwnsTarget = false
        };

        var onlyAgentsCanCreateUserNotes = new Policy(domain, "OnlyAgentsCanCreateUserNotes") { AggregationStrategy = PolicyAggregationStrategy.All };
        onlyAgentsCanCreateUserNotes.AddRule(new PropertyRule {
            Value = noteAuthor,
            Constraints = new RequiredConstraint()
        });

        var onlyAgentsCanViewUserNotes = new Policy(domain, "OnlyAgentsCanViewUserNotes") { AggregationStrategy = PolicyAggregationStrategy.All };
        onlyAgentsCanViewUserNotes.AddRule(new PropertyRule {
            Value = noteAuthor,
            Constraints = new RequiredConstraint()
        });

        customerNotes.AddPolicy(onlyAgentsCanCreateUserNotes);
        customerNotes.AddPolicy(onlyAgentsCanViewUserNotes);

        domain.AddRelationship(customerNotes);
        customer.AddRelationship(customerNotes);

        var agentCases = new Relationship(domain, "AgentSupportCases") {
            Source = agent,
            Target = supportCase,
            Cardinality = RelationshipCardinality.ManyToMany,
            SourceOwnsTarget = false
        };
        var activeAssignmentStage = new Stage(domain, "Active");
        var inactiveAssignmentStage = new Stage(domain, "Inactive");
        var requireAssignedAtWhenActive = new Policy(domain, "RequireAssignedAtWhenActive");
        var requireUnassignedAtWhenInactive = new Policy(domain, "RequireUnassignedAtWhenInactive");
        agentCases.AddStage(activeAssignmentStage);
        agentCases.AddStage(inactiveAssignmentStage);
        var assignedAt = new Property(domain, "AssignedAt", instantType);
        assignedAt.AddConstraint(new RequiredConstraint());
        var unassignedAt = new Property(domain, "UnassignedAt", instantType);
        agentCases.AddProperty(assignedAt);
        agentCases.AddProperty(unassignedAt);

        requireAssignedAtWhenActive.AddRule(new PropertyRule {
            Value = assignedAt,
            Constraints = new RequiredConstraint()
        });

        requireUnassignedAtWhenInactive.AddRule(new PropertyRule {
            Value = unassignedAt,
            Constraints = new RequiredConstraint()
        });

        activeAssignmentStage.AddPolicy(requireAssignedAtWhenActive);
        inactiveAssignmentStage.AddPolicy(requireUnassignedAtWhenInactive);
        domain.AddRelationship(agentCases);
        agent.AddRelationship(agentCases);

        return domain;
    }
}