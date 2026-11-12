using System.Collections;

namespace Poly.DomainModeling.Runtime;

// Snapshot partial: bag and registration capture/restore around a transaction.
public sealed partial class ModuleInstanceExecutor {
    private sealed record BagState(
        IDictionary<string, object?> Bag,
        KeyValuePair<string, object?>[] Entries,
        (IList List, object?[] Items)[] Lists);

    private sealed record ExecutorState(BagState[] Bags, int Registered, int PortMark);

    private ExecutorState Capture() {
        var bags = new BagState[_order.Count];
        for (var i = 0; i < bags.Length; i++) {
            var bag = _order[i].Bag;
            var entries = bag.ToArray();
            var lists = new List<(IList, object?[])>();
            foreach (var (_, value) in entries) {
                if (value is IList { IsFixedSize: false, IsReadOnly: false } list)
                    lists.Add((list, ToArray(list)));
            }
            bags[i] = new BagState(bag, entries, [.. lists]);
        }
        return new ExecutorState(bags, _order.Count, _port.Mark());
    }

    private void Restore(ExecutorState state) {
        _port.RollbackTo(state.PortMark);
        for (var i = _order.Count - 1; i >= state.Registered; i--) {
            var dropped = _order[i];
            _byId.Remove(dropped.Id);
            _byBag.Remove(dropped.Bag);
            _order.RemoveAt(i);
        }
        foreach (var saved in state.Bags) {
            saved.Bag.Clear();
            foreach (var (key, value) in saved.Entries)
                saved.Bag[key] = value;
            foreach (var (list, items) in saved.Lists) {
                list.Clear();
                foreach (var item in items)
                    list.Add(item);
            }
        }
    }

    private static object?[] ToArray(IList list) {
        var items = new object?[list.Count];
        list.CopyTo(items, 0);
        return items;
    }
}