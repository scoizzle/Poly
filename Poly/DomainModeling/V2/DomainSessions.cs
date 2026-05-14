namespace Poly.DomainModeling.V2;

using System.Collections.ObjectModel;

public sealed record DomainSession(Guid SessionId, int Revision, Domain Domain);

public sealed record DomainTraceEntry(int Revision, DateTimeOffset Timestamp, DomainMutation Mutation, DomainValidationResult Validation);

public sealed class DomainSessionManager {
    private readonly Dictionary<Guid, DomainSession> _sessions = [];
    private readonly Dictionary<Guid, List<DomainTraceEntry>> _trace = [];

    public DomainSession CreateSession(string domainName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainName);
        var session = new DomainSession(Guid.NewGuid(), 0, new Domain(domainName, [], []));
        _sessions[session.SessionId] = session;
        _trace[session.SessionId] = [];
        return session;
    }

    public DomainSession Mutate(Guid sessionId, DomainMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (!_sessions.TryGetValue(sessionId, out var session)) {
            throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
        }

        var domain = DomainMutationEngine.Apply(session.Domain, mutation);
        var validation = DomainValidator.Validate(domain);
        var updated = session with { Revision = session.Revision + 1, Domain = domain };
        _sessions[sessionId] = updated;
        _trace[sessionId].Add(new DomainTraceEntry(updated.Revision, DateTimeOffset.UtcNow, mutation, validation));
        return updated;
    }

    public IReadOnlyList<DomainTraceEntry> GetTrace(Guid sessionId)
    {
        if (!_trace.TryGetValue(sessionId, out var trace)) {
            throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
        }

        return new ReadOnlyCollection<DomainTraceEntry>(trace);
    }
}
