using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly.Data;

namespace Poly.Script.Node {
    public class Include : Expression {
        public string Name = string.Empty;

        public override string ToString() {
            return "include '" + Name + "'";
        }

        public static new object Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("include", Index)) {
                var Delta = Index += 7;
                ConsumeWhitespace(Text, ref Delta);

                var Str = String.Parse(Engine, Text, ref Delta, LastIndex) as string;

                if (Str != null) {
                    if (File.Exists(Str)) {
                        var Obj = new Expression();
                        
                        Obj = Engine.Parse(File.ReadAllText(Str), 0, Obj) as Expression;

                        if (Obj != null) {
                            Engine.Includes.Add(Str);

                            if (Obj.Count == 1) {
                                Index = Delta;
                                return Obj.ElementAt(0);
                            }
                            else {
                                Index = Delta;
                                return Obj;
                            }
                        }
                    }
                }
                  
            }

            return null;
        }
    }
}
