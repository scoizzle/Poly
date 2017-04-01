using System;
using System.Collections;
using System.Linq;
using System.IO;
using System.Text;

namespace Poly.Data {
	public partial class JSON : KeyValueCollection<object> {
		public char KeySeperatorCharacter = '.';

		public new object this[string Key] {
			get {
				return Get(Key);
			}
			set {
				Set(Key.Split(KeySeperatorCharacter), value);
			}
		}

		public object this[params string[] Keys] {
			get {
				return Get(Keys);
			}
			set {
				Set(Keys, value);
			}
		}

		public static JSON NewArray(IEnumerable objs) {
			var js = new JSON { IsArray = true };

			foreach (var o in objs)
				js.Add(o);

			return js;
		}

		public static JSON NewArray<T>(params T[] objs) {
			var js = new JSON { IsArray = true };

			foreach (var o in objs)
				js.Add(o);

			return js;
		}

		public JSON() { }

		public JSON(string Json) {
			Parse(Json, this);
		}

		public bool IsEmpty {
			get {
				return Count == 0;
			}
		}

		public bool IsArray { get; set; }

		public void Add(object Object) {
			Add(Count.ToString(), Object);
		}

		public void Add(KeyValuePair Pair) {
			Add(Pair.Key ?? Count.ToString(), Pair.Value);
		}

		public void Add(IEnumerable Elements) {
			foreach (var e in Elements)
				Add(e);
		}

		public override string ToString() {
			return Stringify(this, false);
		}

		public virtual string ToString(bool HumanFormat) {
			return Stringify(this, HumanFormat);
		}

		public static string Stringify(JSON This, bool HumanFormat) {
			var Output = new StringBuilder();
			Stringify(This, Output, HumanFormat ? 0 : -1);
			return Output.ToString();
		}

		public static void Stringify(JSON This, StringBuilder Output = null, int Tabs = -1) {
			if (This == null) return;
			if (Output == null) Output = new StringBuilder();

			var IsArray = This.IsArray;
			int Index = 0, LastIndex = This.Count - 1;

			if (IsArray) Output.Append('[');
			else Output.Append('{');

			if (Tabs >= 0) Output.Append(App.NewLine).Append('\t', Tabs + 1);

			foreach (var Pair in This) {
				if (!IsArray) Output.Append('"').Append(Pair.Key).Append("\":");

				if (Pair.Value == null) Output.Append("null");
				else {
					var Next = Pair.Value as JSON;
					if (Next != null) Stringify(Next, Output, Tabs >= 0 ? Tabs + 1 : -1);

					else {
						var Str = Pair.Value as string;
						if (Str != null) Output.Append('"').Append(Str.Escape()).Append('"');

						else {
							var Bool = Pair.Value as bool?;
							if (Bool != null) Output.Append(Bool == true ? "true" : "false");

							else {
								Output.Append(Pair.Value);
							}
						}
					}
				}

				if (Index++ != LastIndex) {
					Output.Append(',');

					if (Tabs >= 0) Output.Append(App.NewLine).Append('\t', Tabs + 1);
				}
				else {
					if (Tabs >= 0) {
						Output.Append(App.NewLine).Append('\t', Tabs);
					}
				}
			}

			if (IsArray) Output.Append(']');
			else Output.Append('}');
		}

		public static implicit operator JSON(string Text) {
			return Parse(Text);
		}

		public static JSON operator +(JSON Js, object Obj) {
			Js.Add(Obj);

			return Js;
		}

		public static JSON operator -(JSON Js, string Key) {
			Js[Key] = null;

			return Js;
		}
	}
}