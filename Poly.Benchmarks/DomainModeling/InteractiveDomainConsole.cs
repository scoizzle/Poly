using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Benchmarks.DomainModeling;

internal static class InteractiveDomainConsole {
    public static void Run(string domainName) {
        var domain = CreateDomain(domainName);

        Console.WriteLine("Interactive Domain Modeling");
        Console.WriteLine("Type a number to choose an action.");
        Console.WriteLine();

        var shouldExit = false;
        while (!shouldExit) {
            Console.WriteLine($"Current Domain: {domain.Name}");
            var option = PromptMenu(
                "Main Menu",
                [
                    "Guided SupportCase setup",
                    "Add primitive",
                    "Add entity",
                    "Manage entity",
                    "Add relationship",
                    "Manage relationship",
                    "Print ASCII domain",
                    "Exit"
                ]);

            try {
                switch (option) {
                    case 1:
                        domain = RunGuidedSupportCaseSetup();
                        break;
                    case 2:
                        AddPrimitive(domain);
                        break;
                    case 3:
                        AddEntity(domain);
                        break;
                    case 4:
                        ManageEntity(domain);
                        break;
                    case 5:
                        AddRelationship(domain);
                        break;
                    case 6:
                        ManageRelationship(domain);
                        break;
                    case 7:
                        Console.WriteLine();
                        Console.WriteLine(AsciiDomainRenderer.Render(domain));
                        break;
                    case 8:
                        shouldExit = true;
                        break;
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine();
        }
    }

    private static Domain CreateDomain(string domainName) {
        var domain = (Domain?)Activator.CreateInstance(
            typeof(Domain),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new List<IDomainType>(), new List<Relationship>()],
            culture: null);

        if (domain is null) {
            throw new InvalidOperationException("Failed to construct domain instance.");
        }

        domain.Name = domainName;
        return domain;
    }

    private static Domain RunGuidedSupportCaseSetup() {
        Console.WriteLine();
        Console.WriteLine("Guided SupportCase Setup");
        Console.WriteLine("This will create a fresh domain pre-populated with the full SupportCase model.");

        var useDefaultName = PromptYesNo("Use default domain name 'Support Case Management'?");
        var domainName = useDefaultName ? "Support Case Management" : PromptRequiredString("Enter domain name");
        var domain = CreateDomain(domainName);

        Step("Create primitive types", () => {
            domain.AddType(new Primitive { Domain = domain, Name = "string", Category = TypeCategory.Text });
            domain.AddType(new Primitive { Domain = domain, Name = "int", Category = TypeCategory.Integer });
            domain.AddType(new Primitive { Domain = domain, Name = "bool", Category = TypeCategory.Primitive });
            domain.AddType(new Primitive { Domain = domain, Name = "instant", Category = TypeCategory.Instant });
        });

        Step("Create entities and inheritance", () => {
            var user = new Entity(domain, "User");
            user.AddProperty(new Property(domain, "Name", domain.RequirePrimitive("string")));
            user.AddProperty(new Property(domain, "Email", domain.RequirePrimitive("string")));
            domain.AddType(user);

            domain.AddType(new Entity(domain, "Customer", user));
            domain.AddType(new Entity(domain, "Agent", user));

            var supportCase = new Entity(domain, "SupportCase");
            supportCase.AddProperty(new Property(domain, "Title", domain.RequirePrimitive("string")));
            supportCase.AddProperty(new Property(domain, "Priority", domain.RequirePrimitive("int")));
            supportCase.AddProperty(new Property(domain, "IsEscalated", domain.RequirePrimitive("bool")));
            domain.AddType(supportCase);

            var note = new Entity(domain, "Note", supportCase);
            note.AddProperty(new Property(domain, "Content", domain.RequirePrimitive("string")));
            note.AddProperty(new Property(domain, "Author", user));
            domain.AddType(note);
        });

        Step("Create SupportCase stages", () => {
            var supportCase = domain.RequireEntity("SupportCase");
            var newStage = new Stage { Domain = domain, Name = "New" };
            var inProgressStage = new Stage { Domain = domain, Name = "InProgress" };
            var assignedStage = new Stage { Domain = domain, Name = "Assigned", Parent = inProgressStage };
            var resolvedStage = new Stage { Domain = domain, Name = "Resolved" };

            supportCase.AddStage(newStage);
            supportCase.AddStage(inProgressStage);
            supportCase.AddStage(assignedStage);
            supportCase.AddStage(resolvedStage);

            var note = domain.RequireEntity("Note");
            note.AddStage(new Stage {
                Domain = domain,
                Name = "Draft",
                Parent = inProgressStage
            });
        });

        Step("Create SupportCase events", () => {
            var supportCase = domain.RequireEntity("SupportCase");
            var agent = domain.RequireEntity("Agent");
            var stringType = domain.RequirePrimitive("string");

            var caseAssigned = new Event { Domain = domain, Name = "CaseAssigned" };
            caseAssigned.AddProperty(new Property(domain, "AssignedTo", agent));

            var caseResolved = new Event { Domain = domain, Name = "CaseResolved" };
            caseResolved.AddProperty(new Property(domain, "ResolutionSummary", stringType));

            supportCase.AddEvent(caseAssigned);
            supportCase.AddEvent(caseResolved);

            domain.AddType(caseAssigned);
            domain.AddType(caseResolved);
        });

        Step("Create SupportCase actions and effects", () => {
            var supportCase = domain.RequireEntity("SupportCase");
            var newStage = supportCase.RequireStage("New");
            var inProgressStage = supportCase.RequireStage("InProgress");
            var assignedStage = supportCase.RequireStage("Assigned");
            var resolvedStage = supportCase.RequireStage("Resolved");
            var note = domain.RequireEntity("Note");
            var agent = domain.RequireEntity("Agent");
            var stringType = domain.RequirePrimitive("string");

            var assignAction = new DomainAction { Domain = domain, Entity = supportCase, Name = "Assign" };
            var assignAgentParameter = new Property(domain, "Agent", agent);
            assignAction.AddParameter(assignAgentParameter);
            assignAction.AddEffect(new StageTransition { TargetStage = assignedStage });

            var publishAssigned = new PublishEvent { Event = supportCase.RequireEvent("CaseAssigned") };
            publishAssigned.BindProperty(publishAssigned.Event.RequireProperty("AssignedTo"), assignAgentParameter);
            assignAction.AddEffect(publishAssigned);

            supportCase.AddAction(assignAction);
            newStage.AddAction(assignAction);

            var addNoteAction = new DomainAction { Domain = domain, Entity = supportCase, Name = "AddNote" };
            addNoteAction.AddParameter(new Property(domain, "NoteText", stringType));
            addNoteAction.AddEffect(new CreateEntityInstance {
                EntityType = note,
                InitialStage = note.RequireStage("Draft")
            });

            supportCase.AddAction(addNoteAction);
            inProgressStage.AddAction(addNoteAction);

            var resolveAction = new DomainAction { Domain = domain, Entity = supportCase, Name = "Resolve" };
            var resolutionSummaryParameter = new Property(domain, "ResolutionSummary", stringType);
            resolveAction.AddParameter(resolutionSummaryParameter);
            resolveAction.AddEffect(new StageTransition { TargetStage = resolvedStage });

            var publishResolved = new PublishEvent { Event = supportCase.RequireEvent("CaseResolved") };
            publishResolved.BindProperty(publishResolved.Event.RequireProperty("ResolutionSummary"), resolutionSummaryParameter);
            resolveAction.AddEffect(publishResolved);

            supportCase.AddAction(resolveAction);
            inProgressStage.AddAction(resolveAction);
        });

        Step("Create SupportCase and relationship policies", () => {
            var supportCase = domain.RequireEntity("SupportCase");
            var note = domain.RequireEntity("Note");

            var requireTitle = new Policy {
                Domain = domain,
                Name = "RequireTitle",
                AggregationStrategy = PolicyAggregationStrategy.All
            };
            requireTitle.AddRule(new PropertyRule {
                Value = supportCase.RequireProperty("Title"),
                Constraints = new RequiredConstraint()
            });
            supportCase.AddPolicy(requireTitle);

            var createNotesPolicy = new Policy {
                Domain = domain,
                Name = "OnlyAgentsCanCreateUserNotes",
                AggregationStrategy = PolicyAggregationStrategy.All
            };
            createNotesPolicy.AddRule(new PropertyRule {
                Value = note.RequireProperty("Author"),
                Constraints = new RequiredConstraint()
            });

            var viewNotesPolicy = new Policy {
                Domain = domain,
                Name = "OnlyAgentsCanViewUserNotes",
                AggregationStrategy = PolicyAggregationStrategy.All
            };
            viewNotesPolicy.AddRule(new PropertyRule {
                Value = note.RequireProperty("Author"),
                Constraints = new RequiredConstraint()
            });

            var customerNotes = new Relationship(domain, "CustomerNotes") {
                Source = domain.RequireEntity("Customer"),
                Target = note,
                Cardinality = RelationshipCardinality.OneToMany,
                SourceOwnsTarget = false
            };
            customerNotes.AddPolicy(createNotesPolicy);
            customerNotes.AddPolicy(viewNotesPolicy);
            domain.AddRelationship(customerNotes);
            domain.RequireEntity("Customer").AddRelationship(customerNotes);
        });

        Step("Create SupportCase relationships", () => {
            var customer = domain.RequireEntity("Customer");
            var agent = domain.RequireEntity("Agent");
            var supportCase = domain.RequireEntity("SupportCase");
            var note = domain.RequireEntity("Note");
            var instant = domain.RequirePrimitive("instant");

            var customerCases = new Relationship(domain, "CustomerCases") {
                Source = customer,
                Target = supportCase,
                Cardinality = RelationshipCardinality.OneToMany,
                SourceOwnsTarget = true
            };
            domain.AddRelationship(customerCases);
            customer.AddRelationship(customerCases);

            var supportCaseNotes = new Relationship(domain, "SupportCaseNotes") {
                Source = supportCase,
                Target = note,
                Cardinality = RelationshipCardinality.OneToMany,
                SourceOwnsTarget = true
            };
            domain.AddRelationship(supportCaseNotes);
            supportCase.AddRelationship(supportCaseNotes);

            var agentCases = new Relationship(domain, "AgentSupportCases") {
                Source = agent,
                Target = supportCase,
                Cardinality = RelationshipCardinality.ManyToMany,
                SourceOwnsTarget = false
            };

            var active = new Stage { Domain = domain, Name = "Active" };
            var inactive = new Stage { Domain = domain, Name = "Inactive" };
            agentCases.AddStage(active);
            agentCases.AddStage(inactive);

            var assignedAt = new Property(domain, "AssignedAt", instant);
            assignedAt.AddConstraint(new RequiredConstraint());
            var unassignedAt = new Property(domain, "UnassignedAt", instant);

            agentCases.AddProperty(assignedAt);
            agentCases.AddProperty(unassignedAt);

            var requireAssignedAt = new Policy { Domain = domain, Name = "RequireAssignedAtWhenActive" };
            requireAssignedAt.AddRule(new PropertyRule {
                Value = assignedAt,
                Constraints = new RequiredConstraint()
            });
            active.AddPolicy(requireAssignedAt);

            var requireUnassignedAt = new Policy { Domain = domain, Name = "RequireUnassignedAtWhenInactive" };
            requireUnassignedAt.AddRule(new PropertyRule {
                Value = unassignedAt,
                Constraints = new RequiredConstraint()
            });
            inactive.AddPolicy(requireUnassignedAt);

            domain.AddRelationship(agentCases);
            agent.AddRelationship(agentCases);
        });

        Console.WriteLine();
        Console.WriteLine("SupportCase setup complete.");
        Console.WriteLine(AsciiDomainRenderer.Render(domain));

        return domain;
    }

    private static void Step(string title, System.Action action) {
        Console.WriteLine();
        Console.WriteLine($"[Step] {title}");
        Console.WriteLine("Press Enter to apply this step...");
        _ = Console.ReadLine();
        action();
        Console.WriteLine("Done.");
    }

    private static void AddPrimitive(Domain domain) {
        var name = PromptRequiredString("Primitive name");
        var category = PromptEnum<TypeCategory>("Primitive category");

        var primitive = new Primitive {
            Domain = domain,
            Name = name,
            Category = category
        };

        domain.AddType(primitive);
        Console.WriteLine($"Added primitive '{name}'.");
    }

    private static void AddEntity(Domain domain) {
        var name = PromptRequiredString("Entity name");
        var parent = ChooseOptional("Choose parent entity (optional)", domain.GetAvailableEntities().Where(entity => entity is not Relationship).OrderBy(entity => entity.Name).ToArray());

        var entity = new Entity(domain, name, parent);
        domain.AddType(entity);
        Console.WriteLine($"Added entity '{name}'.");
    }

    private static void ManageEntity(Domain domain) {
        var entity = ChooseRequired("Choose entity", domain.GetAvailableEntities().Where(candidate => candidate is not Relationship).OrderBy(candidate => candidate.Name).ToArray());

        var done = false;
        while (!done) {
            Console.WriteLine();
            Console.WriteLine($"Managing entity: {entity.Name}");
            var option = PromptMenu(
                "Entity Menu",
                [
                    "Add property",
                    "Add stage",
                    "Manage stage",
                    "Add event",
                    "Manage event",
                    "Add action",
                    "Manage action",
                    "Add required-property policy",
                    "Show entity summary",
                    "Back"
                ]);

            switch (option) {
                case 1:
                    AddPropertyToEntity(domain, entity);
                    break;
                case 2:
                    AddStageToEntity(entity);
                    break;
                case 3:
                    ManageEntityStage(domain, entity);
                    break;
                case 4:
                    AddEventToEntity(domain, entity);
                    break;
                case 5:
                    ManageEvent(domain, entity);
                    break;
                case 6:
                    AddActionToEntity(entity);
                    break;
                case 7:
                    ManageAction(domain, entity);
                    break;
                case 8:
                    AddRequiredPolicy(entity.Domain, entity, entity.Properties);
                    break;
                case 9:
                    Console.WriteLine(AsciiDomainRenderer.RenderEntitySummary(entity));
                    break;
                case 10:
                    done = true;
                    break;
            }
        }
    }

    private static void AddPropertyToEntity(Domain domain, Entity entity) {
        var name = PromptRequiredString("Property name");
        var type = ChooseRequired<IDomainType>("Choose property type", domain.Types.OrderBy(candidate => candidate.Name).ToArray());
        var property = new Property(domain, name, type);
        entity.AddProperty(property);

        if (PromptYesNo("Add RequiredConstraint to this property?")) {
            property.AddConstraint(new RequiredConstraint());
        }

        Console.WriteLine($"Added property '{name}' to entity '{entity.Name}'.");
    }

    private static void AddStageToEntity(Entity entity) {
        var name = PromptRequiredString("Stage name");
        var parentCandidates = new List<Stage>();
        parentCandidates.AddRange(entity.Stages);
        if (entity.ParentEntity is not null) {
            parentCandidates.AddRange(entity.ParentEntity.Stages);
        }

        var parent = ChooseOptional("Choose parent stage (optional)", parentCandidates.DistinctBy(stage => stage.Name).OrderBy(stage => stage.Name).ToArray());

        var stage = new Stage {
            Domain = entity.Domain,
            Name = name,
            Parent = parent
        };

        entity.AddStage(stage);
        Console.WriteLine($"Added stage '{name}' to entity '{entity.Name}'.");
    }

    private static void ManageEntityStage(Domain domain, Entity entity) {
        var stage = ChooseRequired("Choose stage", entity.Stages.OrderBy(candidate => candidate.Name).ToArray());
        var done = false;

        while (!done) {
            Console.WriteLine();
            Console.WriteLine($"Managing stage: {stage.Name}");
            var option = PromptMenu(
                "Entity Stage Menu",
                [
                    "Attach existing action",
                    "Create action and attach",
                    "Add required-property policy",
                    "Show stage summary",
                    "Back"
                ]);

            switch (option) {
                case 1:
                    AttachExistingActionToStage(entity, stage);
                    break;
                case 2:
                    CreateActionForStage(domain, entity, stage);
                    break;
                case 3:
                    AddRequiredPolicy(domain, stage, entity.Properties);
                    break;
                case 4:
                    Console.WriteLine(AsciiDomainRenderer.RenderStageSummary(stage));
                    break;
                case 5:
                    done = true;
                    break;
            }
        }
    }

    private static void AddEventToEntity(Domain domain, Entity entity) {
        var eventName = PromptRequiredString("Event name");
        var @event = new Event {
            Domain = domain,
            Name = eventName
        };

        entity.AddEvent(@event);
        domain.AddType(@event);

        Console.WriteLine($"Added event '{eventName}' to entity '{entity.Name}'.");
    }

    private static void ManageEvent(Domain domain, Entity entity) {
        var @event = ChooseRequired("Choose event", entity.Events.OrderBy(candidate => candidate.Name).ToArray());
        var done = false;

        while (!done) {
            Console.WriteLine();
            Console.WriteLine($"Managing event: {@event.Name}");
            var option = PromptMenu(
                "Event Menu",
                [
                    "Add property",
                    "Back"
                ]);

            switch (option) {
                case 1:
                    var propertyName = PromptRequiredString("Event property name");
                    var propertyType = ChooseRequired<IDomainType>("Choose event property type", domain.Types.OrderBy(candidate => candidate.Name).ToArray());
                    @event.AddProperty(new Property(domain, propertyName, propertyType));
                    Console.WriteLine($"Added property '{propertyName}' to event '{@event.Name}'.");
                    break;
                case 2:
                    done = true;
                    break;
            }
        }
    }

    private static void AddActionToEntity(Entity entity) {
        var actionName = PromptRequiredString("Action name");
        var action = new DomainAction {
            Domain = entity.Domain,
            Entity = entity,
            Name = actionName
        };

        entity.AddAction(action);

        if (PromptYesNo("Attach action to a stage now?")) {
            var stage = ChooseRequired("Choose stage", entity.Stages.OrderBy(candidate => candidate.Name).ToArray());
            stage.AddAction(action);
        }

        Console.WriteLine($"Added action '{actionName}' to entity '{entity.Name}'.");
    }

    private static void AttachExistingActionToStage(Entity entity, Stage stage) {
        var availableActions = entity.Actions
            .Where(action => !stage.Actions.Contains(action))
            .OrderBy(action => action.Name)
            .ToArray();

        var action = ChooseRequired("Choose action to attach", availableActions);
        stage.AddAction(action);
        Console.WriteLine($"Attached action '{action.Name}' to stage '{stage.Name}'.");
    }

    private static void CreateActionForStage(Domain domain, Entity entity, Stage stage) {
        var actionName = PromptRequiredString("Action name");
        var action = new DomainAction {
            Domain = domain,
            Entity = entity,
            Name = actionName
        };

        entity.AddAction(action);
        stage.AddAction(action);
        Console.WriteLine($"Created and attached action '{action.Name}' to stage '{stage.Name}'.");
    }

    private static void ManageAction(Domain domain, Entity entity) {
        var action = ChooseRequired("Choose action", entity.Actions.OrderBy(candidate => candidate.Name).ToArray());
        var done = false;

        while (!done) {
            Console.WriteLine();
            Console.WriteLine($"Managing action: {action.Name}");
            var option = PromptMenu(
                "Action Menu",
                [
                    "Add parameter",
                    "Add stage transition effect",
                    "Add publish event effect",
                    "Add create entity instance effect",
                    "Add invoke action effect",
                    "Back"
                ]);

            switch (option) {
                case 1:
                    AddActionParameter(domain, action);
                    break;
                case 2:
                    AddStageTransitionEffect(entity, action);
                    break;
                case 3:
                    AddPublishEventEffect(entity, action);
                    break;
                case 4:
                    AddCreateEntityInstanceEffect(domain, action);
                    break;
                case 5:
                    AddInvokeActionEffect(entity, action);
                    break;
                case 6:
                    done = true;
                    break;
            }
        }
    }

    private static void AddActionParameter(Domain domain, DomainAction action) {
        var name = PromptRequiredString("Parameter name");
        var type = ChooseRequired<IDomainType>("Choose parameter type", domain.Types.OrderBy(candidate => candidate.Name).ToArray());
        action.AddParameter(new Property(domain, name, type));
        Console.WriteLine($"Added parameter '{name}' to action '{action.Name}'.");
    }

    private static void AddStageTransitionEffect(Entity entity, DomainAction action) {
        var target = ChooseRequired("Choose target stage", entity.Stages.OrderBy(candidate => candidate.Name).ToArray());
        action.AddEffect(new StageTransition {
            TargetStage = target
        });

        Console.WriteLine($"Added stage transition to '{target.Name}'.");
    }

    private static void AddPublishEventEffect(Entity entity, DomainAction action) {
        var @event = ChooseRequired("Choose event to publish", entity.Events.OrderBy(candidate => candidate.Name).ToArray());
        var effect = new PublishEvent {
            Event = @event
        };

        foreach (var eventProperty in @event.Properties.OrderBy(property => property.Name)) {
            var candidates = GetBindableValues(action)
                .Where(value => ReferenceEquals(value.Type, eventProperty.Type))
                .ToArray();

            if (candidates.Length == 0) {
                throw new InvalidOperationException(
                    $"No available parameter/property can bind event property '{eventProperty.Name}' ({eventProperty.Type.Name}).");
            }

            var source = ChooseRequired($"Choose binding source for event property '{eventProperty.Name}'", candidates);
            effect.BindProperty(eventProperty, source);
        }

        action.AddEffect(effect);
        Console.WriteLine($"Added publish event effect for '{@event.Name}'.");
    }

    private static void AddCreateEntityInstanceEffect(Domain domain, DomainAction action) {
        var entityType = ChooseRequired("Choose entity type to create", domain.GetAvailableEntities().Where(entity => entity is not Relationship).OrderBy(entity => entity.Name).ToArray());
        var initialStage = entityType.Stages.Count == 0
            ? null
            : ChooseOptional("Choose initial stage (optional)", entityType.Stages.OrderBy(stage => stage.Name).ToArray());

        action.AddEffect(new CreateEntityInstance {
            EntityType = entityType,
            InitialStage = initialStage
        });

        Console.WriteLine($"Added create entity instance effect for '{entityType.Name}'.");
    }

    private static void AddInvokeActionEffect(Entity entity, DomainAction action) {
        var targetAction = ChooseRequired("Choose target action", entity.Actions.OrderBy(candidate => candidate.Name).ToArray());
        var effect = new InvokeAction {
            TargetAction = targetAction
        };

        foreach (var targetParameter in targetAction.Parameters.OfType<Property>().OrderBy(parameter => parameter.Name)) {
            var candidates = GetBindableValues(action)
                .Where(value => ReferenceEquals(value.Type, targetParameter.Type))
                .ToArray();

            if (candidates.Length == 0) {
                throw new InvalidOperationException(
                    $"No available parameter/property can bind target action parameter '{targetParameter.Name}'.");
            }

            var source = ChooseRequired($"Choose binding source for action parameter '{targetParameter.Name}'", candidates);
            effect.BindParameter(targetParameter, source);
        }

        action.AddEffect(effect);
        Console.WriteLine($"Added invoke action effect for '{targetAction.Name}'.");
    }

    private static IEnumerable<IDomainValue> GetBindableValues(DomainAction action) {
        foreach (var parameter in action.Parameters) {
            yield return parameter;
        }

        foreach (var property in action.Entity.Properties) {
            yield return property;
        }
    }

    private static void AddRelationship(Domain domain) {
        var name = PromptRequiredString("Relationship name");
        var source = ChooseRequired<IDomainType>("Choose relationship source type", domain.Types.OrderBy(candidate => candidate.Name).ToArray());
        var target = ChooseRequired<IDomainType>("Choose relationship target type", domain.Types.OrderBy(candidate => candidate.Name).ToArray());
        var cardinality = PromptEnum<RelationshipCardinality>("Relationship cardinality");
        var sourceOwnsTarget = PromptYesNo("Does source own target?");

        var relationship = new Relationship(domain, name) {
            Source = source,
            Target = target,
            Cardinality = cardinality,
            SourceOwnsTarget = sourceOwnsTarget
        };

        domain.AddRelationship(relationship);
        if (source is Entity sourceEntity) {
            sourceEntity.AddRelationship(relationship);
        }

        Console.WriteLine($"Added relationship '{name}'.");
    }

    private static void ManageRelationship(Domain domain) {
        var relationship = ChooseRequired("Choose relationship", domain.Relationships.OrderBy(candidate => candidate.Name).ToArray());
        var done = false;

        while (!done) {
            Console.WriteLine();
            Console.WriteLine($"Managing relationship: {relationship.Name}");
            var option = PromptMenu(
                "Relationship Menu",
                [
                    "Add property",
                    "Add stage",
                    "Add required-property policy",
                    "Manage stage",
                    "Back"
                ]);

            switch (option) {
                case 1:
                    AddPropertyToEntity(domain, relationship);
                    break;
                case 2:
                    AddStageToRelationship(relationship);
                    break;
                case 3:
                    AddRequiredPolicy(domain, relationship, relationship.Properties);
                    break;
                case 4:
                    ManageRelationshipStage(domain, relationship);
                    break;
                case 5:
                    done = true;
                    break;
            }
        }
    }

    private static void AddStageToRelationship(Relationship relationship) {
        var stageName = PromptRequiredString("Relationship stage name");
        var parent = ChooseOptional("Choose parent stage (optional)", relationship.Stages.OrderBy(stage => stage.Name).ToArray());

        relationship.AddStage(new Stage {
            Domain = relationship.Domain,
            Name = stageName,
            Parent = parent
        });

        Console.WriteLine($"Added stage '{stageName}' to relationship '{relationship.Name}'.");
    }

    private static void ManageRelationshipStage(Domain domain, Relationship relationship) {
        var stage = ChooseRequired("Choose relationship stage", relationship.Stages.OrderBy(candidate => candidate.Name).ToArray());
        var done = false;

        while (!done) {
            var option = PromptMenu("Relationship Stage Menu", ["Add required-property policy", "Show stage summary", "Back"]);

            switch (option) {
                case 1:
                    AddRequiredPolicy(domain, stage, relationship.Properties);
                    break;
                case 2:
                    Console.WriteLine(AsciiDomainRenderer.RenderStageSummary(stage));
                    break;
                case 3:
                    done = true;
                    break;
            }
        }
    }

    private static void AddRequiredPolicy(Domain domain, Entity owner, IEnumerable<Property> availableProperties) {
        var policyName = PromptRequiredString("Policy name");
        var property = ChooseRequired("Choose target property", availableProperties.OrderBy(candidate => candidate.Name).ToArray());

        var policy = new Policy {
            Domain = domain,
            Name = policyName,
            AggregationStrategy = PolicyAggregationStrategy.All
        };

        policy.AddRule(new PropertyRule {
            Value = property,
            Constraints = new RequiredConstraint()
        });

        owner.AddPolicy(policy);
        Console.WriteLine($"Added policy '{policyName}' to '{owner.Name}'.");
    }

    private static void AddRequiredPolicy(Domain domain, Stage owner, IEnumerable<Property> availableProperties) {
        var policyName = PromptRequiredString("Policy name");
        var property = ChooseRequired("Choose target property", availableProperties.OrderBy(candidate => candidate.Name).ToArray());

        var policy = new Policy {
            Domain = domain,
            Name = policyName,
            AggregationStrategy = PolicyAggregationStrategy.All
        };

        policy.AddRule(new PropertyRule {
            Value = property,
            Constraints = new RequiredConstraint()
        });

        owner.AddPolicy(policy);
        Console.WriteLine($"Added policy '{policyName}' to stage '{owner.Name}'.");
    }

    private static int PromptMenu(string title, IReadOnlyList<string> options) {
        Console.WriteLine();
        Console.WriteLine(title);
        for (var i = 0; i < options.Count; i++) {
            Console.WriteLine($"  {i + 1}. {options[i]}");
        }

        while (true) {
            Console.Write("Select option: ");
            var input = Console.ReadLine();
            if (int.TryParse(input, out var parsed) && parsed >= 1 && parsed <= options.Count) {
                return parsed;
            }

            Console.WriteLine("Invalid selection. Try again.");
        }
    }

    private static string PromptRequiredString(string prompt) {
        while (true) {
            Console.Write($"{prompt}: ");
            var value = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }

            Console.WriteLine("Value is required.");
        }
    }

    private static bool PromptYesNo(string prompt) {
        while (true) {
            Console.Write($"{prompt} [y/n]: ");
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (input is "y" or "yes") {
                return true;
            }

            if (input is "n" or "no") {
                return false;
            }

            Console.WriteLine("Please answer y or n.");
        }
    }

    private static T PromptEnum<T>(string prompt) where T : struct, Enum {
        var values = Enum.GetValues<T>();
        Console.WriteLine(prompt);
        for (var i = 0; i < values.Length; i++) {
            Console.WriteLine($"  {i + 1}. {values[i]}");
        }

        while (true) {
            Console.Write("Select option: ");
            var input = Console.ReadLine();
            if (int.TryParse(input, out var parsed) && parsed >= 1 && parsed <= values.Length) {
                return values[parsed - 1];
            }

            Console.WriteLine("Invalid selection. Try again.");
        }
    }

    private static T ChooseRequired<T>(string prompt, IReadOnlyList<T> values) {
        if (values.Count == 0) {
            throw new InvalidOperationException($"No values available for: {prompt}");
        }

        Console.WriteLine(prompt);
        for (var i = 0; i < values.Count; i++) {
            Console.WriteLine($"  {i + 1}. {DescribeValue(values[i])}");
        }

        while (true) {
            Console.Write("Select option: ");
            var input = Console.ReadLine();
            if (int.TryParse(input, out var parsed) && parsed >= 1 && parsed <= values.Count) {
                return values[parsed - 1];
            }

            Console.WriteLine("Invalid selection. Try again.");
        }
    }

    private static T? ChooseOptional<T>(string prompt, IReadOnlyList<T> values) where T : class {
        if (values.Count == 0) {
            return null;
        }

        Console.WriteLine(prompt);
        Console.WriteLine("  0. (none)");

        for (var i = 0; i < values.Count; i++) {
            Console.WriteLine($"  {i + 1}. {DescribeValue(values[i])}");
        }

        while (true) {
            Console.Write("Select option: ");
            var input = Console.ReadLine();
            if (int.TryParse(input, out var parsed)) {
                if (parsed == 0) {
                    return null;
                }

                if (parsed >= 1 && parsed <= values.Count) {
                    return values[parsed - 1];
                }
            }

            Console.WriteLine("Invalid selection. Try again.");
        }
    }

    private static string DescribeValue<T>(T value) {
        return value switch {
            null => "(null)",
            IDomainType domainType => domainType.Name,
            Property property => $"{property.Name}:{property.Type.Name}",
            DomainAction action => action.Name,
            Stage stage => stage.Name,
            Policy policy => policy.Name,
            _ => value.ToString() ?? typeof(T).Name
        };
    }
}