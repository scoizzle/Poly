using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling;

public sealed class Entity {
    private readonly List<Property> _properties = [];
    private readonly List<Stage> _stages = [];
    private readonly List<Rule> _rules = [];
    private readonly List<Action> _actions = [];
    private readonly List<Event> _events = [];

    public string Name { get; set; } = string.Empty;

    public IReadOnlyCollection<Property> Properties => _properties;
    public IReadOnlyCollection<Stage> Stages => _stages;
    public IReadOnlyCollection<Rule> Rules => _rules;
    public IReadOnlyCollection<Action> Actions => _actions;
    public IReadOnlyCollection<Event> Events => _events;

    public void AddProperty(Property property) => _properties.Add(property);
    public void AddStage(Stage stage) => _stages.Add(stage);
    public void AddRule(Rule rule) => _rules.Add(rule);
    public void AddAction(Action action) => _actions.Add(action);
    public void AddEvent(Event @event) => _events.Add(@event);

    public void Validate() {
        if (string.IsNullOrWhiteSpace(Name)) {
            throw new InvalidOperationException("Entity name is required.");
        }

        EnsureUniqueNames(_properties, p => p.Name, "property");
        EnsureUniqueNames(_stages, s => s.Name, "stage");
        EnsureUniqueNames(_actions, a => a.Name, "action");
        EnsureUniqueNames(_events, e => e.Name, "event");

        var propertyNames = new HashSet<string>(_properties.Select(p => p.Name), StringComparer.Ordinal);
        foreach (var rule in _rules) {
            var member = rule.Member.Property;
            if (!propertyNames.Contains(member.Name)) {
                throw new InvalidOperationException($"Rule member '{member.Name}' is not defined on entity '{Name}'.");
            }
        }

        var actions = new HashSet<Action>(_actions);
        var events = new HashSet<Event>(_events);
        var rules = new HashSet<Rule>(_rules);

        foreach (var stage in _stages) {
            ValidateStageHierarchy(stage);
        }

        foreach (var stage in _stages) {
            if (string.IsNullOrWhiteSpace(stage.Name)) {
                throw new InvalidOperationException("Stage name is required.");
            }

            foreach (var action in stage.Actions) {
                if (!actions.Contains(action)) {
                    throw new InvalidOperationException($"Stage '{stage.Name}' references an action not registered on entity '{Name}'.");
                }
            }

            foreach (var @event in stage.EntryEvents) {
                if (!events.Contains(@event)) {
                    throw new InvalidOperationException($"Stage '{stage.Name}' references an entry event not registered on entity '{Name}'.");
                }
            }

            foreach (var @event in stage.ExitEvents) {
                if (!events.Contains(@event)) {
                    throw new InvalidOperationException($"Stage '{stage.Name}' references an exit event not registered on entity '{Name}'.");
                }
            }

            foreach (var rule in stage.Rules) {
                if (!rules.Contains(rule)) {
                    throw new InvalidOperationException($"Stage '{stage.Name}' references a rule not registered on entity '{Name}'.");
                }
            }

            var effectiveActionNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var effectiveAction in stage.GetEffectiveActions()) {
                if (!actions.Contains(effectiveAction)) {
                    throw new InvalidOperationException($"Stage '{stage.Name}' inherits an action not registered on entity '{Name}'.");
                }

                if (!effectiveActionNames.Add(effectiveAction.Name)) {
                    throw new InvalidOperationException($"Stage '{stage.Name}' has ambiguous inherited action '{effectiveAction.Name}'.");
                }
            }
        }

        foreach (var action in _actions) {
            foreach (var parameter in action.Parameters) {
                ValidateValueSource(parameter, propertyNames, $"Action '{action.Name}' parameter", requireRegisteredProperty: false);
            }

            foreach (var effect in action.Effects.OfType<SetPropertyMutation>()) {
                ValidateSetPropertyMutation(effect, propertyNames, action.Name);
            }
        }

        foreach (var @event in _events) {
            foreach (var property in @event.Properties) {
                ValidateValueSource(property, propertyNames, $"Event '{@event.Name}' property", requireRegisteredProperty: false);
            }
        }
    }

    private static void EnsureUniqueNames<T>(IEnumerable<T> values, Func<T, string> selector, string label) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values) {
            var name = selector(value);
            if (string.IsNullOrWhiteSpace(name)) {
                throw new InvalidOperationException($"{label} name is required.");
            }

            if (!seen.Add(name)) {
                throw new InvalidOperationException($"Duplicate {label} name '{name}'.");
            }
        }
    }

    private static void ValidateStageHierarchy(Stage stage) {
        var visited = new HashSet<Stage>();
        var current = stage;

        while (current is not null) {
            if (!visited.Add(current)) {
                throw new InvalidOperationException($"Stage hierarchy cycle detected at '{current.Name}'.");
            }

            current = current.SuperStage;
        }

        if (stage.SuperStage is not null && !stage.SuperStage.SubStages.Contains(stage)) {
            throw new InvalidOperationException($"Stage '{stage.Name}' declares super stage '{stage.SuperStage.Name}', but the parent does not list it as a substage.");
        }

        foreach (var subStage in stage.SubStages) {
            if (!ReferenceEquals(subStage.SuperStage, stage)) {
                throw new InvalidOperationException($"Stage '{stage.Name}' lists substage '{subStage.Name}', but the child does not reference it as its super stage.");
            }
        }
    }

    private static void ValidateSetPropertyMutation(SetPropertyMutation mutation, HashSet<string> propertyNames, string actionName) {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(mutation.Target);
        ArgumentNullException.ThrowIfNull(mutation.Value);

        var target = mutation.Target.Property;
        if (!propertyNames.Contains(target.Name)) {
            throw new InvalidOperationException($"Action '{actionName}' mutation target '{target.Name}' is not defined on entity.");
        }

        ValidateValueSource(mutation.Value, propertyNames, $"Action '{actionName}' mutation value", requireRegisteredProperty: false);
        var valueTypeCategory = GetValueTypeCategory(mutation.Value);
        if (!AreTypeCategoriesCompatible(target.TypeCategory, valueTypeCategory)) {
            throw new InvalidOperationException($"Action '{actionName}' mutation value type is not compatible with target '{target.Name}'.");
        }
    }

    private static void ValidateValueSource(ValueSource source, HashSet<string> propertyNames, string context, bool requireRegisteredProperty = true) {
        ArgumentNullException.ThrowIfNull(source);

        if (source is PropertyValueSource propertySource) {
            ArgumentNullException.ThrowIfNull(propertySource.Property);
            if (requireRegisteredProperty && !propertyNames.Contains(propertySource.Property.Name)) {
                throw new InvalidOperationException($"{context} references property '{propertySource.Property.Name}' that is not defined on entity.");
            }

            return;
        }

        if (source is LiteralValueSource) {
            return;
        }

        throw new InvalidOperationException($"{context} uses unsupported source type '{source.GetType().Name}'.");
    }

    private static TypeCategory GetValueTypeCategory(ValueSource source) {
        return source switch {
            PropertyValueSource propertySource => propertySource.Property.TypeCategory,
            LiteralValueSource literal => GetLiteralTypeCategory(literal.Value),
            _ => TypeCategory.None
        };
    }

    private static TypeCategory GetLiteralTypeCategory(object? value) {
        return value switch {
            null => TypeCategory.Nullable,
            string => TypeCategory.Text,
            char => TypeCategory.Text,
            byte => TypeCategory.Integer | TypeCategory.Unsigned,
            sbyte => TypeCategory.Integer | TypeCategory.Signed,
            short => TypeCategory.Integer | TypeCategory.Signed,
            ushort => TypeCategory.Integer | TypeCategory.Unsigned,
            int => TypeCategory.Integer | TypeCategory.Signed,
            uint => TypeCategory.Integer | TypeCategory.Unsigned,
            long => TypeCategory.Integer | TypeCategory.Signed,
            ulong => TypeCategory.Integer | TypeCategory.Unsigned,
            float => TypeCategory.FloatingPoint,
            double => TypeCategory.FloatingPoint,
            decimal => TypeCategory.HighPrecision,
            DateTime => TypeCategory.Instant,
            DateTimeOffset => TypeCategory.Instant,
            TimeSpan => TypeCategory.Duration,
            Guid => TypeCategory.Identifier,
            byte[] => TypeCategory.Binary | TypeCategory.Collection,
            _ => TypeCategory.None
        };
    }

    private static bool AreTypeCategoriesCompatible(TypeCategory target, TypeCategory source) {
        if (source == TypeCategory.Nullable) {
            return (target & TypeCategory.Nullable) == TypeCategory.Nullable;
        }

        var normalizedTarget = target & ~TypeCategory.Nullable;
        var normalizedSource = source & ~TypeCategory.Nullable;

        if (normalizedSource == TypeCategory.None) {
            return true;
        }

        if (normalizedTarget == normalizedSource) {
            return true;
        }

        var targetIsNumeric = (normalizedTarget & TypeCategory.Numeric) == TypeCategory.Numeric;
        var sourceIsNumeric = (normalizedSource & TypeCategory.Numeric) == TypeCategory.Numeric;
        if (targetIsNumeric && sourceIsNumeric) {
            return true;
        }

        var targetIsTemporal = (normalizedTarget & TypeCategory.Temporal) == TypeCategory.Temporal;
        var sourceIsTemporal = (normalizedSource & TypeCategory.Temporal) == TypeCategory.Temporal;
        if (targetIsTemporal && sourceIsTemporal) {
            return true;
        }

        return (normalizedTarget & normalizedSource) == normalizedTarget;
    }
}

public sealed class Property {
    private readonly List<Constraint> _constraints = [];

    public string Name { get; set; } = string.Empty;
    public TypeCategory TypeCategory { get; set; }
    public IReadOnlyCollection<Constraint> Constraints => _constraints;

    public void AddConstraint(Constraint constraint) => _constraints.Add(constraint);
}

public abstract class ValueSource;

public sealed class PropertyValueSource : ValueSource {
    public Property Property { get; init; } = null!;
}

public sealed class LiteralValueSource : ValueSource {
    public object? Value { get; init; }
}

public sealed class Stage {

    public string Name { get; set; } = string.Empty;
    public IReadOnlyCollection<Rule> Rules { get; init; } = [];
    public IReadOnlyCollection<Action> Actions { get; init; } = [];
    public IReadOnlyCollection<Event> EntryEvents { get; init; } = [];
    public IReadOnlyCollection<Event> ExitEvents { get; init; } = [];
    public Stage? SuperStage { get; init; }
    public IReadOnlyCollection<Stage> SubStages { get; init; } = [];

    public IEnumerable<Action> GetEffectiveActions() {
        var ancestry = new Stack<Stage>();
        var current = this;
        while (current is not null) {
            ancestry.Push(current);
            current = current.SuperStage;
        }

        while (ancestry.Count > 0) {
            foreach (var action in ancestry.Pop().Actions) {
                yield return action;
            }
        }
    }
}

public abstract class Effect;

public abstract class Mutation : Effect;

public sealed class SetPropertyMutation : Mutation {
    public required PropertyValueSource Target { get; init; }
    public required ValueSource Value { get; init; }
}

public sealed class EmitEvent : Effect {
    public Event Event { get; init; } = null!;
}

public sealed class Action {
    private readonly List<ValueSource> _parameters = [];
    private readonly List<Effect> _effects = [];

    public string Name { get; set; } = string.Empty;
    public IReadOnlyCollection<ValueSource> Parameters => _parameters;
    public IReadOnlyCollection<Effect> Effects => _effects;

    public void AddParameter(ValueSource parameter) => _parameters.Add(parameter);
    public void AddEffect(Effect effect) => _effects.Add(effect);
}

public sealed class Event {
    private readonly List<ValueSource> _properties = [];

    public string Name { get; set; } = string.Empty;
    public IReadOnlyCollection<ValueSource> Properties => _properties;

    public void AddProperty(ValueSource property) => _properties.Add(property);
}

/*
  DEMO MODEL CLASSES - NOT FINALIZED, SUBJECT TO CHANGE
 */

static class Demo {
    static void Test() {
        var idProperty = CreateProperty("Id", TypeCategory.Text, [new RequiredConstraint(), new LengthConstraint(maxLength: 50)]);
        var nameProperty = CreateProperty("Name", TypeCategory.Text, [new RequiredConstraint(), new LengthConstraint(maxLength: 100)]);
        var emailProperty = CreateProperty("Email", TypeCategory.Text, [new LengthConstraint(maxLength: 100)]);

        var draftRule = new DemoRule {
            Member = Source(nameProperty),
            Constraints = new ConstraintSet([new RequiredConstraint(), new LengthConstraint(maxLength: 100)])
        };
        var activeRule = new DemoRule {
            Member = Source(emailProperty),
            Constraints = new ConstraintSet([new LengthConstraint(maxLength: 100)])
        };
        var deprecatedRule = new DemoRule {
            Member = Source(idProperty),
            Constraints = new ConstraintSet([new RequiredConstraint()])
        };

        var createCustomer = CreateEvent(
            "CreateCustomer",
            [
                Source(idProperty),
                Source(nameProperty)
            ]);

        var customerCreated = CreateEvent(
            "CustomerCreated",
            [
                Source(idProperty),
                Source(nameProperty),
                Source(emailProperty)
            ]);

        var draftStage = new Stage {
            Name = "Draft",
            Rules = [draftRule],
            EntryEvents = [],
            ExitEvents = [createCustomer]
        };

        var activeStage = new Stage {
            Name = "Active",
            Rules = [activeRule],
            EntryEvents = [customerCreated],
            ExitEvents = []
        };

        var deprecatedStage = new Stage {
            Name = "Deprecated",
            Rules = [deprecatedRule],
            EntryEvents = [],
            ExitEvents = []
        };

        var model = CreateEntity(
            "Customer",
            [idProperty, nameProperty, emailProperty],
            [draftStage, activeStage, deprecatedStage],
            [draftRule, activeRule, deprecatedRule],
            [],
            [createCustomer, customerCreated]);
    }

    static void Test2() {
        var activeSubStages = new List<Stage>();
        var connectedSubStages = new List<Stage>();

        var callIdProperty = CreateProperty("CallId", TypeCategory.Text, [new RequiredConstraint(), new LengthConstraint(maxLength: 36)]);
        var callerNumberProperty = CreateProperty("CallerNumber", TypeCategory.Text, [new RequiredConstraint(), new LengthConstraint(maxLength: 20)]);
        var recipientNumberProperty = CreateProperty("RecipientNumber", TypeCategory.Text, [new RequiredConstraint(), new LengthConstraint(maxLength: 20)]);
        var startedAtUtcProperty = CreateProperty("StartedAtUtc", TypeCategory.Text, [new LengthConstraint(maxLength: 40)]);
        var holdStartedAtUtcProperty = CreateProperty("HoldStartedAtUtc", TypeCategory.Text, [new LengthConstraint(maxLength: 40)]);
        var resumedAtUtcProperty = CreateProperty("ResumedAtUtc", TypeCategory.Text, [new LengthConstraint(maxLength: 40)]);
        var toneProperty = CreateProperty("Tone", TypeCategory.Text, [new LengthConstraint(maxLength: 1)]);
        var volumeProperty = CreateProperty("Volume", TypeCategory.Numeric, [new RequiredConstraint()]);
        var endedAtUtcProperty = CreateProperty("EndedAtUtc", TypeCategory.Text, [new LengthConstraint(maxLength: 40)]);
        var callDurationProperty = CreateProperty("CallDuration", TypeCategory.Duration, [new RequiredConstraint()]);
        var endReasonProperty = CreateProperty("EndReason", TypeCategory.Text, [new LengthConstraint(maxLength: 40)]);

        var activeCallRule = new DemoRule {
            Member = Source(callIdProperty),
            Constraints = new ConstraintSet([new RequiredConstraint(), new LengthConstraint(maxLength: 36)])
        };
        var activeParticipantsRule = new DemoRule {
            Member = Source(callerNumberProperty),
            Constraints = new ConstraintSet([new RequiredConstraint(), new LengthConstraint(maxLength: 20)])
        };
        var activeRecipientRule = new DemoRule {
            Member = Source(recipientNumberProperty),
            Constraints = new ConstraintSet([new RequiredConstraint(), new LengthConstraint(maxLength: 20)])
        };
        var dialingRule = new DemoRule {
            Member = Source(callerNumberProperty),
            Constraints = new ConstraintSet([new RequiredConstraint(), new LengthConstraint(maxLength: 20)])
        };
        var connectedRule = new DemoRule {
            Member = Source(startedAtUtcProperty),
            Constraints = new ConstraintSet([new RequiredConstraint()])
        };
        var onHoldRule = new DemoRule {
            Member = Source(holdStartedAtUtcProperty),
            Constraints = new ConstraintSet([new RequiredConstraint()])
        };
        var endedRule = new DemoRule {
            Member = Source(endedAtUtcProperty),
            Constraints = new ConstraintSet([new RequiredConstraint()])
        };
        var endedReasonRule = new DemoRule {
            Member = Source(endReasonProperty),
            Constraints = new ConstraintSet([new RequiredConstraint(), new LengthConstraint(maxLength: 40)])
        };

        var placeCall = CreateEvent(
            "PlaceCall",
            [
                Source(callerNumberProperty),
                Source(recipientNumberProperty)
            ]);

        var volumeChanged = CreateEvent(
            "VolumeChanged",
            [
                Source(callIdProperty),
                Source(volumeProperty)
            ]);

        var connectCall = CreateEvent(
            "ConnectCall",
            [
                Source(callIdProperty),
                Source(startedAtUtcProperty)
            ]);

        var holdCall = CreateEvent(
            "HoldCall",
            [
                Source(callIdProperty),
                Source(holdStartedAtUtcProperty)
            ]);

        var resumeCall = CreateEvent(
            "ResumeCall",
            [
                Source(callIdProperty),
                Source(resumedAtUtcProperty)
            ]);

        var toneSent = CreateEvent(
            "ToneSent",
            [
                Source(callIdProperty),
                Source(toneProperty)
            ]);

        var sendDtmfTone = CreateAction(
            "SendDtmfTone",
            [
                Source(callIdProperty),
                Source(toneProperty)
            ],
            [new EmitEvent { Event = toneSent }]);

        var increaseVolume = CreateAction(
            "IncreaseVolume",
            [
                Source(callIdProperty),
                Source(CreateProperty("VolumeDelta", TypeCategory.Numeric, [new RequiredConstraint()]))
            ],
            [
                new SetPropertyMutation { Target = Source(volumeProperty), Value = Literal(1) },
                new EmitEvent { Event = volumeChanged }
            ]);

        var decreaseVolume = CreateAction(
            "DecreaseVolume",
            [
                Source(callIdProperty),
                Source(CreateProperty("VolumeDelta", TypeCategory.Numeric, [new RequiredConstraint()]))
            ],
            [
                new SetPropertyMutation { Target = Source(volumeProperty), Value = Literal(-1) },
                new EmitEvent { Event = volumeChanged }
            ]);

        var placeCallOnHold = CreateAction(
            "PlaceCallOnHold",
            [
                Source(callIdProperty),
                Source(holdStartedAtUtcProperty)
            ],
            [
                new SetPropertyMutation { Target = Source(holdStartedAtUtcProperty), Value = Source(holdStartedAtUtcProperty) },
                new EmitEvent { Event = holdCall }
            ]);

        var resumeHeldCall = CreateAction(
            "ResumeHeldCall",
            [
                Source(callIdProperty),
                Source(resumedAtUtcProperty)
            ],
            [
                new SetPropertyMutation { Target = Source(resumedAtUtcProperty), Value = Source(resumedAtUtcProperty) },
                new EmitEvent { Event = resumeCall }
            ]);

        var callEnded = CreateEvent(
            "CallEnded",
            [
                Source(callIdProperty),
                Source(endedAtUtcProperty),
                Source(callDurationProperty),
                Source(endReasonProperty)
            ]);

        var endCall = CreateAction(
            "EndCall",
            [
                Source(callIdProperty),
                Source(endedAtUtcProperty),
                Source(callDurationProperty),
                Source(endReasonProperty)
            ],
            [
                new SetPropertyMutation { Target = Source(endedAtUtcProperty), Value = Source(endedAtUtcProperty) },
                new SetPropertyMutation { Target = Source(callDurationProperty), Value = Source(callDurationProperty) },
                new SetPropertyMutation { Target = Source(endReasonProperty), Value = Source(endReasonProperty) },
                new EmitEvent { Event = callEnded }
            ]);

        var activeCallStage = new Stage {
            Name = "Active",
            Rules = [activeCallRule, activeParticipantsRule, activeRecipientRule],
            Actions = [endCall],
            EntryEvents = [placeCall],
            ExitEvents = [callEnded],
            SubStages = activeSubStages
        };

        var dialingStage = new Stage {
            Name = "Dialing",
            Rules = [dialingRule],
            Actions = [],
            EntryEvents = [placeCall],
            ExitEvents = [connectCall],
            SuperStage = activeCallStage
        };

        var connectedStage = new Stage {
            Name = "Connected",
            Rules = [connectedRule],
            Actions = [sendDtmfTone, increaseVolume, decreaseVolume, placeCallOnHold],
            EntryEvents = [connectCall, resumeCall],
            ExitEvents = [holdCall],
            SuperStage = activeCallStage,
            SubStages = connectedSubStages
        };

        var onHoldStage = new Stage {
            Name = "OnHold",
            Rules = [onHoldRule],
            Actions = [resumeHeldCall],
            EntryEvents = [holdCall],
            ExitEvents = [resumeCall],
            SuperStage = connectedStage
        };

        activeSubStages.Add(dialingStage);
        activeSubStages.Add(connectedStage);
        connectedSubStages.Add(onHoldStage);

        var endedStage = new Stage {
            Name = "Ended",
            Rules = [endedRule, endedReasonRule],
            Actions = [],
            EntryEvents = [callEnded],
            ExitEvents = []
        };

        var model = CreateEntity(
            "PhoneCall",
            [
                callIdProperty,
                callerNumberProperty,
                recipientNumberProperty,
                startedAtUtcProperty,
                holdStartedAtUtcProperty,
                resumedAtUtcProperty,
                toneProperty,
                volumeProperty,
                endedAtUtcProperty,
                callDurationProperty,
                endReasonProperty
            ],
            [activeCallStage, endedStage],
            [activeCallRule, activeParticipantsRule, activeRecipientRule, dialingRule, connectedRule, onHoldRule, endedRule, endedReasonRule],
            [sendDtmfTone, increaseVolume, decreaseVolume, placeCallOnHold, resumeHeldCall, endCall],
            [placeCall, connectCall, holdCall, resumeCall, toneSent, volumeChanged, callEnded]);
    }

    /// <summary>
    /// This test is intended to prove completeness of the modeling system by attempting to model itself.
    /// If we can model the modeling system using the modeling system, then we have likely achieved a good level of expressiveness and flexibility.
    /// </summary>
    static void Test3() {
        Property CreateIdProperty(string name) =>
            CreateProperty(name, TypeCategory.Identifier, [new RequiredConstraint()]);

        Property CreateOptionalIdProperty(string name) =>
            CreateProperty(name, TypeCategory.Identifier | TypeCategory.Nullable, []);

        Property CreateNameProperty(string name) =>
            CreateProperty(name, TypeCategory.Text, [new RequiredConstraint(), new LengthConstraint(maxLength: 100)]);

        Property CreateRequiredTextProperty(string name, int maxLength = 250) =>
            CreateProperty(name, TypeCategory.Text, [new RequiredConstraint(), new LengthConstraint(maxLength: maxLength)]);

        Property CreateOptionalTextProperty(string name, int maxLength = 250) =>
            CreateProperty(name, TypeCategory.Text, [new LengthConstraint(maxLength: maxLength)]);

        Property CreateKindProperty(string name) =>
            CreateProperty(name, TypeCategory.Enumeration, [new RequiredConstraint()]);

        Property CreatePositionProperty(string name) =>
            CreateProperty(name, TypeCategory.Integer, [new RequiredConstraint()]);

        DemoRule CreateRequiredRule(Property property) => new() {
            Member = Source(property),
            Constraints = new ConstraintSet([new RequiredConstraint()])
        };

        Entity CreateDefinitionEntity(string entityTypeName, Property idProperty, Property nameProperty, IEnumerable<Property> extraProperties) {
            var idRule = CreateRequiredRule(idProperty);
            var nameRule = CreateRequiredRule(nameProperty);

            var createEvent = CreateEvent(
                $"Create{entityTypeName}",
                [
                    Source(idProperty),
                    Source(nameProperty)
                ]);

            var publishEvent = CreateEvent(
                $"Publish{entityTypeName}",
                [
                    Source(idProperty)
                ]);

            var publishAction = CreateAction(
                $"Publish{entityTypeName}",
                [
                    Source(idProperty)
                ],
                [new EmitEvent { Event = publishEvent }]);

            var draftStage = new Stage {
                Name = "Draft",
                Rules = [idRule, nameRule],
                Actions = [publishAction],
                EntryEvents = [createEvent],
                ExitEvents = [publishEvent]
            };

            var publishedStage = new Stage {
                Name = "Published",
                Rules = [idRule, nameRule],
                Actions = [],
                EntryEvents = [publishEvent],
                ExitEvents = []
            };

            return CreateEntity(
                entityTypeName,
                [idProperty, nameProperty, .. extraProperties],
                [draftStage, publishedStage],
                [idRule, nameRule],
                [publishAction],
                [createEvent, publishEvent]);
        }

        var modelDefinition = CreateDefinitionEntity(
            "ModelDefinition",
            CreateIdProperty("ModelDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateRequiredTextProperty("Version", 50),
                CreateOptionalTextProperty("Description", 500)
            ]);

        var entityDefinition = CreateDefinitionEntity(
            "EntityDefinition",
            CreateIdProperty("EntityDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("ModelDefinitionId"),
                CreateOptionalTextProperty("Description", 500)
            ]);

        var propertyDefinition = CreateDefinitionEntity(
            "PropertyDefinition",
            CreateIdProperty("PropertyDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("EntityDefinitionId"),
                CreateKindProperty("TypeCategory"),
                CreateOptionalTextProperty("Description", 500)
            ]);

        var constraintDefinition = CreateDefinitionEntity(
            "ConstraintDefinition",
            CreateIdProperty("ConstraintDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("PropertyDefinitionId"),
                CreateKindProperty("ConstraintKind"),
                CreateOptionalTextProperty("ConfigurationText", 1000)
            ]);

        var ruleDefinition = CreateDefinitionEntity(
            "RuleDefinition",
            CreateIdProperty("RuleDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("EntityDefinitionId"),
                CreateIdProperty("MemberPropertyDefinitionId"),
                CreateOptionalTextProperty("ConstraintDescription", 1000)
            ]);

        var stageDefinition = CreateDefinitionEntity(
            "StageDefinition",
            CreateIdProperty("StageDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("EntityDefinitionId"),
                CreateOptionalIdProperty("SuperStageDefinitionId")
            ]);

        var stageRuleBindingDefinition = CreateDefinitionEntity(
            "StageRuleBindingDefinition",
            CreateIdProperty("StageRuleBindingDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("StageDefinitionId"),
                CreateIdProperty("RuleDefinitionId"),
                CreatePositionProperty("Position")
            ]);

        var actionDefinition = CreateDefinitionEntity(
            "ActionDefinition",
            CreateIdProperty("ActionDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("EntityDefinitionId"),
                CreateOptionalTextProperty("Description", 500)
            ]);

        var stageActionBindingDefinition = CreateDefinitionEntity(
            "StageActionBindingDefinition",
            CreateIdProperty("StageActionBindingDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("StageDefinitionId"),
                CreateIdProperty("ActionDefinitionId"),
                CreatePositionProperty("Position")
            ]);

        var eventDefinition = CreateDefinitionEntity(
            "EventDefinition",
            CreateIdProperty("EventDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("EntityDefinitionId"),
                CreateOptionalTextProperty("Description", 500)
            ]);

        var stageEventBindingDefinition = CreateDefinitionEntity(
            "StageEventBindingDefinition",
            CreateIdProperty("StageEventBindingDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("StageDefinitionId"),
                CreateIdProperty("EventDefinitionId"),
                CreateKindProperty("BindingKind"),
                CreatePositionProperty("Position")
            ]);

        var valueSourceDefinition = CreateDefinitionEntity(
            "ValueSourceDefinition",
            CreateIdProperty("ValueSourceDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateKindProperty("SourceKind"),
                CreateOptionalIdProperty("PropertyDefinitionId"),
                CreateOptionalTextProperty("LiteralTypeCategory", 100),
                CreateOptionalTextProperty("LiteralValueText", 1000)
            ]);

        var actionParameterDefinition = CreateDefinitionEntity(
            "ActionParameterDefinition",
            CreateIdProperty("ActionParameterDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("ActionDefinitionId"),
                CreateIdProperty("ValueSourceDefinitionId"),
                CreatePositionProperty("Position")
            ]);

        var eventPropertyDefinition = CreateDefinitionEntity(
            "EventPropertyDefinition",
            CreateIdProperty("EventPropertyDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("EventDefinitionId"),
                CreateIdProperty("ValueSourceDefinitionId"),
                CreatePositionProperty("Position")
            ]);

        var effectDefinition = CreateDefinitionEntity(
            "EffectDefinition",
            CreateIdProperty("EffectDefinitionId"),
            CreateNameProperty("Name"),
            [
                CreateIdProperty("ActionDefinitionId"),
                CreateKindProperty("EffectKind"),
                CreateOptionalIdProperty("EventDefinitionId"),
                CreateOptionalIdProperty("TargetPropertyDefinitionId"),
                CreateOptionalIdProperty("ValueSourceDefinitionId")
            ]);

        var modelingSystem = new[] {
            modelDefinition,
            entityDefinition,
            propertyDefinition,
            constraintDefinition,
            ruleDefinition,
            stageDefinition,
            stageRuleBindingDefinition,
            actionDefinition,
            stageActionBindingDefinition,
            eventDefinition,
            stageEventBindingDefinition,
            valueSourceDefinition,
            actionParameterDefinition,
            eventPropertyDefinition,
            effectDefinition
        };
    }

    static Property CreateProperty(string name, TypeCategory typeCategory, Constraint[] constraints) {
        var property = new Property {
            Name = name,
            TypeCategory = typeCategory
        };

        foreach (var constraint in constraints) {
            property.AddConstraint(constraint);
        }

        return property;
    }

    static PropertyValueSource Source(Property property) =>
        new() { Property = property };

    static LiteralValueSource Literal(object? value) =>
        new() { Value = value };

    static Event CreateEvent(string name, IEnumerable<ValueSource> properties) {
        var @event = new Event {
            Name = name
        };

        foreach (var property in properties) {
            @event.AddProperty(property);
        }

        return @event;
    }

    static Action CreateAction(string name, IEnumerable<ValueSource> parameters, IEnumerable<Effect> effects) {
        var action = new Action {
            Name = name
        };

        foreach (var parameter in parameters) {
            action.AddParameter(parameter);
        }

        foreach (var effect in effects) {
            action.AddEffect(effect);
        }

        return action;
    }

    static Entity CreateEntity(
        string name,
        IEnumerable<Property> properties,
        IEnumerable<Stage> stages,
        IEnumerable<Rule> rules,
        IEnumerable<Action> actions,
        IEnumerable<Event> events) {
        var entity = new Entity {
            Name = name
        };

        foreach (var property in properties) {
            entity.AddProperty(property);
        }

        foreach (var stage in stages) {
            entity.AddStage(stage);
        }

        foreach (var rule in rules) {
            entity.AddRule(rule);
        }

        foreach (var action in actions) {
            entity.AddAction(action);
        }

        foreach (var @event in events) {
            entity.AddEvent(@event);
        }

        entity.Validate();

        return entity;
    }

    sealed class DemoRule : Rule;
}