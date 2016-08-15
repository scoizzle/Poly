using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Linq;

using Poly.Data;

namespace Poly.Script.Nodes {
    public class Variable : Value {
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
				else if (Elements[Index] == ContextAccess) {
					Current = Context;
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

                if ((Result = Obj.GetValue<object>(Key)) != null) {
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

		public static Node Parse(Engine Engine, StringIterator It) {
            var Start = It.Index;
            var IsGlobal = It.Consume ("Global");
			var IsStatic = IsGlobal ? false : It.Consume ("Static");
            var Delta = It.Index;

            var List = new List<Node>();

            while (!It.IsDone()) {
                if (It.Current == '.') {
                    var Name = It.Substring(Delta, It.Index - Delta);

                    if (!string.IsNullOrEmpty(Name)) {
						if (string.CompareOrdinal(Name, "_") == 0) {
							List.Add(ContextAccess);
						}
						else	
                        if (Engine.ReferencedTypes.ContainsKey(Name)) {
                            List.Add(new Helpers.SystemTypeGetter(Engine.ReferencedTypes[Name]));
                        }
                        else {
                            List.Add(new StaticValue(Name));
                        }
                    }

                    It.Tick();
                    Delta = It.Index;
                }
                else if (It.Current == '[') {
                    if (It.Index != Delta) {
                        List.Add(new StaticValue(It.Substring(Delta, It.Index - Delta)));
                    }

                    It.Tick();
                    Delta = It.Index;

                    if (It.Goto('[', ']')) {
                        var Sub = Engine.ParseValue(It.Clone(Delta, It.Index));

                        if (Sub == null)
                            return null;

                        List.Add(Sub);

                        It.Tick();
                        Delta = It.Index;
                    }
                    else return null;
                }
                else if (char.IsLetterOrDigit(It.Current) || It.Current == '_') {
                    It.Tick();
                }
                else break;
            }

            if (It.Index != Start) {
                if (It.Index != Delta) {
                    if (1 == It.Index - Delta && It[Delta] == '_') {
                        List.Add(ContextAccess);
                    }
                    else {
                        List.Add(new StaticValue(It.Substring(Delta, It.Index - Delta)));
                    }
                }

                return new Variable(Engine) {
                    Elements = List.ToArray(),
                    IsGlobal = IsGlobal,
                    IsStatic = IsStatic
                };
            }

            return null;
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
