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
        var mutation = domain.CreateMutation();

        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var intType = new Primitive(domain, "int", TypeCategory.Integer);
        var boolType = new Primitive(domain, "bool", TypeCategory.Primitive);
        var instantType = new Primitive(domain, "instant", TypeCategory.Instant);
        mutation.AddType(stringType);
        mutation.AddType(intType);
        mutation.AddType(boolType);
        mutation.AddType(instantType);

        var user = new Entity(domain, "User");
        mutation.AddProperty(user, new Property(domain, "Name", stringType));
        mutation.AddProperty(user, new Property(domain, "Email", stringType));
        mutation.AddType(user);

        var customer = new Entity(domain, "Customer", user);
        mutation.AddType(customer);

        var agent = new Entity(domain, "Agent", user);
        mutation.AddType(agent);

        var supportCase = new Entity(domain, "SupportCase");
        var supportCaseTitle = new Property(domain, "Title", stringType);
        mutation.AddProperty(supportCase, supportCaseTitle);
        mutation.AddProperty(supportCase, new Property(domain, "Priority", intType));
        mutation.AddProperty(supportCase, new Property(domain, "IsEscalated", boolType));
        mutation.AddType(supportCase);

        var newStage = new Stage(domain, "New");
        var inProgressStage = new Stage(domain, "InProgress");
        var assignedStage = new Stage(domain, "Assigned") { Parent = inProgressStage };
        var resolvedStage = new Stage(domain, "Resolved");

        var caseAssignedEvent = new DomainEvent(domain, "CaseAssigned");
        var assignedToProperty = new Property(domain, "AssignedTo", agent);
        mutation.AddProperty(caseAssignedEvent, assignedToProperty);
        var caseResolvedEvent = new DomainEvent(domain, "CaseResolved");
        var resolutionSummaryProperty = new Property(domain, "ResolutionSummary", stringType);
        mutation.AddProperty(caseResolvedEvent, resolutionSummaryProperty);
        mutation.AddEvent(supportCase, caseAssignedEvent);
        mutation.AddEvent(supportCase, caseResolvedEvent);

        var assignAction = new DomainAction(domain, "Assign", supportCase);
        var assignAgentParameter = new Property(domain, "Agent", agent);
        mutation.AddParameter(assignAction, assignAgentParameter);
        mutation.AddEffect(assignAction, new StageTransition(domain) { TargetStage = assignedStage });
        var publishAssigned = new PublishEvent(domain) { Event = caseAssignedEvent };
        publishAssigned.BindProperty(assignedToProperty, assignAgentParameter);
        mutation.AddEffect(assignAction, publishAssigned);
        mutation.AddAction(newStage, assignAction);

        var addNoteAction = new DomainAction(domain, "AddNote", supportCase);
        mutation.AddParameter(addNoteAction, new Property(domain, "NoteText", stringType));
        mutation.AddAction(inProgressStage, addNoteAction);

        var resolveAction = new DomainAction(domain, "Resolve", supportCase);
        var resolutionSummaryParameter = new Property(domain, "ResolutionSummary", stringType);
        mutation.AddParameter(resolveAction, resolutionSummaryParameter);
        mutation.AddEffect(resolveAction, new StageTransition(domain) { TargetStage = resolvedStage });
        var publishResolved = new PublishEvent(domain) { Event = caseResolvedEvent };
        publishResolved.BindProperty(resolutionSummaryProperty, resolutionSummaryParameter);
        mutation.AddEffect(resolveAction, publishResolved);
        mutation.AddAction(inProgressStage, resolveAction);

        mutation.AddStage(supportCase, newStage);
        mutation.AddStage(supportCase, inProgressStage);
        mutation.AddStage(supportCase, assignedStage);
        mutation.AddStage(supportCase, resolvedStage);

        var note = new Entity(domain, "Note", supportCase);
        mutation.AddProperty(note, new Property(domain, "Content", stringType));
        var noteAuthor = new Property(domain, "Author", user);
        mutation.AddProperty(note, noteAuthor);
        var noteDraftStage = new Stage(domain, "Draft") { Parent = inProgressStage };
        mutation.AddStage(note, noteDraftStage);

        mutation.AddType(note);

        var requireTitle = new Policy(domain, "RequireTitle") { AggregationStrategy = PolicyAggregationStrategy.All };
        mutation.AddRule(requireTitle, new PropertyRule(domain, "TitleRequired", supportCaseTitle, new RequiredConstraint()));
        mutation.AddPolicy(supportCase, requireTitle);

        var ownership = new Relationship(domain, "CustomerCases", customer, supportCase, RelationshipCardinality.OneToMany, true);
        mutation.AddRelationship(ownership);
        mutation.AddEntityRelationship(customer, ownership);

        var caseNotes = new Relationship(domain, "SupportCaseNotes", supportCase, note, RelationshipCardinality.OneToMany, true);
        mutation.AddRelationship(caseNotes);
        mutation.AddEntityRelationship(supportCase, caseNotes);

        mutation.AddEffect(addNoteAction, new CreateEntityInstance(domain) {
            EntityType = note,
            InitialStage = noteDraftStage
        });

        var customerNotes = new Relationship(domain, "CustomerNotes", customer, note, RelationshipCardinality.OneToMany, false);

        var onlyAgentsCanCreateUserNotes = new Policy(domain, "OnlyAgentsCanCreateUserNotes") { AggregationStrategy = PolicyAggregationStrategy.All };
        mutation.AddRule(onlyAgentsCanCreateUserNotes, new PropertyRule(domain, "OnlyAgentsCanCreateUserNotes", noteAuthor, new RequiredConstraint()));

        var onlyAgentsCanViewUserNotes = new Policy(domain, "OnlyAgentsCanViewUserNotes") { AggregationStrategy = PolicyAggregationStrategy.All };
        mutation.AddRule(onlyAgentsCanViewUserNotes, new PropertyRule(domain, "OnlyAgentsCanViewUserNotes", noteAuthor, new RequiredConstraint()));

        mutation.AddPolicy(customerNotes, onlyAgentsCanCreateUserNotes);
        mutation.AddPolicy(customerNotes, onlyAgentsCanViewUserNotes);

        mutation.AddRelationship(customerNotes);
        mutation.AddEntityRelationship(customer, customerNotes);

        var agentCases = new Relationship(domain, "AgentSupportCases", agent, supportCase, RelationshipCardinality.ManyToMany, false);
        var activeAssignmentStage = new Stage(domain, "Active");
        var inactiveAssignmentStage = new Stage(domain, "Inactive");
        var requireAssignedAtWhenActive = new Policy(domain, "RequireAssignedAtWhenActive");
        var requireUnassignedAtWhenInactive = new Policy(domain, "RequireUnassignedAtWhenInactive");
        mutation.AddStage(agentCases, activeAssignmentStage);
        mutation.AddStage(agentCases, inactiveAssignmentStage);
        var assignedAt = new Property(domain, "AssignedAt", instantType);
        mutation.AddConstraint(assignedAt, new RequiredConstraint());
        var unassignedAt = new Property(domain, "UnassignedAt", instantType);
        mutation.AddProperty(agentCases, assignedAt);
        mutation.AddProperty(agentCases, unassignedAt);

        mutation.AddRule(requireAssignedAtWhenActive, new PropertyRule(domain, "RequireAssignedAtWhenActive", assignedAt, new RequiredConstraint()));

        mutation.AddRule(requireUnassignedAtWhenInactive, new PropertyRule(domain, "RequireUnassignedAtWhenInactive", unassignedAt, new RequiredConstraint()));

        mutation.AddPolicy(activeAssignmentStage, requireAssignedAtWhenActive);
        mutation.AddPolicy(inactiveAssignmentStage, requireUnassignedAtWhenInactive);
        mutation.AddRelationship(agentCases);
        mutation.AddEntityRelationship(agent, agentCases);

        var result = mutation.Apply();

        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0) {
            System.Console.WriteLine("Mutation errors:");
            foreach (var error in errors) {
                System.Console.WriteLine($"  [{error.Code}] {error.Message} on node {error.Node}");
            }
        }

        return domain;
    }
}