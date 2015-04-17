﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;

using Poly.Data;

namespace Poly.Script.Nodes {
    public class Variable : Node {
        Engine Engine;
        public bool IsStatic;

        Type RequestedType;

        public Variable(Engine Engine) {
            this.Engine = Engine;
            this.IsStatic = false;
            this.RequestedType = null;
        }

        public Variable(Engine Eng, string Text) {
            this.Engine = Eng;

            var V = Parse(Eng, Text, 0);
            if (V != null) {
                this.Elements = V.Elements;
                this.IsStatic = V.IsStatic;
                this.RequestedType = V.RequestedType;
            }
        }

        public override object Evaluate(jsObject Context) {
            if (Elements == null)
                return null;

            object Current =
                this.IsStatic ?
                    Engine.Static :
                    Context;

            for (int i = 0; i < Elements.Length; i++) {
                var Node = Elements[i];
                var Str = Node as Types.String;
                var String = default(string);
                var Key = default(object);
                var Value = default(object);

                if (Str != null) { 
                    String = Str.Value;
                }
                else if (Node is Helpers.SystemTypeGetter) {
                    Current = Node.Evaluate(Context);

                    if (Elements.Length - i == 1)
                        return Current;

                    continue;
                }
                else {
                    Key = Node.Evaluate(Context);

                    if (Key != null) {
                        String = Key.ToString();
                    }
                    else if (Node.Elements == null && Elements.Length - i == 1) {
                        return Context;
                    }
                    else return null;
                }

                var Object = Current as jsObject;

                if (Object != null) {
                    if (!Object.GetValue(String, out Value)) {
                        Class Class;
                        Function Func;

                        if (Current is Types.ClassInstance) {
                            Class = (Current as Types.ClassInstance).Class;

                            Func = Class.GetFunction(String);

                            if (Func != null)
                                Value = new Event.Handler(Func.Evaluate);
                        }
                        else {
                            Engine.Types.TryGetValue(String, out Value);
                        }
                    }
                }

                if (Value == default(object) && Current != null) {
                    Class Class;
                    Function Func;

                    if (Current is Class) {
                        Class = Current as Class;

                        if (Class.StaticFunctions.TryGet(String, out Func)) {
                            Value = new Event.Handler(Func.Evaluate);
                        }
                    }
                    else if (Key is int && Current is string) {
                        String = Current as string;
                        var Int = (int)(Key);

                        if (Int > -1 && Int < String.Length)
                            Value = String[Int];
                    }
                    else {
                        var Type = Current as Type;

                        if (Type == null) {
                            Type = Current.GetType();
                        }

                        Value = GetProperty(Type, Current, String);
                    }
                }

                if (Elements.Length - i == 1)
                    return Value;

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

            for (int i = 0; i < Elements.Length; i++) {
                var Node = Elements[i];
                var Str = Node as Types.String;
                var String = default(string);

                if (Str != null) {
                    String = Str.Value;
                }
                else {
                    var Key = Node.Evaluate(Context) as string;

                    if (Key != null)
                        String = Key.ToString();
                    else return false;
                }

                var Value = default(object);
                var Object = Current as jsObject;

                if (Object != null) {
                    if (Elements.Length - i == 1) {
                        if (Val == null) {
                            Object.Remove(String);
                        }
                        else {
                            Object.AssignValue(String, Val);
                        }
                        return true;
                    }

                    if (!Object.TryGetValue(String, out Value)) {
                        var Instance = Current as Types.ClassInstance;

                        if (Instance != null) {
                            var Func = Instance.Class.Functions[String];

                            if (Func != null)
                                Value = new Event.Handler(Func.Evaluate);
                        }
                    }
                }

                if (Value == default(object) && Current != null) {
                    var Type = Current as Type;

                    if (Type == null)
                        Type = Current.GetType();

                    if (Elements.Length - i == 1) {
                        return SetProperty(Type, Current, String, Val);
                    }

                    Value = GetProperty(Type, Current, String);

                    if (Value == default(object) && Object != default(jsObject)) {
                        Value = new jsObject();
                        Object.AssignValue(String, Value);
                    }
                }

                Current = Value;
            }

            return false;        
        }
        
        private object GetProperty(Type Type, object Obj, string Name) {
            var Prop = RequestedType == null ? 
                Type.GetProperty(Name) :
                Type.GetProperty(Name, RequestedType);

            if (Prop != null)
                return Prop.GetValue(Obj, null);

            var Field = Type.GetField(Name);

            if (Field != null)
                return Field.GetValue(Obj);

            return null;
        }

        private bool SetProperty(Type Type, object Obj, string Name, object Value) {
            if (Obj != null) {
                var Prop = RequestedType == null ?
                    Type.GetProperty(Name) :
                    Type.GetProperty(Name, RequestedType);

                if (Prop != null) {
                    Prop.SetValue(Obj, Value);
                    return true;
                }
            }

            var Field = Type.GetField(Name);

            if (Field != null) {
                Field.SetValue(Obj, Value);
                return true;
            }

            return false;
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
                        Type = Text.Substring(":", ";", Index); 
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

                    if (!string.IsNullOrEmpty(Key)) {
                        if (Engine.Shorthands.ContainsKey(Key) && List.Count == 0) {
                            List.Add(new Helpers.SystemTypeGetter(Engine.Shorthands[Key]));
                        }
                        else {
                            List.Add(new Types.String(Key));
                        }
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
                    List.Add(new Variable(Engine));
                }
                else if (Engine.Shorthands.ContainsKey(Key) && List.Count == 0) {
                    List.Add(new Helpers.SystemTypeGetter(Engine.Shorthands[Text.Substring(SigFig, Delta - SigFig)]));
                }
                else {
                    List.Add(new Types.String(Text.Substring(SigFig, Delta - SigFig)));
                }
            }

            if (List.Count == 0)
                return null;


            Index = Delta;
            Var.Elements = List.ToArray();

            ConsumeWhitespace(Text, ref Delta);
            if (Text.Compare("as", Delta)) {
                Delta += 2;
                ConsumeWhitespace(Text, ref Delta);

                int Start = Delta;
                ConsumeValidName(Text, ref Delta);

                if (Delta != Start) {
                    var Name = Text.Substring(Start, Delta - Start);

                    if (Engine.Shorthands.ContainsKey(Name)) {
                        Var.RequestedType = Helpers.SystemTypeGetter.GetType(Engine.Shorthands[Name]);
                    }
                    else {
                        Var.RequestedType = Helpers.SystemTypeGetter.GetType(Name);
                    }

                    Index = Delta;
                }
            }
            else if (List.Count == 1) {
                var First = List[0] as Variable;

                if (First != null && First.RequestedType != null) {
                    return First;
                }
            }

            return Var;
        }

        public override string ToString() {
            if (Elements == null)
                return "_";

            StringBuilder Output = new StringBuilder(IsStatic ? "Static." : string.Empty);

            foreach (var E in Elements) {
                var V = E as Variable;

                if (V != null) {
                    Output.AppendFormat("[{0}].", V);
                }
                else {
                    Output.AppendFormat("{0}.", E);
                }
            }

            return Output.ToString(0, Output.Length - 1);
        }
    }
}
