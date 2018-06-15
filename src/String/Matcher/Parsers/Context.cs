using System;
using System.Collections.Generic;

namespace Poly {
    using Data;

    public partial class Matcher {
        class Context 
            : Stack<(_CompareDelegate Compare,
                    _CompareDelegate GotoCompare,
                    _ExtractDelegate Extract,
                    _ExtractDelegate GotoExtract,
                    _TemplateDelegate Template)> {

            public TypeInformation Type;

            public int MinimumLength;

            public Context(Type type) { 
                Type = TypeInformation.Get(type);

                Push(DefaultCompare, DefaultExtract, DefaultTemplate);
            }

            public void Push(
                _CompareDelegate compares,
                _ExtractDelegate extracts,
                _TemplateDelegate template) =>
                Push((
                    compares, compares,
                    extracts, extracts, 
                    template
                ));

            public static readonly _CompareDelegate DefaultCompare = _ => true;
            public static readonly _ExtractDelegate DefaultExtract = (_, __) => true;
            public static readonly _TemplateDelegate DefaultTemplate = (_, __) => true;
        }
    }
}