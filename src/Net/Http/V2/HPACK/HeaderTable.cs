using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Net.Http.V2.HPACK {
    public partial class HeaderTable {
        public List<Header> dynamic_table;

        public int MaxSize { get; private set; }

        public int MaxSizeSetting { get; }

        public int Size { get { return dynamic_table.Sum(h => h.Size); } }

        private int static_length { get; }

        private int dynamic_length { get { return dynamic_table.Count; } }

        public HeaderTable(int max_size) {
            MaxSize = max_size;
            MaxSizeSetting = max_size;

            static_length = static_table.Length;
            dynamic_table = new List<Header>();
        }

        public Header this[int index] {
            get {
                if (index < 1)
                    return null;

                if (index <= static_length) {
                    return static_table[index - 1];
                }
                else {
                    return dynamic_table[dynamic_length - (index - static_length)];
                }
            }
        }

        public int GetIndex(string key, string value = null) {
            var idx = 0;
            var dlen = dynamic_length;
            var slen = static_length;

            for (int i = 0; i < dlen; i++) {
                var header = dynamic_table[i];

                if (header.Key.Compare(key)) {
                    idx = slen + (dlen - i);

                    if (header.Value.Compare(value))
                        return idx * -1;
                }
            }

            if (idx != 0)
                return idx;

            for (int i = 0; i < slen; i++) {
                var header = static_table[i];

                if (header.Key.Compare(key)) {
                    idx = i + 1;

                    if (header.Value.Compare(value))
                        return idx * -1;
                }
            }

            return idx;
        }

        public bool SetMaxSize(int new_size) {
            if (new_size > MaxSizeSetting)
                return false;

            MaxSize = new_size;
            return ReserveSpace(dynamic_length - new_size);
        }

        public bool ReserveSpace(int size) {
            if (size > MaxSize)
                return false;

            var sz = size + dynamic_length;

            if (MaxSize < sz) {
                var count = dynamic_length;
                var number_to_remove = 0;

                while (number_to_remove < count && MaxSize < sz)
                    sz -= dynamic_table[number_to_remove++].Size;

                dynamic_table.RemoveRange(0, number_to_remove);
            }

            size = sz;
            return true;
        }

        public Header Add(string key, string value) {
            var header = new Header(key, value);

            if (!ReserveSpace(header.Size))
                return null; ;

            dynamic_table.Add(header);

            return header;
        }
    }
}