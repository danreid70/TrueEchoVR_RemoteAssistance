using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Unity.AI.Assistant.Tools.Editor
{
    /// <summary>
    /// Pure utility methods for building ripgrep arguments and
    /// post-processing ripgrep output. Extracted from Grep so the logic
    /// can be tested without spawning a process or an agent tool context.
    /// </summary>
    static class GrepUtility
    {
        internal const int DefaultMaxOutputChars = 8192;
        internal const int DefaultMaxOutputLines = 80;

        static readonly string[] k_DefaultExcludeGlobs =
        {
            "*.fbx",
            "*.obj",
        };

        /// <summary>
        /// Builds the complete ripgrep argument string from user-provided
        /// rg arguments and one or more project search paths.
        /// Enforces project-scoping and output safety while letting the
        /// caller pass any standard ripgrep flags.
        /// </summary>
        internal static string BuildArguments(
            string userArgs,
            params string[] searchPaths)
        {
            var quotedPaths = FormatSearchPaths(searchPaths);
            bool isFileListing = IsFileListingMode(userArgs);

            var args = new StringBuilder();

            // Enforced flags (not overridable)
            args.Append("--color never ");
            args.Append("--iglob \"!*.meta\" ");

            // Sensible defaults for content search (placed before user args so they can be overridden)
            if (!isFileListing)
            {
                args.Append("-n --heading ");

                if (!HasFileFilter(userArgs))
                {
                    // Default to C# files only — prevents noisy matches in
                    // .shadergraph, .prefab, .unity, .asset files.
                    // Override with explicit --glob, --iglob, or --type.
                    args.Append("--type cs ");

                    foreach (var glob in k_DefaultExcludeGlobs)
                    {
                        args.Append($"--glob \"!{glob}\" ");
                    }
                }
            }

            // User-provided ripgrep arguments (pattern included)
            var sanitized = SanitizeUserArgs(userArgs);
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                args.Append(sanitized);
                args.Append(' ');
            }

            // Restrict search to project directories
            args.Append("-- ");
            args.Append(quotedPaths);

            return args.ToString().TrimEnd();
        }

        /// <summary>
        /// Removes flags that conflict with our enforced settings and strips
        /// bare <c>--</c> separators (we add our own before search paths).
        /// </summary>
        internal static string SanitizeUserArgs(string userArgs)
        {
            if (string.IsNullOrWhiteSpace(userArgs))
                return "";

            var sanitized = Regex.Replace(userArgs, @"--color(?:\s+|=)\w+", "");
            sanitized = Regex.Replace(sanitized, @"(?<=\s|^)--(?=\s|$)", "");
            sanitized = Regex.Replace(sanitized, @"\s{2,}", " ");

            return sanitized.Trim();
        }

        /// <summary>
        /// Returns true when the user args contain <c>--files</c>, indicating
        /// a file-listing operation rather than a content search.
        /// </summary>
        internal static bool IsFileListingMode(string userArgs)
        {
            if (string.IsNullOrWhiteSpace(userArgs))
                return false;

            return Regex.IsMatch(userArgs, @"(^|\s)--files(\s|$)");
        }

        /// <summary>
        /// Returns true when the user args contain file-filtering flags (--glob, --iglob,
        /// --type, or their short forms). Used to decide whether to add default exclude globs.
        /// </summary>
        internal static bool HasFileFilter(string userArgs)
        {
            if (string.IsNullOrWhiteSpace(userArgs))
                return false;

            return userArgs.Contains("--glob") || userArgs.Contains("--iglob") ||
                   userArgs.Contains("--type") ||
                   Regex.IsMatch(userArgs, @"(^|\s)-[a-zA-Z]*[tg]\s");
        }

        static string FormatSearchPaths(string[] searchPaths)
        {
            return string.Join(" ", searchPaths.Select(p => $"\"{EscapeQuotes(p)}\""));
        }

        static string EscapeQuotes(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var escaped = value.Replace("\"", "\\\"");

            // Windows MSVC CRT argument parsing treats backslashes before a
            // closing double-quote as escape characters.  Double any trailing
            // backslashes so they are interpreted as literal characters and
            // don't swallow the closing quote (e.g. -e "pattern\\").
            int trailing = 0;
            for (var i = escaped.Length - 1; i >= 0 && escaped[i] == '\\'; i--)
                trailing++;

            if (trailing > 0)
                escaped += new string('\\', trailing);

            return escaped;
        }

        /// <summary>
        /// Strips the project root prefix from all absolute paths in ripgrep output
        /// so that paths appear relative.
        /// </summary>
        internal static string StripProjectRoot(string stdout, string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRoot) || string.IsNullOrEmpty(stdout))
                return stdout;

            return stdout.Replace(projectRoot + "/", "")
                         .Replace(projectRoot + "\\", "");
        }

        /// <summary>
        /// Truncates content-mode output to <paramref name="maxChars"/>,
        /// cutting at the last newline boundary so partial lines are not emitted.
        /// Also enforces a maximum line count via <see cref="DefaultMaxOutputLines"/>.
        /// </summary>
        internal static string TruncateContentOutput(string stdout, int maxChars)
        {
            if (string.IsNullOrEmpty(stdout))
                return stdout;

            var totalLines = CountLines(stdout);
            var totalChars = stdout.Length;

            // Apply line limit first, then char limit
            string result = stdout;
            bool truncated = false;

            if (totalLines > DefaultMaxOutputLines)
            {
                result = TruncateToMaxLines(result, DefaultMaxOutputLines, "");
                truncated = true;
            }

            if (result.Length > maxChars)
            {
                var truncateAt = result.LastIndexOf('\n', maxChars);
                result = truncateAt > 0
                    ? result.Substring(0, truncateAt)
                    : result.Substring(0, maxChars);
                truncated = true;
            }

            if (!truncated)
                return stdout;

            var shownLines = CountLines(result);
            return result +
                $"\n\n[Results truncated: showing {shownLines} of {totalLines} lines. " +
                "Your search is too broad. To reduce results: " +
                "use -l to list only file paths, " +
                "use a more specific pattern, " +
                "use --glob \"*.cs\" to filter by file type, " +
                "or use the path parameter to restrict to a directory.]";
        }

        static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            int count = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Keeps only the first <paramref name="maxLines"/> lines from
        /// <paramref name="text"/>. If lines were dropped, appends
        /// <paramref name="truncationMessage"/>.
        /// </summary>
        internal static string TruncateToMaxLines(string text, int maxLines, string truncationMessage)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            int count = 0;
            int pos = 0;
            while (pos < text.Length && count < maxLines)
            {
                int nextNewline = text.IndexOf('\n', pos);
                if (nextNewline < 0)
                {
                    count++;
                    pos = text.Length;
                    break;
                }

                count++;
                pos = nextNewline + 1;
            }

            if (pos >= text.Length)
                return text.TrimEnd('\n', '\r');

            var truncatedText = text.Substring(0, pos).TrimEnd('\n', '\r');
            return truncatedText + "\n\n" + truncationMessage;
        }
    }
}
