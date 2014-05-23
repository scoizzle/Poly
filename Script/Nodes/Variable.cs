using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Node {
    public class Variable : Value {
        private class Node {
            public bool IsVariable;
            public object Obj;

            public Node(bool isVar, object Obj) {
                this.IsVariable = isVar;
                this.Obj = Obj;
            }

            public override string ToString() {
                if (Obj == null)
                    return "";

                if (IsVariable) {
                    return "[" + Obj.ToString() + "]";
                }
                else {
                    return Obj.ToString();
                }
            }
        }

        private Node[] List = null;

        public Engine Engine = null;
        public bool IsStatic = false;

        new public int Count {
            get {
                if (List != null)
                    return List.Length;
                return 0;
            }
        }

        public Variable(Engine Engine) {
            this.Engine = Engine;
        }

        public override object Evaluate(jsObject Context) {
            object Current;
            
            if (this.IsStatic)
                Current = Engine.Static;
            else
                Current = Context;

            for (int Index = 0; Index < List.Length; Index++) {
                var Node = List[Index];
                var Key = "";

                if (Node.IsVariable) {
                    var Val = GetValue(Node.Obj, Context);

                    if (Val == null)
                        return null;

                    Key = Val.ToString();
                }
                else {
                    Key = Node.Obj as string;
                }

                object Value = null;
                jsObject Object;

                if ((Object = Current as jsObject) != null) {
                    Value = Object[Key];
                }

                if (Value == null) {
                    if ((Value = GetProperty(Current, Key)) == null) { 
                        CustomTypeInstance Instance;
                        if ((Instance = Current as CustomTypeInstance) != null) {
                            Value = Function.GetFunctionHandler(Instance.Type.GetFunction(Key), Context);
                        }
                        else if (!this.IsStatic && Engine.Types.ContainsKey(Key)) {
                            Value = Engine.Types.Get<object>(Key);
                        }
                        else break;
                    }                    
                }

                if (List.Length - Index == 1) {
                    if (Value == null) {
                        CustomType Type;
                        if ((Type = Current as CustomType) != null) {
                            return Function.GetHandlerArguments(Type.Construct, Context);
                        }
                    }

                    return Value;
                }
                else {
                    Current = Value;
                }
            }

            return null;
        }

        public object Assign(jsObject Context, object Val) {
            object Current;
            
            if (this.IsStatic)
                Current = Engine.Static;
            else
                Current = Context;

            for (int Index = 0; Index < List.Length; Index++) {
                var Node = List[Index];
                var Key = "";

                if (Node.IsVariable) {
                    var V = GetValue(Node.Obj, Context);

                    if (V == null)
                        return null;

                    Key = V.ToString();
                }
                else {
                    Key = Node.Obj as string;
                }

                object Value = null;
                jsObject Object;

                if (List.Length - Index == 1) {
                    if ((Object = Current as jsObject) != null) {
                        Object[Key] = Val;
                    }
                    else if (!SetProperty(Current, Key, Val)) {
                        return null;
                    }

                    return Val;
                }
                else if ((Object = Current as jsObject) != null) {
                    Value = Object[Key];
                }                

                if (Value == null) {
                    if ((Value = GetProperty(Current, Key)) == null) {
                        if (Object != null) {
                            Value = new jsObject();
                            Object[Key] = Value;
                        }
                        else break;
                    }                    
                }

                Current = Value;
            }

            return null;
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

        private static int GetNextTokenIndex(string Text, int Index, int LastIndex) {
            var Next = Text.FirstPossibleIndex(Index, ".", "[", "]");

            if (Next == -1 || Next > LastIndex)
                Next = LastIndex;

            for (; Index < Next; Index++) {
                if (!IsValidChar(Text[Index])) {
                    break;
                }
            }

            return Index;
        }

        public static Variable Parse(Engine Engine, string Text, int Index, int LastIndex = -1) {
            return Parse(Engine, Text, ref Index, LastIndex == -1 ? Text.Length : LastIndex);
        }

        public static new Variable Parse(Engine Engine, string Text, ref int Index, int LastPossibleIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastPossibleIndex))
                return null;

            var Var = new Variable(Engine);
            var Delta = Index;
            var LastIndex = Index;
            var List = new List<Node>();

            ConsumeValidName(Text, ref LastIndex);

            if (LastIndex > LastPossibleIndex)
                LastIndex = LastPossibleIndex;

            if (Text.Compare("Static", Index)) {
                Var.IsStatic = true;
                Delta += 7;
            }

            while (Delta < LastIndex) {
                var Next = GetNextTokenIndex(Text, Delta, LastIndex);

                if (Next == -1 || Next >= LastIndex) {
                    List.Add(
                        new Node(false, Text.SubString(Delta, LastIndex - Delta))
                    );

                    Delta = LastIndex;
                }
                else if (Text[Next] == ']') {
                    List.Add(
                        new Node(true, Engine.Parse(Text, ref Delta, Next))
                    );

                    Delta = Next + 1;
                }
                else if ((Next - Delta) > 0) {
                    List.Add(
                        new Node(false, Text.SubString(Delta, Next - Delta))
                    );

                    Delta = Next + 1;
                }
                else break;
            }

            Var.List = List.ToArray();
            Index = Delta;
            return Var;
        }

        public override string ToString() {
            return string.Join(".", (IEnumerable<Node>)List);
        }
    }
}
