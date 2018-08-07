using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Poly.Data {
    public class Array<T> : Serializer<T> {
        Type       element_type;
        Serializer element_serializer;

        public Array() {
            element_type = typeof(T).GetElementType();
            element_serializer = Serializer.Get(element_type);
        }

        public override bool Serialize(StringBuilder json, T obj) {
            var array = obj as Array;
            var lastIndex = array.Length - 1;

            json.Append('[');
            for (var i = 0; i <= lastIndex; i++) {
                var member = array.GetValue(i);
                var serialize = element_serializer.SerializeObject(json, member);

                if (!serialize)
                    return false;

                if (i != lastIndex)
                    json.Append(',');
            }

            json.Append(']');
            return true;
        }
        
        public override bool Deserialize(StringIterator json, out T obj) {
            if (!json.SelectSection('[', ']'))
                goto formatException;

            var list = new List<object>();

            if (!json.IsDone) {
                do {
                    json.ConsumeWhitespace();

                    if (element_serializer.DeserializeObject(json, out object element))
                        list.Add(element);
                    else
                        goto formatException;

                    if (!json.Consume(',')) {
                        json.ConsumeSection();
                        break;
                    }
                }
                while (!json.IsDone);
            }

            var count = list.Count;
            var array = Array.CreateInstance(element_type, count);

            if (count > 0)
                Array.Copy(list.ToArray(), array, count);

            obj = (T)(object)(array);
            return true;

        formatException:
            Log.Error($"Unable to deserialize {json} into {element_type.Name}");
            obj = default;
            return false;
        }

        public override bool ValidateFormat(StringIterator json) {
            if (!json.SelectSection('[', ']'))
                return false;

            if (!json.IsDone) {
                do {
                    json.ConsumeWhitespace();

                    if (!element_serializer.ValidateFormat(json))
                        return false;

                    if (!json.Consume(',')) {
                        json.ConsumeSection();
                        break;
                    }
                }
                while (!json.IsDone);
            }

            json.ConsumeSection();
            return true;
        }
    }
}