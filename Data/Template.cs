using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Data {
    public static class jsObjectExtension {
        public static string Template(this jsObject This, string Template) {
            List<string> List = new List<string>();

            for (int i = 0, Last = 0; i < Template.Length; i++) {
                string Next = Template.FirstPossible(i, "{", "[", "]", "\\");

                switch (Next) {
                    case "{":
                        var Open = Template.Find("{", i);
                        var Close = Template.Find("}", Open);
                        var Start = Close + 1;

                        if (i != Open) {
                            List.Add(Template.SubString(i, Open - i));
                        }

                        var Mod = Template.Find(':', Open, Close);

                        if (Mod == -1) {
                            Mod = Close;
                        }

                        var Name = Template.SubString(Open + 1, Mod - Open - 1);
                        var Obj = This.Get<object>(Name);

                        Open = Template.Find("{/" + Name);

                        if (Open != -1 && Obj is jsObject) {
                            Close = Template.Find("}", Open);

                            var SubTemplate = Template.SubString(Start, Open - Start);
                            var jsObj = (Obj as jsObject);


                            if (jsObj.All(pair => pair.Value is jsObject)) {
                                foreach (jsObject Sub in jsObj.Values) {
                                    List.Add(Sub.Template(SubTemplate));
                                }
                            }
                            else {
                                List.Add(jsObj.Template(SubTemplate));
                            }
                        }
                        else {
                            if (Obj != null)
                                List.Add(Obj.ToString());
                        }

                        i = Close;
                        Last = i;
                        break;

                    case "[":
                    case "]": {
                        var Index = Template.Find(Next, i);
                        List.Add(Template.SubString(i, Index - i));
                        i = Index + 1;
                        break;
                    }

                    case "\\": {
                        var Index = Template.Find(Next, i);
                        List.Add(Template.SubString(i, Index - i));
                        Close = Template.FirstPossibleIndex(Index, "{", "[", "]", "\\");

                        if (Close == -1) {
                            Close = Template.Length;
                        }

                        List.Add(Template.SubString(Index, Close - Index));
                        i = Close;
                        break;
                    }

                    default:
                        List.Add(Template.SubString(i, Template.Length - i));
                        i = Template.Length;
                        break;
                }
            }

            return string.Join("", List);
        }
        
        public static bool Extract(this jsObject This, string Template, string Data, bool IgnoreCase = false) {
            if (string.IsNullOrEmpty(Template) || string.IsNullOrEmpty(Data)) {
                return false;
            }

            if (Data.Match(Template, IgnoreCase, This) == null)
                return false;
            return true;
        }
    }
}
