namespace Poly.String {
    using Data;

    public partial class Matcher<T> {
        static class Wildcard {
            public static bool Parse(StringIterator it, Context context) {
                if (it.Consume('*')) {
                    var next = context.Peek();
                    var has_next = Matcher<T>.Parse(it, context);

                    if (has_next)
                        next = context.Peek();

                    context.Push(next.GotoCompare, next.GotoExtract, next.GotoRawExtract, next.Template, next.RawTemplate);
                    return true;
                }

                return false;
            }
        }
    }
}