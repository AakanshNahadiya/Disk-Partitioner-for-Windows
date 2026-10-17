using System;
using DiskPartitioner.Helpers;
using Xunit;

namespace DiskPartitioner.Tests
{
    public class TextSanitizerTests
    {
        [Fact]
        public void ContainsEscape_DetectsAscii27AndAnsiSequences()
        {
            string clean = "Normal string";
            string withEsc = "Hello\u001bWorld";
            string withAnsi = "\x1B[32mDiskPart succeeded\x1B[0m";

            Assert.False(TextSanitizer.ContainsEscape(clean));
            Assert.True(TextSanitizer.ContainsEscape(withEsc));
            Assert.True(TextSanitizer.ContainsEscape(withAnsi));
            Assert.False(TextSanitizer.ContainsEscape(null));
            Assert.False(TextSanitizer.ContainsEscape(string.Empty));
        }

        [Fact]
        public void StripAnsiAndControl_RemovesEscAndAnsiCodes()
        {
            string raw = "\x1B[1;31mDiskPart has encountered an error: Access is denied.\x1B[0m\u001b";
            string cleaned = TextSanitizer.StripAnsiAndControl(raw);

            Assert.Equal("DiskPart has encountered an error: Access is denied.", cleaned);
            Assert.False(cleaned.Contains('\u001b'), $"Cleaned string contains ESC. Length: {cleaned.Length}");
        }

        [Fact]
        public void StripAnsiAndControl_PreservesLineBreaksWhenRequested()
        {
            string raw = "Line 1\r\nLine 2\tTabbed\u001b\0";
            string cleaned = TextSanitizer.StripAnsiAndControl(raw, preserveLineBreaks: true);

            Assert.Equal("Line 1\r\nLine 2\tTabbed", cleaned);
            Assert.False(cleaned.Contains('\u001b'));
            Assert.False(cleaned.Contains('\0'));
        }

        [Fact]
        public void StripAnsiAndControl_RemovesLineBreaksWhenPreserveIsFalse()
        {
            string raw = "Line 1\r\nLine 2\tTabbed\u001b";
            string cleaned = TextSanitizer.StripAnsiAndControl(raw, preserveLineBreaks: false);

            Assert.Equal("Line 1Line 2Tabbed", cleaned);
        }

        [Fact]
        public void SanitizeSpeech_RemovesEscReplacesPipeAndNormalizesSpaces()
        {
            string raw = "Partition 1 | Label \"Windows\" \u001b  -- 200 GB";
            string spoken = TextSanitizer.SanitizeSpeech(raw);

            Assert.False(spoken.Contains('\u001b'));
            Assert.False(spoken.Contains('|'));
            Assert.False(spoken.Contains('"'));
            Assert.Contains("Partition 1 , Label Windows -- 200 GB", spoken);
        }

        [Fact]
        public void SanitizeLabel_RemovesNullsAndEscapeChars()
        {
            string raw = "\u001bSamsung SSD 980\0\r\n";
            string cleaned = TextSanitizer.SanitizeLabel(raw);

            Assert.Equal("Samsung SSD 980", cleaned);
            Assert.False(cleaned.Contains('\u001b'));
            Assert.False(cleaned.Contains('\0'));
        }
    }
}
