using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Linq;

using Poly.Data;

namespace Poly.Script.Nodes {
    public class Variable : Node {
        Engine Engine;
        public bool IsStatic, IsGlobal;

        public Variable(Engine Engine) {
            this.Engine = Engine;
            this.IsGlobal = this.IsStatic = false;
        }

        public override object Evaluate(jsObject Context) {
            if (Elements == null)
                return null;

            object Current;
            if (IsGlobal)
                Current = App.GlobalContext;
            else
            if (IsStatic)
                Current = Engine.Static;
            else
                Current = Context;

            for (int Index = 0; Index < Elements.Length; Index++) {
                string Key;

                if (Elements[Index] is StaticValue) {
                    Key = (Elements[Index] as StaticValue).Value as string;
                }
                else if (Elements[Index] is Helpers.SystemTypeGetter) {
                    Current = Elements[Index].Evaluate(Context);
                    continue;
                }
                else {
                    var Result = Elements[Index].Evaluate(Context);

                    if (Result == null)
                        continue;
                    else
                        Key = Result.ToString();
                }

                Current = GetNextValue(Current, Key);
            }

            return Current;
        }

        public bool Assign(jsObject Context, object Val) {
            if (Elements == null)
                return false;

            object Current, Next;
            string Key;

            if (IsGlobal)
                Current = App.GlobalContext;
            else
                if (IsStatic)
                    Current = Engine.Static;
                else
                    Current = Context;

            for (int Index = 0; Index < Elements.Length; Index++) {
                if (Elements[Index] is StaticValue) {
                    Key = (Elements[Index] as StaticValue).Value as string;
                }
                else if (Elements[Index] is Helpers.SystemTypeGetter) {
                    Current = Elements[Index].Evaluate(Context);
                    continue;
                }
                else {
                    var Result = Elements[Index].Evaluate(Context);

                    if (Result == null)
                        break;
                    else
                        Key = Result.ToString();
                }

                if (Index == Elements.Length - 1) {
                    return SetNextValue(Current, Key, Val);
                }
                else {
                    Next = GetNextValue(Current, Key);

                    if (Next == null) {
                        Next = new jsObject();

                        if (!SetNextValue(Current, Key, Next))
                            return false;
                    }

                    Current = Next;
                }
            }

            return false;
        }

        private object GetNextValue(object Current, string Key) {
            if (Current is jsObject) {
                object Result = null;
                var Obj = Current as jsObject;

                if (Obj.TryGet(Key, out Result)) {
                    return Result;
                }
                else
                if (Current is Types.ClassInstance) {
                    Function F = (Current as Types.ClassInstance).GetFunction(Key);

					if (F != null)
                    	return new Event.Handler(F.Evaluate);
                }
            }

            if (Current is Class) {
                Function F = (Current as Class).StaticFunctions[Key];

				if (F != null)
                	return new Event.Handler(F.Evaluate);
            }
            else {
                object C = Engine.Types.Get(Key);

                if (C != null)
                    return C;
            }
            
            if (Current is Type) {
                return GetObjectValue(Current as Type, Current, Key);
            }
            
            if (Current != null) {
                return GetObjectValue(Current.GetType(), Current, Key);
            }

            return null;
        }

        private bool SetNextValue(object Current, string Key, object Value) {
            if (Current is jsObject) {
                var Obj = Current as jsObject;

                if (Value == null)
                    Obj.Remove(Key);
                else
                    Obj.AssignValue(Key, Value);

                return true;
            }
            else
                if (Current is Type) {
                    return SetObjectValue(Current as Type, Current, Key, Value);
                }
                else {
                    return SetObjectValue(Current.GetType(), Current, Key, Value);
                }
        }

        private object GetObjectValue(Type Type, object Obj, string Name) {
            var Prop = Type.GetProperty(Name);

            if (Prop != null) {
                try {
                    return Prop.GetValue(Obj, null);
                }
                catch { }
            }

            var Field = Type.GetField(Name);

            if (Field != null) {
                try {
                    return Field.GetValue(Obj);
                }
                catch { }
            }

            return null;
        }

        private bool SetObjectValue(Type Type, object Obj, string Name, object Value) {
            var Prop = Type.GetProperty(Name);

            if (Prop != null) {
                try {
                    Prop.SetValue(Obj, Value);
                    return true;
                }
                catch { }
            }

            var Field = Type.GetField(Name);

            if (Field != null) {
                try {
                    Field.SetValue(Obj, Value);
                    return true;
                }
                catch { }
            }

            return false;
        }

        public static Variable Parse(Engine Engine, string Text, int Index, int LastIndex = -1) {
            return Parse(Engine, Text, ref Index, LastIndex == -1 ? Text.Length : LastIndex);
        }

        public static Variable Parse(Engine Engine, string Text, ref int Index, int LastPossibleIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastPossibleIndex))
                return null;

            var Delta = Index;
            var End = Index;
            ConsumeValidName(Text, ref End);

            if (Delta == End)
                return null;

            bool IsStatic, IsGlobal;
            IsStatic = IsGlobal = false;

            if (Text.Compare("Global", Delta)) {
                IsGlobal = true;
                Delta += 6;
            }
            else 
            if (Text.Compare("Static", Delta)) {
                IsStatic = true;
                Delta += 6;
            }
            else
            if (Text.Compare("_", Delta)) {
                Index++;
                return new Variable(Engine) { Elements = new Node[0] };
            }

            var SigFig = Delta;
            var List = new List<Node>();

            for (; Delta < End; Delta++) {
                if (Text[Delta] == '.') {
                    var Key = Text.Substring(SigFig, Delta - SigFig);
                    SigFig = Delta + 1;

                    if (string.IsNullOrEmpty(Key))
                        continue;

                    if (List.Count == 0 && Engine.ReferencedTypes.ContainsKey(Key)) {
                        List.Add(new Helpers.SystemTypeGetter(Engine.ReferencedTypes[Key]));
                    }
                    else {
                        List.Add(new StaticValue(Key));
                    }
                }
                else
                if (Text[Delta] == '[') {
                    if (Delta != SigFig) {
                        List.Add(new StaticValue(Text.Substring(SigFig, Delta - SigFig)));
                    }

                    if (Text.FindMatchingBrackets("[", "]", ref Delta, ref SigFig, false)) {
                        List.Add(Engine.Parse(Text, ref Delta, SigFig));
                        Delta = ++SigFig;
                    }
                    else return null;
                }
                else
                if (!IsValidChar(Text[Delta]))
                    break;
            }

            if (SigFig < End) {
                List.Add(new StaticValue(Text.Substring(SigFig, End - SigFig)));
            }

            Index = End;
            return new Variable(Engine) {
                Elements = List.ToArray(),
                IsGlobal = IsGlobal,
                IsStatic = IsStatic
            };
        }

		private IEnumerable<string> ToStringParts() {
			if (IsGlobal)
				yield return "Global";

			if (IsStatic)
				yield return "Static";

			if (Elements == null)
				yield return "_";

			foreach (var e in Elements)
				yield return e.ToString ();
		}

        public override string ToString() {
			return string.Join (".", ToStringParts ());
        }
    }
}
