using System;
using System.Collections.Generic;

namespace Poly.String {
    using Data;

    public partial class Matcher<T> {
        protected class Context : Stack<(
            CompareDelegate Compare, CompareDelegate GotoCompare,
            ExtractDelegate Extract, ExtractDelegate GotoExtract,
            RawExtractDelegate RawExtract, RawExtractDelegate GotoRawExtract,
            TemplateDelegate Template, RawTemplateDelegate RawTemplate)> {

            public int MinimumLength;
            public readonly TypeInformation TypeInfo;

            public Context(TypeInformation info) {
                TypeInfo = info;

                Push(DefaultCompare, DefaultExtract, DefaultRawExtract, DefaultTemplate, DefaultRawTemplate);
            }

            public void Push(
                CompareDelegate compares,
                ExtractDelegate extracts,
                RawExtractDelegate raw_extracts,
                TemplateDelegate template,
                RawTemplateDelegate raw_template) =>
                Push((
                    compares, compares,
                    extracts, extracts, 
                    raw_extracts, raw_extracts,
                    template, raw_template
                ));

            public virtual bool GetMember(string name, out Member member) =>
                TypeInfo.Members.TryGetValue(name, out member);

            public static readonly CompareDelegate DefaultCompare = _ => true;
            public static readonly ExtractDelegate DefaultExtract = (_, __) => true;
            public static readonly RawExtractDelegate DefaultRawExtract = (_, __) => true;
            public static readonly TemplateDelegate DefaultTemplate = (_, __) => true;
            public static readonly RawTemplateDelegate DefaultRawTemplate = (_, __) => true;
        }
    }
}