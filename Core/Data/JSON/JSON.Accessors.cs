using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Data {
    public partial class JSON {
        new public object Get(string Key) {
            return Get(Key.Split(KeySeperatorCharacter));
        }

        public object Get(params string[] Keys) {
            int i = 0;
			int len = Keys.Length;

            object Value = null;
            JSON Current = this;

			while (i < len) { 
                if (Current == null)
                    return null;

                if (!Current.TryGet(Keys[i], out Value))
					return null;
				
				if (i == len)
					break;
				
				Current = Value as JSON;
				i++;
			};

            return Value;
        }
        
        public T Get<T>(string Key) {
            return Get<T>(Key.Split(KeySeperatorCharacter));
        }

        public T Get<T>(params string[] Key) {
            var Value = Get(Key);

			try {
				return (T)(Value);
			}
			catch { }
			return default(T);
		}

        public T Get<T>(IEnumerable<string> Key) {
            var Value = Get(Key.ToArray());


			try {
				return (T)(Value);
			}
			catch { }
			return default(T);
        }

		public void Set<T>(string Key, T[] Values) {
			AssignValue(Key, NewArray(Values));
		}

        new public void Set(string Key, object Value) {
            AssignValue(Key, Value);
        }

        public void Set(IEnumerable<string> Keys, object Value) {
            object Object;
            JSON Current = this;
            int I = 1, Len = Keys.Count();

            foreach (var K in Keys) {
                if (I++ == Len)
                    Current.AssignValue(K, Value);
                else if (Current.TryGet(K, out Object)) {
                    if (Object is JSON)
                        Current = Object as JSON;
                    else break;
                }
                else {
                    Current.AssignValue(K, Current = new JSON());
                }
            }
        }

        public void Set(string[] Keys, object Value) {
            int i = 0;
            object Object;
            JSON Current = this;

            do {
                if (Keys.Length - i == 1) {
                    Current.AssignValue(Keys[i], Value);
                }
                else if (Current.TryGet(Keys[i], out Object)) {
                    if (Object is JSON)
                        Current = Object as JSON;
                    else break;
                }
                else {
                    Current.AssignValue(Keys[i], Current = new JSON());
                }
            }
            while (++i < Keys.Length);
        }

        public virtual bool TryGet(string Key, out object Value) {
            return TryGetValue(Key, out Value);
        }

        public virtual bool TryGet<T>(string Key, out T Value) {
            object Val;

			if (TryGet(Key, out Val)) {
				try {
					Value = (T)(Val);
					return true;
				}
				catch { }
			}

			Value = default(T);
			return false;
        }

        public virtual void AssignValue(string Key, object Value) {
            base[Key] = Value;
        }
    }
}