namespace Poly.Syntax.Analysis;

public static class AnalysisSettingsExtensions {
    extension(AnalysisContext context) {
        public TSetting? GetSetting<TSetting>() where TSetting : class =>
            context.Settings.Get<TSetting>();
    }

    extension(AnalysisResult result) {
        public TSetting? GetSetting<TSetting>() where TSetting : class =>
            result.SettingsUsed.Get<TSetting>();
    }

    extension(INodeMetadataProvider provider) {
        public TSetting? GetSetting<TSetting>() where TSetting : class {
            return provider switch {
                AnalysisContext context => context.GetSetting<TSetting>(),
                AnalysisResult result => result.GetSetting<TSetting>(),
                _ => null
            };
        }
    }
}