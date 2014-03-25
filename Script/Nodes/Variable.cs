using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Node {
    public class Variable : Value {
        public bool IsStatic = false;
        public Engine Engine = null;

        public Variable(Engine Engine, params Node[] Name) {
            this.Engine = Engine;

            foreach (var Part in Name) {
                Add(Part);
            }
        }

        public string GetName() {
            return string.Join(".", this.Values);
        }

        public override object Evaluate(jsObject Context) {
            object Current = IsStatic ? 
                Engine.StaticObjects : 
                Context;

            int Count = this.Count, i = 0;
            foreach (var rObj in this.Values) {
                var Obj = rObj;
                var Node = Obj as Node;

                if (Node != null) {
                    Obj = Node.Evaluate(Context);
                }

                var Value = GetValue(Obj, Current as jsObject);

                if (Obj == Value) {
                    var jObj = Current as jsObject;

                    if (jObj != null) {
                        Value = jObj.Get<object>(Obj.ToString());
                    }
                }

                if (Value == null) {
                    Value = GetProperty(Current, Obj.ToString());

                    if (Value == null) {
                        if (Current is CustomTypeInstance) {
                            Value = Function.Get(Engine, Obj.ToString(), Current);

                            if (Value == null) {
                                break;
                            }
                        }
                        else break;
                    }
                }

                if ((Count - i) == 1) {
                    return Value;
                }
                Current = Value;
                i++;
            }

            return null;
        }

        public object Assign(jsObject Context, object Val) {
            object Current = IsStatic ? 
                Engine.StaticObjects : 
                Context;

            int Count = this.Count, i = 0;
            foreach (var Obj in this.Values) {
                if ((Count - i) == 1) {
                    if (Current is jsObject) {
                        Set(GetValue(Obj, Context).ToString(), Current as jsObject, Val);
                        return Val;
                    }
                    else {
                        SetProperty(Current, Obj.ToString(), Val);
                        return Val;
                    }
                }
                else {
                    var Value = GetValue(Obj, Current as jsObject);

                    if (Obj == Value) {
                        Value = Context.Get<object>(Obj.ToString());
                    }

                    if (Value == null) {
                        Value = GetProperty(Current, Obj.ToString());

                        if (Value == null) {
                            Value = new jsObject();

                            if (Current is jsObject) {
                                Set(Obj.ToString(), Current as jsObject, Value);
                            }
                            else {
                                SetProperty(Current, Obj.ToString(), Val);
                            }
                        }
                    }

                    Current = Value;
                }

                i++;
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

                    var Name = Text.Substring(Open, Delta - Open);

                    if (Name == "Static") {
                        Var.IsStatic = true;
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
            return GetName();
        }

        public object GetProperty(object Obj, string Name) {
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

        public bool SetProperty(object Obj, string Name, object Value) {
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
