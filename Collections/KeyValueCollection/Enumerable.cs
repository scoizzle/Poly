using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Collections {

    public partial class KeyValueCollection<T> {
        public IEnumerable<KeyValuePair> KeyValuePairs {
            get {
                var list = List;
                var count = list.Count;

                for (var i = 0; i < count; i++) {
                    var collection = list[i];
                    var collection_list = collection.List;
                    var collection_elements = collection_list;
                    var collection_count = collection_list.Count;

                    for (var j = 0; j < collection_count; j++) {
                        yield return collection_elements[j];
                        //var element = collection_elements[j];

                        //if (element is KeyArrayPair array) {
                        //    foreach (var kvp in array.GetEnumerator())
                        //        yield return kvp;
                        //}
                        //else {
                        //    yield return element;
                        //}
                    }
                }
            }
        }

        public IEnumerator<KeyValuePair> GetEnumerator() =>
            KeyValuePairs.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            KeyValuePairs.GetEnumerator();
    }
}