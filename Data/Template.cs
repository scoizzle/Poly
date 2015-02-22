using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Data {
    public static class jsObjectExtension {
        private static bool Template(jsObject This, string Format, StringBuilder Output) {
            StringIterator It = new StringIterator(Format);

            for (; !It.IsDone(); It.Tick()) {
                var Current = It.Index;
                if (It.Goto('{')) {
                    while (It.IsAfter('\\')) {
                        It.Tick();
                        It.Goto('{');
                    }
                }


                Output.Append(It.String, Current, It.Index - Current);

                It.Tick();
                Current = It.Index;

                if (It.Goto('{', '}')) {
                    var NameEnd = It.String.IndexOf(':', Current, It.Index - Current);

                    if (NameEnd == -1)
                        NameEnd = It.Index;

                    var Name = It.Substring(Current, NameEnd - Current);
                    var ObjectEnd = It.Find(string.Format("{{/{0}}}", Name));

                    var Object = This[Name];

                    if (Object != null) {
                        var JObj = Object as jsObject;

                        if (JObj != null && ObjectEnd != -1) {
                            var SubTemplate = It.Substring(It.Index, ObjectEnd - It.Index);

                            foreach (jsObject Sub in JObj.Values) {
                                if (Sub == null)
                                    continue;

                                Template(Sub, SubTemplate, Output);
                            }

                            It.Index += Name.Length + 3;
                        }
                        else {
                            Output.Append(Object);
                        }
                    }
                }
                else if (It.IsAt('[') || It.IsAt(']')) {
                    It.Tick();
                }
                else return false;
            }

            return true;
        }

        public static string Template(this jsObject This, string Format) {
            StringBuilder Output = new StringBuilder();

            if (Template(This, Format, Output))
                return Output.ToString();

            return null;
        }
                
        public static bool Extract(this jsObject This, string Template, string Data, bool IgnoreCase = false) {
            if (string.IsNullOrEmpty(Template) || string.IsNullOrEmpty(Data)) {
                return false;
            }

            if (Data.Match(Template, IgnoreCase, 0, This) == null)
                return false;
            return true;
        }
    }
}
