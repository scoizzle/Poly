using System;
using System.Collections.Generic;
using System.Text;
using Poly.Data;

namespace Poly.Script.Nodes {
    public class Variable : Node {
        Engine Engine;
        public bool IsStatic;

        public Variable(Engine Engine) {
            this.Engine = Engine;
            this.IsStatic = false;
        }

        public Variable(Engine Eng, string Text) {
            this.Engine = Eng;

            var V = Parse(Eng, Text, 0);
            if (V != null) {
                this.Elements = V.Elements;
                this.IsStatic = V.IsStatic;
            }
        }

        public override object Evaluate(jsObject Context) {
            if (Elements == null)
                return Context;

            object Current = 
                this.IsStatic ? 
                    Engine.Static :
                    Context;

            int Index = 0;
            foreach (Node N in Elements) {
                Index ++;
                string Key;
                object Value = null;

                var S = N as Types.String;
                if (S != null) {
                    Key = S.Value;
                }
                else {
                    Value = N.Evaluate(Context);

                    var Get = N as Helpers.ContextGetter;
                    if (Get != null)
                        return Value;

                    if (Value == null)
                        return null;

                    Key = Value.ToString();

                    if ((N as Helpers.SystemTypeGetter) == null)
                        Value = null;
                }

                var jO = Current as jsObject;

                object V = null;
                if (jO != null && jO.TryGetValue(Key, out V))
                    Value = V;
                else if (Value == null) {
                    var T = Current as Type;
                    if (T != null)
                        Value = GetProperty(T, null, Key);
                    else if (Current != null && (V = GetProperty(Current.GetType(), Current, Key)) != null)
                        Value = V;
                    else {
                        var I = Current as Types.ClassInstance;

                        if (I != null)
                            Value = new Event.Handler(I.Class.GetFunction(Key).Evaluate);
                        else if (!this.IsStatic && this.Engine.Types.ContainsKey(Key))
                            this.Engine.Types.TryGetValue(Key, out Value); 
                    }                   
                }

                if (Index == Elements.Length)
                    return Value;

                if (Value == null)
                    break;

                Current = Value;
            }

            return null;
        }

        public bool Assign(jsObject Context, object Val) {
            if (Elements == null)
                return false;

            object Current =
                this.IsStatic ?
                    Engine.Static :
                    Context;

            int Index = 0;
            foreach (Node N in Elements) {
                Index++;
                string Key;
                object Value = null;

                var S = N as Types.String;
                if (S != null) {
                    Key = S.Value;
                }
                else {
                    Value = N.Evaluate(Context);

                    if (Value == null)
                        return false;

                    Key = Value.ToString();
                }

                var jO = Current as jsObject;
                if (Index == Elements.Length) {
                    if (jO != null) {
                        if (Val == null)
                            jO.Remove(Key);
                        else
                            jO.AssignValue(Key, Val);
                        return true;
                    }
                    else {
                        var T = Current as Type;
                        if (T == null)
                            T = Current.GetType();

                        return SetProperty(T, Current, Key, Val);
                    }
                }

                if (jO == null || !jO.TryGetValue(Key, out Value)) {
                    if (Value == null) {
                        var T = Current as Type;
                        if (T != null)
                            Value = GetProperty(T, null, Key);
                        else if (Current != null)
                            Value = GetProperty(Current.GetType(), Current, Key);

                        if (jO != null && Value == null) {
                            Value = new jsObject();
                            jO.AssignValue(Key, Value);
                        }
                    }
                }

                if (Value == null)
                    break;

                Current = Value;
            }

            return false;
        }

        public static object GetProperty(Type Type, object Obj, string Name) {
            try {
                var Prop = Type.GetProperty(Name);

                if (Prop != null)
                    return Prop.GetValue(Obj, null);

                var Field = Type.GetField(Name);

                if (Field != null)
                    return Field.GetValue(Obj);
            }
            catch {
                App.Log.Error(
                    string.Format("Could find property or field {0} for Type {1}", Name, Type.Name)
                );
            }
            return null;
        }

        public static bool SetProperty(Type Type, object Obj, string Name, object Value) {
            try {
                if (Obj != null) {
                    var Prop = Type.GetProperty(Name);

                    if (Prop != null)
                        Prop.SetValue(Obj, Value);
                }

                var Field = Type.GetField(Name);

                if (Field != null)
                    Field.SetValue(Obj, Value);
            }
            catch {
                App.Log.Error(
                    string.Format("Could find property or field {0} for Type {1}", Name, Type.Name)
                );
                return false;
            }
            return true;
        }

        public static Variable Parse(Engine Engine, string Text, int Index, int LastIndex = -1) {
            return Parse(Engine, Text, ref Index, LastIndex == -1 ? Text.Length : LastIndex);
        }

        public static Variable Parse(Engine Engine, string Text, ref int Index, int LastPossibleIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastPossibleIndex))
                return null;

            var Var = new Variable(Engine);
            var Delta = Index;
            var List = new List<Node>();

            if (Text.Compare("Static", Index)) {
                Var.IsStatic = true;
                Delta += 7;
            }
            else if (Text.Compare("Enum", Index)) {
                if (Text.Compare(':', Index + 4)) {
                    string Type;
                    
                    var Next = Text.IndexOf(':', Index + 5);
                    if (Next == -1 || Text.IndexOf(';', Index + 5) < Next) {
                        Type = Text.Substring(":", ";", Index + 4); 
                    }
                    else {
                        Type = Text.FindMatchingBrackets(":", ":", Index);
                    }

                    if (!string.IsNullOrEmpty(Type)) {
                        List.Add(new Helpers.SystemTypeGetter(Type));
                        Delta += 6 + Type.Length;
                    }
                }
            }

            var SigFig = Delta;

            for (; Delta < LastPossibleIndex; Delta++) {
                if (Text[Delta] == '.') {
                    var Key = Text.Substring(SigFig, Delta - SigFig);
                    if (Engine.Shorthands.ContainsKey(Key)) {
                        List.Add(new Helpers.SystemTypeGetter(Engine.Shorthands[Key]));
                    }
                    else {
                        List.Add(new Types.String(Key));
                    }

                    SigFig = Delta + 1;
                }
                else if (Text[Delta] == '[') {
                    if ((Delta - SigFig) > 1) {
                        List.Add(new Types.String(Text.Substring(SigFig, Delta - SigFig)));
                    }

                    if (Text.FindMatchingBrackets("[", "]", ref Delta, ref SigFig, false)) {
                        List.Add(Engine.Parse(Text, ref Delta, SigFig));
                        Delta = ++SigFig;
                    }
                    else return null;
                }
                else if (!IsValidChar(Text[Delta])) {
                    break;
                }
            }

            if ((Delta - SigFig) > 0 && Delta <= LastPossibleIndex && Text[SigFig] != ';') {
                var Key = Text.Substring(SigFig, Delta - SigFig);

                if (Key == "_") {
                    List.Add(new Helpers.ContextGetter());
                }
                else if (Engine.Shorthands.ContainsKey(Key)) {
                    List.Add(new Helpers.SystemTypeGetter(Engine.Shorthands[Text.Substring(SigFig, Delta - SigFig)]));
                }
                else {
                    List.Add(new Types.String(Text.Substring(SigFig, Delta - SigFig)));
                }
            }

            if (List.Count == 0)
                return null;

            Var.Elements = List.ToArray();

            Index = Delta;
            return Var;
        }

        public override string ToString() {
            StringBuilder Output = new StringBuilder(IsStatic ? "Static." : string.Empty);

            foreach (var E in Elements) {
                var V = E as Variable;

                if (V != null) {
                    Output.AppendFormat("[{0}].", V.ToString());
                }
                else {
                    Output.AppendFormat("{0}.", E);
                }
            }

            return Output.ToString(0, Output.Length - 1);
        }
    }
}
