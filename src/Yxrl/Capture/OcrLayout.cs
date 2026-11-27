using System.Text;

namespace Yxrl.Capture;

internal enum OcrParagraphMode
{
    Smart,
    Merge,
    Keep
}

internal enum OcrPunctuationMode
{
    Keep,
    Cjk,
    English
}

internal readonly record struct OcrLayoutOptions(
    OcrParagraphMode Paragraphs = OcrParagraphMode.Smart,
    bool RemoveSpaces = false,
    OcrPunctuationMode Punctuation = OcrPunctuationMode.Keep);

internal readonly record struct OcrSpan(int LineIndex, int Start, int Length);
internal sealed record OcrFormattedResult(string Text, IReadOnlyList<OcrSpan> Spans);

internal static class OcrLayout
{
    public static OcrFormattedResult Format(OcrDocument document, OcrLayoutOptions options)
    {
        if (document.Lines.Count == 0)
            return new("", []);

        var sb = new StringBuilder();
        var spans = new List<OcrSpan>(document.Lines.Count);
        for (int i = 0; i < document.Lines.Count; i++)
        {
            string segment = TransformSegment(document.Lines[i].Text, options);
            if (i > 0)
            {
                string sep = Separator(document.Lines[i - 1], document.Lines[i], options);
                sb.Append(sep);
            }

            int start = sb.Length;
            sb.Append(segment);
            spans.Add(new(i, start, segment.Length));
        }

        return new(sb.ToString(), spans);
    }

    private static string Separator(OcrLine prev, OcrLine next, OcrLayoutOptions options)
    {
        if (options.Paragraphs == OcrParagraphMode.Keep)
            return Environment.NewLine;

        bool join = options.Paragraphs == OcrParagraphMode.Merge || ShouldSmartMerge(prev, next);
        if (!join)
            return Environment.NewLine;

        if (options.RemoveSpaces)
            return "";

        char prevEnd = prev.Text.Length > 0 ? prev.Text[^1] : '\0';
        char nextStart = next.Text.Length > 0 ? next.Text[0] : '\0';
        return IsCjk(prevEnd) || IsCjk(nextStart) ? "" : " ";
    }

    private static bool ShouldSmartMerge(OcrLine prev, OcrLine next)
    {
        if (prev.Bounds.IsEmpty || next.Bounds.IsEmpty)
            return false;
        char last = prev.Text.TrimEnd() is { Length: > 0 } t ? t[^1] : '\0';
        if (last is '。' or '！' or '？' or '.' or '!' or '?' or '：' or ':' or '；' or ';' or '…')
            return false;

        double lineHeight = Math.Max(prev.Bounds.Height, next.Bounds.Height);
        double gap = next.Bounds.Top - prev.Bounds.Bottom;
        if (gap > 0.65 * lineHeight || gap < -lineHeight * 0.9)
            return false;

        double leftDiff = Math.Abs(next.Bounds.Left - prev.Bounds.Left);
        if (gap > -0.25 * lineHeight && leftDiff > Math.Max(48, lineHeight * 2.5))
            return false;

        return true;
    }

    private static string TransformSegment(string input, OcrLayoutOptions options)
    {
        string value = options.Punctuation switch
        {
            OcrPunctuationMode.Cjk => ToCjkPunctuation(input),
            OcrPunctuationMode.English => ToEnglishPunctuation(input),
            _ => input
        };
        if (!options.RemoveSpaces)
            return value;

        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
            if (c is not (' ' or '\t' or '\u3000'))
                sb.Append(c);
        return sb.ToString();
    }

    private static string ToCjkPunctuation(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            char prev = i > 0 ? s[i - 1] : '\0';
            char next = i + 1 < s.Length ? s[i + 1] : '\0';
            bool betweenDigits = char.IsDigit(prev) && char.IsDigit(next);
            bool betweenAscii = IsAsciiWord(prev) && IsAsciiWord(next);
            sb.Append(c switch
            {
                ',' when !betweenDigits => '，',
                '.' when !betweenDigits && !betweenAscii => '。',
                ':' when !betweenDigits && !betweenAscii => '：',
                ';' => '；',
                '?' => '？',
                '!' => '！',
                '(' => '（',
                ')' => '）',
                _ => c
            });
        }
        return sb.ToString();
    }

    private static string ToEnglishPunctuation(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                '，' => ",",
                '。' => ".",
                '：' => ":",
                '；' => ";",
                '？' => "?",
                '！' => "!",
                '（' => "(",
                '）' => ")",
                '、' => ",",
                '“' or '”' => "\"",
                '‘' or '’' => "'",
                _ => c.ToString()
            });
        }
        return sb.ToString();
    }

    private static bool IsAsciiWord(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
    private static bool IsCjk(char c) => c is (>= '\u4e00' and <= '\u9fff') or (>= '\u3400' and <= '\u4dbf') or (>= '\u3000' and <= '\u303f') or (>= '\uff00' and <= '\uffef');
}
