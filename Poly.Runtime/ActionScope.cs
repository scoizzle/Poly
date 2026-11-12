using System.Collections;
using System.ComponentModel;

namespace Poly.Runtime;

/// <summary>
/// The module's one undo log, #677 R-08 (Q1). A lowered action opens a scope, touches every
/// target before it writes, records every instance it creates, and commits or rolls back in its
/// epilogue. A failure aborts the whole action: <see cref="Rollback"/> restores every touched
/// object in reverse touch order and then reverts the storage journal, so entity state and
/// reserved keys agree again. There is no partial commit and no poison state.
/// </summary>
/// <remarks>
/// <para>
/// The ambient frame lives in an <see cref="AsyncLocal{T}"/> and every frame mutation is under
/// one lock; entity data itself is not synchronised (Q2), so parallel actions need distinct
/// journals or host serialisation. The frame is opened by <see cref="Begin"/>, joined by a nested
/// <see cref="Begin"/>, and closed by <see cref="Commit"/> on the root token or by
/// <see cref="Rollback"/> on any token. <see cref="Commit"/> and <see cref="Rollback"/> resolve
/// their frame from the token, never from the ambient frame, so a token that outlives its flow
/// still completes the frame it opened.
/// </para>
/// <para>
/// <see cref="ActionScope"/> throws only on host misuse: a domain failure is a returned
/// <see cref="DomainResult"/>, never an exception. <see cref="Touch"/> and <see cref="Create"/>
/// outside a scope are no-ops, which keeps a host with no scope cheap; on a closed ambient frame
/// they throw, because that means a leaked frame.
/// </para>
/// </remarks>
public static class ActionScope {
    private const int RootScopeId = 0;

    private static readonly object Gate = new();
    private static readonly AsyncLocal<Frame?> Ambient = new();
    private static readonly Dictionary<IStorageJournal, Frame> LiveRoots =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Opens a scope, or joins the innermost open one as a nested scope. With no ambient frame,
    /// or a closed one, this opens a root: it registers <paramref name="journal"/> as a live root
    /// (throwing when another root already holds it) and takes the journal's root mark.
    /// </summary>
    /// <param name="journal">The storage port to journal, or <c>null</c> in a host with no storage.</param>
    public static ActionScopeToken Begin(IStorageJournal? journal = null) {
        lock (Gate) {
            var ambient = Ambient.Value;
            if (ambient is null || ambient.State != FrameState.Open) {
                if (journal is not null && LiveRoots.ContainsKey(journal))
                    throw new InvalidOperationException(
                        "The storage journal already has a live action scope; a flat journal supports one root mark at a time.");

                var frame = new Frame(journal, journal?.Mark() ?? 0);
                if (journal is not null)
                    LiveRoots.Add(journal, frame);

                Ambient.Value = frame;
                return new ActionScopeToken(frame, RootScopeId);
            }

            if (journal is not null && !ReferenceEquals(journal, ambient.Journal))
                throw new InvalidOperationException(
                    "A nested action scope must join the frame's journal, or name none.");

            var scopeId = ambient.NextScopeId++;
            ambient.OpenScopes.Add(scopeId);
            return new ActionScopeToken(ambient, scopeId);
        }
    }

    /// <summary>
    /// Snapshots <paramref name="instance"/> once, before the caller writes it, so
    /// <see cref="Rollback"/> can restore it. Accepts an entity (<see cref="IActionScoped"/>) or
    /// a simulator bag (<see cref="IDictionary{TKey,TValue}"/> of <c>string</c> to <c>object?</c>);
    /// anything else is an <see cref="ArgumentException"/>. A no-op outside a scope.
    /// </summary>
    public static void Touch(object instance) {
        ArgumentNullException.ThrowIfNull(instance);

        lock (Gate) {
            var frame = Ambient.Value;
            if (frame is null)
                return;
            if (frame.State != FrameState.Open)
                throw new InvalidOperationException(
                    "The ambient action scope is closed; the flow is holding a leaked frame.");
            if (frame.TouchedSet.Contains(instance))
                return;

            var snapshot = instance switch {
                IActionScoped scoped => scoped.__Snapshot(),
                IDictionary<string, object?> bag => SnapshotBag(bag),
                _ => throw new ArgumentException(
                    $"ActionScope.Touch cannot snapshot {instance.GetType().FullName}: an entity must implement Poly.Runtime.IActionScoped, and a simulator bag must be an IDictionary<string, object?>.",
                    nameof(instance))
            };

            frame.TouchedSet.Add(instance);
            frame.Touched.Add(instance);
            frame.Snapshots.Add(snapshot);
        }
    }

    /// <summary>
    /// Records an instance the action created, with its entity name, so the host can see what the
    /// action added before it closes the root. A no-op outside a scope.
    /// </summary>
    public static void Create(object instance, string entity) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(entity);

        lock (Gate) {
            var frame = Ambient.Value;
            if (frame is null)
                return;
            if (frame.State != FrameState.Open)
                throw new InvalidOperationException(
                    "The ambient action scope is closed; the flow is holding a leaked frame.");

            frame.Created.Add((instance, entity));
        }
    }

    /// <summary>
    /// The instances the ambient action created, in creation order. A copy taken under the lock,
    /// read before the root closes; empty outside an open scope.
    /// </summary>
    public static IReadOnlyList<(object Instance, string Entity)> CreatedInstances {
        get {
            lock (Gate) {
                var frame = Ambient.Value;
                return frame is null || frame.State != FrameState.Open ? [] : [.. frame.Created];
            }
        }
    }

    /// <summary>True while this flow's ambient frame is open.</summary>
    public static bool HasOpenScope {
        get {
            lock (Gate) {
                var frame = Ambient.Value;
                return frame is not null && frame.State == FrameState.Open;
            }
        }
    }

    /// <summary>
    /// Completes the scope <paramref name="token"/> names. A nested token removes its own id; the
    /// root token closes the frame and keeps the journal's contents. The root throws while a
    /// nested scope is still open.
    /// </summary>
    public static void Commit(ActionScopeToken token) {
        lock (Gate) {
            var frame = Resolve(token);
            if (frame.State == FrameState.Committed)
                throw new InvalidOperationException("The action scope is already committed.");
            if (frame.State == FrameState.RolledBack)
                throw new InvalidOperationException(
                    "The action scope was rolled back; it cannot be committed.");

            if (token.ScopeId != RootScopeId) {
                if (!frame.OpenScopes.Remove(token.ScopeId))
                    throw new InvalidOperationException("The nested action scope is already complete.");

                return;
            }

            if (frame.OpenScopes.Count > 1)
                throw new InvalidOperationException(
                    "A nested action scope is still open; the root cannot commit.");

            frame.State = FrameState.Committed;
            Close(frame);
        }
    }

    /// <summary>
    /// Rolls the whole frame back: every touched object in reverse touch order, then the journal
    /// to the root mark. Any token of an open frame may do it, because a handler failure aborts
    /// the whole action (Q1). Rolling back an already rolled-back frame is a no-op; a committed
    /// frame throws. Every restore is attempted, the journal is always reverted and the frame is
    /// always closed; failures surface together as an <see cref="AggregateException"/>.
    /// </summary>
    public static void Rollback(ActionScopeToken token) {
        lock (Gate) {
            var frame = Resolve(token);
            if (frame.State == FrameState.RolledBack)
                return;
            if (frame.State == FrameState.Committed)
                throw new InvalidOperationException(
                    "The action scope was committed; it cannot be rolled back.");

            var errors = new List<Exception>();
            for (var i = frame.Touched.Count - 1; i >= 0; i--) {
                try {
                    Restore(frame.Touched[i], frame.Snapshots[i]);
                }
                catch (Exception exception) {
                    errors.Add(exception);
                }
            }

            try {
                frame.Journal?.RollbackTo(frame.RootMark);
            }
            catch (Exception exception) {
                errors.Add(exception);
            }

            frame.State = FrameState.RolledBack;
            frame.Touched.Clear();
            frame.Snapshots.Clear();
            frame.TouchedSet.Clear();
            frame.Created.Clear();
            Close(frame);

            if (errors.Count > 0)
                throw new AggregateException(
                    "ActionScope.Rollback could not restore every touched object.", errors);
        }
    }

    private static Frame Resolve(ActionScopeToken token) =>
        token.Frame ?? throw new ArgumentException(
            "The token did not come from ActionScope.Begin.", nameof(token));

    private static void Close(Frame frame) {
        frame.OpenScopes.Clear();
        if (frame.Journal is not null)
            LiveRoots.Remove(frame.Journal);
        if (ReferenceEquals(Ambient.Value, frame))
            Ambient.Value = null;
    }

    private static void Restore(object instance, object snapshot) {
        switch (instance) {
            case IActionScoped scoped:
                scoped.__Restore(snapshot);
                break;
            case IDictionary<string, object?> bag when snapshot is BagSnapshot saved:
                RestoreBag(bag, saved);
                break;
            default:
                throw new InvalidOperationException(
                    $"ActionScope cannot restore {instance.GetType().FullName}.");
        }
    }

    // Shallow by contract: entries by reference and non-fixed list contents, so a mutated child
    // object is only restored when the action also touched it.
    private static BagSnapshot SnapshotBag(IDictionary<string, object?> bag) {
        var entries = bag.ToArray();
        var lists = new List<(IList List, object?[] Items)>();
        foreach (var (_, value) in entries) {
            if (value is IList { IsFixedSize: false, IsReadOnly: false } list)
                lists.Add((list, ToArray(list)));
        }

        return new BagSnapshot(entries, [.. lists]);
    }

    private static void RestoreBag(IDictionary<string, object?> bag, BagSnapshot snapshot) {
        bag.Clear();
        foreach (var (key, value) in snapshot.Entries)
            bag[key] = value;

        foreach (var (list, items) in snapshot.Lists) {
            list.Clear();
            foreach (var item in items)
                list.Add(item);
        }
    }

    private static object?[] ToArray(IList list) {
        var items = new object?[list.Count];
        list.CopyTo(items, 0);
        return items;
    }

    private sealed record BagSnapshot(
        KeyValuePair<string, object?>[] Entries,
        (IList List, object?[] Items)[] Lists);
}

/// <summary>
/// A handle on one open action scope. The default value is invalid; <see cref="ActionScope.Commit"/>
/// and <see cref="ActionScope.Rollback"/> throw on it.
/// </summary>
public readonly struct ActionScopeToken {
    internal ActionScopeToken(Frame frame, int scopeId) {
        Frame = frame;
        ScopeId = scopeId;
    }

    /// <summary>True only for the token that opened the frame; a nested token is false.</summary>
    public bool IsRoot => Frame is not null && ScopeId == RootScopeId;

    internal Frame Frame { get; }

    internal int ScopeId { get; }

    private const int RootScopeId = 0;
}

/// <summary>
/// The snapshot pair a generated entity implements so an action scope can restore it. Generated
/// entities implement the members implicitly (public methods under the reserved <c>__</c> prefix,
/// which the DSL refuses for member and action-parameter names); an explicit implementation would
/// need an explicit-interface member in the AST and writer. Neither may call the storage port.
/// </summary>
public interface IActionScoped {
    /// <summary>The entity's current state, opaque to the scope.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    object __Snapshot();

    /// <summary>Restores a state this scope captured before the action wrote the entity.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    void __Restore(object state);
}

/// <summary>One open frame: its journal, its root mark, and what the action touched and created.</summary>
internal sealed class Frame(IStorageJournal? journal, int rootMark) {
    internal IStorageJournal? Journal { get; } = journal;

    internal int RootMark { get; } = rootMark;

    internal FrameState State { get; set; } = FrameState.Open;

    internal int NextScopeId { get; set; } = 1;

    internal HashSet<int> OpenScopes { get; } = [0];

    internal HashSet<object> TouchedSet { get; } = new(ReferenceEqualityComparer.Instance);

    internal List<object> Touched { get; } = [];

    internal List<object> Snapshots { get; } = [];

    internal List<(object Instance, string Entity)> Created { get; } = [];
}

internal enum FrameState {
    Open,
    Committed,
    RolledBack
}