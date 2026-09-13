namespace PZLauncher.Services
{
    internal sealed class LaunchPlan
    {
        public string JavaExecutable { get; init; } = string.Empty;
        public string WorkingDirectory { get; init; } = string.Empty;
        public string CacheDirectory { get; init; } = string.Empty;
        public string ConsoleLogPath { get; init; } = string.Empty;
        public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

        public string CommandLinePreview
        {
            get
            {
                var redacted = Arguments.Select((argument, index) => index > 0 && Arguments[index - 1] is "+password" or "-adminpassword" ? "••••••" : argument);
                return $"{Quote(JavaExecutable)} {string.Join(" ", redacted.Select(Quote))}";
            }
        }

        private static string Quote(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Any(char.IsWhiteSpace)
                ? $"\"{value.Replace("\"", "\\\"")}\""
                : value;
        }
    }
}
