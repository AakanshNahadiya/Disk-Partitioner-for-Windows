using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DiskPartitioner.Helpers
{
    /// <summary>
    /// Provides utility methods to identify and remove ESC (U+001B), ANSI escape sequences,
    /// and non-printable control characters across process outputs, UI elements, and screen reader speech.
    /// </summary>
    public static class TextSanitizer
    {
        // Matches ANSI escape sequences (CSI sequences, OSC sequences, and standalone ESC)
        private static readonly Regex AnsiAndEscRegex = new(
            @"\x1B(?:\[[0-?]*[ -/]*[@-~]|\].*?(?:\x07|\x1B\\)|[()#%*+-.]\S|[=><~])|\x1B",
            RegexOptions.Compiled);

        // Matches multiple consecutive whitespace characters (excluding single spaces)
        private static readonly Regex MultipleSpacesRegex = new(
            @"[ \t]{2,}",
            RegexOptions.Compiled);

        /// <summary>
        /// Checks if a string contains the ESC (U+001B) character or any ANSI escape sequences.
        /// </summary>
        public static bool ContainsEscape(string? input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            return input.Contains('\u001b') || AnsiAndEscRegex.IsMatch(input);
        }

        /// <summary>
        /// Strips ANSI escape codes, ESC (U+001B), and non-printable control characters from text.
        /// </summary>
        /// <param name="input">The raw text to sanitize.</param>
        /// <param name="preserveLineBreaks">If true, keeps standard \r, \n, and \t.</param>
        /// <returns>A cleaned string free of escape codes and invisible control characters.</returns>
        public static string StripAnsiAndControl(string? input, bool preserveLineBreaks = true)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // 1. Remove ANSI escape codes and standalone ESC (U+001B)
            string stripped = AnsiAndEscRegex.Replace(input, string.Empty);

            // 2. Filter out non-printable control characters and zero-width/invisible characters
            var sb = new StringBuilder(stripped.Length);
            foreach (char c in stripped)
            {
                if (c == '\u001b' || c == '\0' || c == '\u200B' || c == '\uFEFF' || c == '\u007F')
                {
                    continue; // Skip invisible / control chars
                }

                if (char.IsControl(c))
                {
                    if (preserveLineBreaks && (c == '\r' || c == '\n' || c == '\t'))
                    {
                        sb.Append(c);
                    }
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Sanitizes text specifically for screen readers (NVDA, Narrator, JAWS) and status announcements.
        /// Removes ESC characters, replaces jarring symbols (like pipe '|'), and normalizes spaces.
        /// </summary>
        public static string SanitizeSpeech(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Strip ANSI codes and control characters (no line breaks in spoken announcements)
            string cleaned = StripAnsiAndControl(input, preserveLineBreaks: false);

            // Replace characters that screen readers vocalize awkwardly
            cleaned = cleaned.Replace('|', ',')
                             .Replace('"', ' ')
                             .Replace('•', ' ')
                             .Replace('—', ' ')
                             .Replace('–', ' ');

            // Normalize spaces
            cleaned = MultipleSpacesRegex.Replace(cleaned, " ").Trim();

            return cleaned;
        }

        /// <summary>
        /// Cleans user/hardware input labels (volume labels, disk models) ensuring no invisible control characters exist.
        /// </summary>
        public static string SanitizeLabel(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return StripAnsiAndControl(input, preserveLineBreaks: false).Trim();
        }
    }
}
