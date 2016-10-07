using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;

    public partial class Matcher {
        class Context : StringIterator {
            public int BlockIndex, BlockCount;

            internal ManagedArray<Extraction> Extractions;

            public Context(string data, int blockCount, int index = 0, int length = 0) : base(data) {
                Extractions = new ManagedArray<Extraction>();
                BlockCount = blockCount;

                if (length == 0) Length = data.Length;
                if (index >= 0 && index < Length) Index = index;
                else Index = 0;
            }

            public void AddExtraction(string key, int start, int len) {
                Extractions.Add(new StringExtraction { key = key, start = start, len = len });
            }

            public void AddExtraction(string key, int start, int len, StringMatching.ModDelegate[] mods) {
                Extractions.Add(new ModifiedExtraction { key = key, start = start, len = len, Modifiers = mods });
            }

            public void AddExtraction(int kStart, int kLen, int vStart, int vLen) {
                Extractions.Add(new KeyValuePairExtraction(
                    new ModifiedExtraction { start = kStart, len = kLen },
                    new ModifiedExtraction { start = vStart, len = vLen }
                ));
            }

            public void ExtractInto(jsObject Storage) {
                var List = Extractions.Elements;
                for (var i = 0; i < Extractions.Count; i++) {
                    var ext = List[i];

                    var key = ext.Key(this);
                    var val = ext.Value(this);

                    Storage.AssignValue(key ?? Storage.Count.ToString(), val);
                }
            }

            public class Extraction {
                public virtual string Key(Context Context) { return null; }
                public virtual object Value(Context Context) { return null; }
            }

            public class StringExtraction : Extraction {
                public string key;
                public int start, len;

                public override string Key(Context Context) {
                    return key;
                }

                public override object Value(Context Context) {
                    return Context.Substring(start, len);
                }
            }

            public class ModifiedExtraction : StringExtraction {
                public StringMatching.ModDelegate[] Modifiers;

                public override object Value(Context Context) {
                    return Modify(Context.Substring(start, len));
                }

                private object Modify(string value) {
                    if (Modifiers == null)
                        return value;

                    object Value = value;

                    var i = 0;
                    var Len = Modifiers.Length;

                    do {
                        Value = Modifiers[i](Value as string);
                    }
                    while (Value is string && i++ < Len);

                    return Value;
                }
            }

            public class KeyValuePairExtraction : Extraction {
                public Extraction key, val;
                
                public KeyValuePairExtraction(Extraction Key, Extraction Val) {
                    key = Key;
                    val = Val;
                }

                public KeyValuePairExtraction(bool chrono, ManagedArray<Extraction> list) {
                    if (chrono) {
                        key = list.Elements[0];
                        val = list.Elements[1];
                    }
                    else {
                        key = list.Elements[1];
                        val = list.Elements[0];
                    }
                }

                public override string Key(Context Context) {
                    return key.Value(Context) as string;
                }

                public override object Value(Context Context) {
                    return val.Value(Context);
                }
            }

            public class GroupedExtraction : Extraction {
                public Extraction[] Extracts;

                public GroupedExtraction(ManagedArray<Extraction> exts) {
                    Extracts = exts.ToArray();
                }

                public override object Value(Context Context) {
                    var group = new jsObject();


                    foreach (var ext in Extracts) {
                        if (ext == null) continue;
                        group.AssignValue(ext.Key(Context) ?? group.Count.ToString(), ext.Value(Context));
                    }

                    return group;
                }
            }

            public class ValueExtraction : Extraction {
                public Extraction val;

                public ValueExtraction(ManagedArray<Extraction> exts) {
                    val = exts.FirstOrDefault();
                }

                public override object Value(Context Context) {
                    return val?.Value(Context);
                }
            }
        }
    }
}
