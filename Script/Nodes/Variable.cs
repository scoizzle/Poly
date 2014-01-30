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

            var List = this.ToList();

            for (int i = 0; i < List.Count; i++) {
                var Obj = List[i].Value;

                var V = Obj is Node ?
                    GetValue(Obj, Current as jsObject) :
                    Get(Obj as string, Current as jsObject);

                if (Current is jsObject && V != null) {
                    if ((List.Count - i) == 1) {
                        return V;
                    }
                    else {
                        Current = V;
                    }
                }

                if (Current != null) {
                    var Name = Obj.ToString();
                    var Prop = GetProperty(Current, Name);

                    if (Prop != null) {
                        if ((List.Count - i) == 1) {
                            return Prop;
                        }
                        else {
                            Current = Prop;
                        }
                    }
                }

                if ((List.Count - i) == 1 && V != null) {
                    return Current;
                }
            }

            return null;
        }

        public object Assign(jsObject Context, object Value) {
            jsObject CurrentObj = IsStatic ?
                Engine.StaticObjects :
                Context;

            var List = this.ToList();

            for (int i = 0; i < List.Count; i++) {
                var O = List[i].Value;

                if (O is string) {
                    var N = O as string;

                    if ((List.Count - i) == 1) {
                        Set(N, CurrentObj, Value);
                        break;
                    }

                    var V = Get(N, CurrentObj);

                    if (V == null) {
                        if (i > 0)
                            V = Get(N, Context);
                    }

                    if (V == null && (List.Count - i) > 1) {
                        V = new jsObject();
                        Set(N, CurrentObj, V);
                    }

                    if (V is jsObject)
                        CurrentObj = (V as jsObject);
                    else return null;                        
                }
                else if (O is Node) {
                    if ((List.Count - i) == 1) {
                        return null;
                    }

                    var V = (O as Node).Evaluate(Context);

                    if (V is jsObject) {
                        CurrentObj = (V as jsObject);
                    }
                }
                else break;
            }

            return Value;
        }

        public static Variable Parse(Engine Engine, string Text, int Index) {
            return Parse(Engine, Text, ref Index, Text.Length - 1);
        }

        public static new Variable Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            var Open = Delta;
            var Var = new Variable(Engine);

            for (; Delta <= LastIndex && Delta < Text.Length; Delta++) {
                if (Text[Delta] == '.') {
                    if (Open == Delta)
                        return null;

                    var Name = Text.Substring(Open, Delta - Open);

                    if (Name == "Static") {
                        Var.IsStatic = true;
                    }
                    else {
                        Var.Add(Name);
                    }
                    Open = ++Delta;
                }
                else if (IsValidChar(Text[Delta])) {
                    continue;
                }
                else if (Text[Delta] == '(') {
                    ConsumeEval(Text, ref Delta);

                    Var.Add(
                        Call.Parse(Engine, Text, ref Open, Delta)
                    );

                    if (Text[Delta] == '.')
                        continue;
                }
                else if (Text[Delta] == '[') {
                    var x = Delta;
                    ConsumeBlock(Text, ref Delta);

                    Var.Add(
                        Engine.Parse(Text, ref x, Delta)
                    );

                    if (Text[Delta] == '.') {
                        Open = ++Delta;
                    }

                }
                else break;
            } 
            
            if (Open != Delta) {
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
