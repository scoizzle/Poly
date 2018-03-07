namespace Poly.String.Matcher.Parsers {

    using Data;

    public static class Extract {

        private static ExtractDelegate Handler(string key) =>
            (it, set) =>
                set(key, it.ToString());

        private static ExtractDelegate Handler(string key, ExtractDelegate next) =>
            (it, set) =>
                next(it, set) &&
                set(key, it.ToString());

        private static ExtractDelegate Handler(string key, Serializer serializer) =>
            (it, set) =>
                serializer.DeserializeObject(it, out object value) &&
                set(key, value);

        private static ExtractDelegate Handler(string key, Serializer serializer, ExtractDelegate next) =>
            (it, set) =>
                next(it, set) &&
                serializer.DeserializeObject(it, out object value) &&
                set(key, value);

        private static ExtractDelegate OptionalHandler(string key) =>
            (it, set) =>
                set(key, it.ToString()) ||
                true;

        private static ExtractDelegate OptionalHandler(string key, ExtractDelegate next) =>
            (it, set) =>
                next(it, set) ?
                    (set(key, it.ToString()) || true) :
                    true;

        private static ExtractDelegate OptionalHandler(string key, Serializer serializer) =>
            (it, set) =>
                serializer.DeserializeObject(it, out object value) ?
                    (set(key, value) || true) :
                    true;

        private static ExtractDelegate OptionalHandler(string key, Serializer serializer, ExtractDelegate next) =>
            (it, set) =>
                next(it, set) && serializer.DeserializeObject(it, out object value) ?
                    (set(key, it.ToString()) || true) :
                    true;

        private static TemplateDelegate Templater(string key) {
            return (it, get) => {
                if (get(key, out object value) && value != null) {
                    it.Append(value);
                    return true;
                }

                return false;
            };
        }

        private static TemplateDelegate Templater(string key, TemplateDelegate next) {
            return (it, get) => {
                if (get(key, out object value) && value != null) {
                    it.Append(value);

                    return next(it, get);
                }

                return false;
            };
        }

        private static Matcher.TemplateDelegate Templater(string key, Serializer serializer) {
            return (it, get) => {
                if (get(key, out object value) && value != null) {
                    if (serializer.SerializeObject(it, value))
                        return true;
                }

                return false;
            };
        }

        private static Matcher.TemplateDelegate Templater(string key, Serializer serializer, Matcher.TemplateDelegate next) {
            return (it, get) => {
                if (get(key, out object value) && value != null) {
                    if (serializer.SerializeObject(it, value))
                        return next(it, get);
                }

                return false;
            };
        }

        private static Matcher.TemplateDelegate OptionalTemplater(string key) {
            return (it, get) => {
                if (get(key, out object value) && value != null)
                    it.Append(value);

                return true;
            };
        }

        private static Matcher.TemplateDelegate OptionalTemplater(string key, Matcher.TemplateDelegate next) {
            return (it, get) => {
                if (get(key, out object value) && value != null)
                    it.Append(value);

                return next(it, get);
            };
        }

        private static Matcher.TemplateDelegate OptionalTemplater(string key, Serializer serializer) {
            return (it, get) => {
                if (get(key, out object value) && value != null)
                    serializer.SerializeObject(it, value);

                return true;
            };
        }

        private static Matcher.TemplateDelegate OptionalTemplater(string key, Serializer serializer, Matcher.TemplateDelegate next) {
            return (it, get) => {
                if (get(key, out object value) && value != null)
                    serializer.SerializeObject(it, value);

                return next(it, get);
            };
        }

        private static bool parse_internal(
            string key,
            string type,
            bool optional,
            out ExtractDelegate go_to,
            out ExtractDelegate extract,
            out TemplateDelegate template,
            bool has_next,
            ExtractDelegate next_goto,
            ExtractDelegate next_extract,
            TemplateDelegate next_template) {
            var serial = Serializer.GetCached(type);

            if (serial == null) {
                if (optional) {
                    if (has_next) {
                        go_to = extract = OptionalHandler(key, next_goto);
                        template = OptionalTemplater(key, next_template);
                    }
                    else {
                        go_to = extract = OptionalHandler(key);
                        template = OptionalTemplater(key);
                    }
                }
                else {
                    if (has_next) {
                        go_to = extract = Handler(key, next_goto);
                        template = Templater(key, next_template);
                    }
                    else {
                        go_to = extract = Handler(key);
                        template = Templater(key);
                    }
                }
            }
            else {
                if (optional) {
                    if (has_next) {
                        go_to = extract = OptionalHandler(key, serial, next_goto);
                        template = OptionalTemplater(key, serial, next_template);
                    }
                    else {
                        go_to = extract = OptionalHandler(key, serial);
                        template = OptionalTemplater(key, serial);
                    }
                }
                else {
                    if (has_next) {
                        go_to = extract = Handler(key, serial, next_goto);
                        template = Templater(key, serial, next_template);
                    }
                    else {
                        go_to = extract = Handler(key, serial);
                        template = Templater(key, serial);
                    }
                }
            }

            return true;
        }

        public static bool Parse(StringIterator it, out ExtractDelegate go_to, out ExtractDelegate extract, out Matcher.TemplateDelegate template) {
            if (it.SelectSection('{', '}')) {
                string key, type;

                if (it.SelectSection(" as ")) {
                    key = it;
                    it.ConsumeSection();
                    type = it;
                }
                else {
                    key = it;
                    type = "";
                }

                it.ConsumeSection();

                var optional = it.Consume('?');

                var has_next = Parser.Parse(
                    it,
                    out ExtractDelegate next_goto,
                    out ExtractDelegate next_extract,
                    out TemplateDelegate next_template);

                return parse_internal(
                    key,
                    type,
                    optional,
                    out go_to,
                    out extract,
                    out template,
                    has_next,
                    next_goto,
                    next_extract,
                    next_template
                );
            }

            go_to = null;
            extract = null;
            template = null;
            return false;
        }

        public static bool Parse(
            StringIterator it,
            out ExtractDelegate go_to,
            out ExtractDelegate extract,
            out TemplateDelegate template,
            ExtractDelegate next_goto,
            ExtractDelegate next_extract,
            TemplateDelegate next_template) {
            if (it.SelectSection('{', '}')) {
                string key, type;

                if (it.SelectSection(" as ")) {
                    key = it;
                    it.ConsumeSection();
                    type = it;
                }
                else {
                    key = it;
                    type = "";
                }

                it.ConsumeSection();

                var optional = it.Consume('?');

                var has_next = Parser.Parse(
                    it,
                    out ExtractDelegate parsed_goto,
                    out ExtractDelegate parsed_extract,
                    out TemplateDelegate parsed_template,
                    next_goto,
                    next_extract,
                    next_template);

                if (has_next) {
                    return parse_internal(
                        key,
                        type,
                        optional,
                        out go_to,
                        out extract,
                        out template,
                        true,
                        parsed_goto,
                        parsed_extract,
                        parsed_template
                    );
                }
                else {
                    return parse_internal(
                        key,
                        type,
                        optional,
                        out go_to,
                        out extract,
                        out template,
                        true,
                        next_goto,
                        next_extract,
                        next_template
                    );
                }
            }

            go_to = null;
            extract = null;
            template = null;
            return false;
        }
    }
}