using System.Linq;

namespace Poly.Collections {
    using Data;
    using String.Matcher.Parsers;

    public partial class MatchingCollection<T> {
        public T Get(string key, SetDelegate set) {
            var it = new StringIterator(key);
            it.SelectSplitSections(KeySeperatorCharacter);

            return TryGetItem(it, out T value, set) ? value : default;
        }

        public T Get<C>(string key, Serializer<C> serializer, C context) =>
            Get(key, Matcher.SetMemberValue(serializer, context));

        private bool TryGetGroup(StringIterator it, out Group group, SetDelegate set) {
            Group current = storage;

            do {
                if (it.IsLastSection) {
                    group = current;
                    return true;
                }

                var next = current.Groups.SingleOrDefault(_ => _.Matcher.Extract(it, set));

                if (next == null) {
                    group = null;
                    return false;
                }

                current = next;
                it.ConsumeSection();
            }
            while (!it.IsDone);

            group = default;
            return false;
        }

        private bool TryGetStorage(StringIterator it, Group group, out Item item, SetDelegate set) {
            var element = group.Items.SingleOrDefault(_ => _.Matcher.Extract(it, set));

            if (element is Item result) {
                item = result;
                return true;
            }

            item = default;
            return false;
        }

        private bool TryGetItem(StringIterator it, out T value, SetDelegate set) {
            if (TryGetGroup(it, out Group group, set)) {
                if (TryGetStorage(it, group, out Item item, set)) {
                    value = item.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}