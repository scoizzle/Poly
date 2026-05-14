namespace Poly.DomainModeling.V2;

public static class DomainMcpTools {
    public static DomainSession CreateEntityWithPattern(DomainSessionManager sessions, Guid sessionId, string entityName, string pattern)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var session = sessions.Mutate(sessionId, DomainMutation.AddEntity(entityName));
        session = sessions.Mutate(sessionId, DomainMutation.AddProperty(entityName, "Id", "Uuid", isRequired: true));

        if (string.Equals(pattern, "AggregateRoot", StringComparison.OrdinalIgnoreCase)) {
            session = sessions.Mutate(sessionId, DomainMutation.AddProperty(entityName, "CreatedAt", "DateTime", isRequired: true));
            session = sessions.Mutate(sessionId, DomainMutation.AddProperty(entityName, "UpdatedAt", "DateTime", isRequired: false));
            session = sessions.Mutate(sessionId, DomainMutation.AddStage(entityName, "Draft", isInitial: true));
            session = sessions.Mutate(sessionId, DomainMutation.AddStage(entityName, "Active"));
            session = sessions.Mutate(sessionId, DomainMutation.AddStage(entityName, "Archived"));
        }
        else if (string.Equals(pattern, "ReferenceData", StringComparison.OrdinalIgnoreCase)) {
            session = sessions.Mutate(sessionId, DomainMutation.AddProperty(entityName, "Code", "Text", isRequired: true));
            session = sessions.Mutate(sessionId, DomainMutation.AddProperty(entityName, "DisplayName", "Text", isRequired: true));
        }

        return session;
    }

    public static DomainSession AddCRUD(DomainSessionManager sessions, Guid sessionId, string entityName)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        var session = sessions.Mutate(sessionId, DomainMutation.AddAction(entityName, "Create"));
        session = sessions.Mutate(sessionId, DomainMutation.AddActionEffect(entityName, "Create", new CreateEntity(entityName)));

        session = sessions.Mutate(sessionId, DomainMutation.AddAction(entityName, "Read"));
        session = sessions.Mutate(sessionId, DomainMutation.AddActionEffect(entityName, "Read", new SetProperty("LastAccessedAt", "UtcNow")));

        session = sessions.Mutate(sessionId, DomainMutation.AddAction(entityName, "Update"));
        session = sessions.Mutate(sessionId, DomainMutation.AddActionEffect(entityName, "Update", new SetProperty("UpdatedAt", "UtcNow")));

        session = sessions.Mutate(sessionId, DomainMutation.AddAction(entityName, "Delete"));
        session = sessions.Mutate(sessionId, DomainMutation.AddActionEffect(entityName, "Delete", new TransitionStage("Archived")));

        return session;
    }
}
