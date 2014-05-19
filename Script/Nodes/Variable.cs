using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Node {
    public class Variable : Value {
        public bool IsStatic = false, IsSimple = false;
        public Engine Engine = null;

        public Variable(Engine Engine, params Node[] Name) {
            this.Engine = Engine;

            foreach (var Part in Name) {
                Add(Part);
            }
        }

		public override object Evaluate(jsObject Context) {
            if (IsSimple) {
                return Get(string.Join(".", this.Values), Context);
			}

            bool CurrentIsJsObject = true;
            jsObject CurrentAsJs;

            object Current = IsStatic ?
                (CurrentAsJs = Engine.Static) :
                (CurrentAsJs = Context);

            int Index = 0;
            foreach (var Raw in this.Values) {
                var Obj = Raw is Node ?
                    GetValue(Raw, Context) :
                    Raw;

                if (Obj == null)
                    return null;

                object Value = CurrentIsJsObject ?
                    Value = GetValue(Obj, CurrentAsJs) :
                    Obj;

                if (Value == Obj) {
					if (CurrentIsJsObject) {
						Value = CurrentAsJs[Obj.ToString()];
                    }
                    else {
                        Value = Value is CustomType ?
                            (Value as CustomType).Construct :
                            null;
                    }
                } 
                
                if (Value == null) {
                    Value = GetProperty(Current, Obj as string);

                    if (Value == null) {
                        if (Current is CustomTypeInstance) {
                            Value = Function.GetFunctionHandler(
                                Function.Get(Engine, Obj as string, Current), 
                                Context
                            );
                        }
                        else {
                            jsObject Collection;
                            if (Engine.Types.TryGetValue(Obj as string, out Collection)) {
                                Value = Collection;
                            }
                        }

                        if (Value == null) {
                            break;
                        }
                    }
                }

                if ((Count - Index) == 1)
                    return Value;

                Index++;
                Current = Value;
                CurrentAsJs = AsJsObject(Current);
                CurrentIsJsObject = CurrentAsJs != null;
            }

            return null;
        }

        public object Assign(jsObject Context, object Val) {
            if (IsSimple) {
                Set(string.Join(".", this.Values), Context, Val);
                return Val;
            }

            bool CurrentIsJsObject = true;
            jsObject CurrentAsJs;

            object Current = IsStatic ?
                (CurrentAsJs = Engine.Static) :
                (CurrentAsJs = Context);

            int Index = 0;
            foreach (var Raw in this.Values) {
                var Obj = Raw is Node ?
                    GetValue(Raw, Context) :
                    Raw;

                if ((Count - Index) == 1) {
                    if (CurrentIsJsObject) {
                        CurrentAsJs[Obj.ToString()] = Val;
                        return Val;
                    }
                    else {
                        SetProperty(Current, Obj.ToString(), Val);
                        return Val;
                    }
                }

                if (Obj == Raw) {
                    if (CurrentIsJsObject) {
                        Obj = CurrentAsJs.Get<object>(Obj.ToString());
                    }
                }

                if (Obj == null) {
                    Obj = GetProperty(Current, Raw.ToString());

                    if (Obj == null) {
                        Obj = new jsObject();

                        if (CurrentIsJsObject) {
                            CurrentAsJs[Raw.ToString()] = Obj;
                        }
                        else {
                            SetProperty(Current, Raw.ToString(), Val);
                        }
                    }
                }

                Index++;
                Current = Obj;
                CurrentAsJs = AsJsObject(Current);
                CurrentIsJsObject = CurrentAsJs != null;
            }

            return null;

        }

        public static Variable Parse(Engine Engine, string Text, int Index, int LastIndex = -1) {
            return Parse(Engine, Text, ref Index, LastIndex == -1 ? Text.Length - 1 : LastIndex);
        }
        
        public static new Variable Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            var Open = Delta;
            var Var = new Variable(Engine);

            for (; Delta <= LastIndex && Delta < Text.Length; Delta++) {
                if (Text[Delta] == '.') {
                    if (Open >= Delta)
                        return null;

                    var Name = Text.SubString(Open, Delta - Open);

                    if (Name.Compare("Static")) {
                        Var.IsStatic = true;
                    }
                    else if (Name.Compare("Simple")) {
                        Var.IsSimple = true;
                    }
                    else {
                        Var.Add(Name);
                    }
                    Open = Delta + 1;
                }
                else if (IsValidChar(Text[Delta])) {
                    continue;
                }
                else if (Text[Delta] == '[') {
                    if ((Delta - Open) != 0) {
                        Var.Add(Text.SubString(Open, Delta - Open));
                    }

                    var x = Delta + 1;
                    ConsumeBlock(Text, ref Delta);

                    Var.Add(
                        Engine.Parse(Text, ref x, Delta - 1)
                    );

                    Open = Delta + 1;
                }
                else break;
            } 
            
            if (Open < Delta) {
                Var.Add(Text.Substring(Open, Delta - Open));
            }

            Index = Delta;
            return Var;
        }

        public override string ToString() {
            return string.Join(".", Values);
        }

        public static object GetProperty(object Obj, string Name) {
            if (Obj == null)
                return null;

            var Type = Obj.GetType();

            try {
                var PropInfo = Type.GetProperty(Name);

                if (PropInfo != null) {
                    return PropInfo.GetValue(Obj, null);
                }
            }
            catch { }

            return null;
        }

        public static bool SetProperty(object Obj, string Name, object Value) {
            if (Obj == null)
                return false;

            var Type = Obj.GetType();

            try {
                var PropInfo = Type.GetProperty(Name);

                if (PropInfo != null) {
                    PropInfo.SetValue(Obj, Value, null);
                    return true;
                }
            }
            catch { }

            return false;
        }

        public static object Eval(Engine Engine, string Key, jsObject Context) {
            var Var = Parse(Engine, Key, 0);

            if (Var != null) {
                return Var.Evaluate(Context);
            }

            return null;
        }

        public static object Get(string Key, jsObject Context) {
            if (Context != null) {
                return Context[Key];
            }

            return null;
        }

        public static void Set(string Key, jsObject Context, object Data) {
            if (Context == null || string.IsNullOrEmpty(Key))
                return;
            
            Context[Key] = Data;
        }

        public static void Move(string Key, jsObject Old, jsObject New) {
            Set(Key, New, Get(Key, Old));
        }
    }
}
