namespace Poly.Collections {

    public partial class MatchingCollection<T> {

        public bool Add(string key, T value) {
            var it = new StringIterator(key);
            it.SelectSplitSections(KeySeperatorCharacter);

            if (TryInsertGroup(it, out Group group)) {
                if (!TryGetStorage(it, group, out Item item)) {
                    group.Items.Add(new Item(it, value));
                    Count++;
                    return true;
                }
            }

            return false;
        }

        public bool Remove(string key) {
            var it = new StringIterator(key);
            it.SelectSplitSections(KeySeperatorCharacter);

            if (TryGetGroup(it, out Group group)) {
                if (TryGetStorage(it, group, out Item item)) {
                    group.Items.Remove(item);
                    return true;
                }
            }

            return false;
        }

        public void Clear() {
            storage.Groups.Clear();
            storage.Items.Clear();
            Count = 0;
        }

        public T Get(string key) {
            var it = new StringIterator(key);
            it.SelectSplitSections(KeySeperatorCharacter);

            return TryGetItem(it, out T value) ? value : default;
        }

        public bool TryGet(string key, out T value) {
            var it = new StringIterator(key);
            it.SelectSplitSections(KeySeperatorCharacter);

            return TryGetItem(it, out value);
        }

        public void Set(string key, T value) {
            var it = new StringIterator(key);
            it.SelectSplitSections(KeySeperatorCharacter);

            TrySetItem(it, value);
        }

        public bool TrySet(string key, T value) {
            var it = new StringIterator(key);
            it.SelectSplitSections(KeySeperatorCharacter);

            return TrySetItem(it, value);
        }

        private bool TryGetGroup(StringIterator it, out Group group) {
            Group current = storage;

            do {
                if (it.IsLastSection) {
                    group = current;
                    return true;
                }

                var next = current.Groups.SingleOrDefault(_ => _.Matcher.Compare(it));

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

        private bool TryInsertGroup(StringIterator it, out Group group) {
            Group current = storage;

            do {
                if (it.IsLastSection) {
                    group = current;
                    return true;
                }

                var next = current.Groups.SingleOrDefault(_ => _.Matcher.Compare(it));

                if (next == null) {
                    next = new Group(it);
                    current.Groups.Add(next);
                }

                current = next;
                it.ConsumeSection();
            }
            while (!it.IsDone);

            group = default;
            return false;
        }

        private bool TryGetStorage(StringIterator it, Group group, out Item item) {
            var element = group.Items.SingleOrDefault(_ => _.Matcher.Compare(it));

            if (element is Item result) {
                item = result;
                return true;
            }

            item = default;
            return false;
        }

        private bool TryGetItem(StringIterator it, out T value) {
            if (TryGetGroup(it, out Group group)) {
                if (TryGetStorage(it, group, out Item item)) {
                    value = item.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private bool TrySetItem(StringIterator it, T value) {
            if (TryInsertGroup(it, out Group group)) {
                if (TryGetStorage(it, group, out Item item)) {
                    item.Value = value;
                }
                else {
                    group.Items.Add(new Item(it, value));
                    Count++;
                }

                return true;
            }

            return false;
        }
    }
}