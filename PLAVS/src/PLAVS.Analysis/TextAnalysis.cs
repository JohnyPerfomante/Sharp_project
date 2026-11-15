using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML;
using Microsoft.ML.Data;
using PLAVS.Application;
using Raffinert.FuzzySharp;

namespace PLAVS.Analysis;

public static partial class TextNormalizer
{
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:'[\p{L}]+)*|[+#]+")] private static partial Regex Words();
    public static string Normalize(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            if (c is '\u200b' or '\ufeff' or '\u00ad') continue;
            if (c is '’' or 'ʼ' or '`' or '‘') sb.Append('\'');
            else if (c is '–' or '—' or '−') sb.Append('-');
            else if (!char.IsControl(c) || char.IsWhiteSpace(c)) sb.Append(c);
        }
        return Spaces().Replace(sb.ToString(), " ").Trim();
    }
    public static string[] Tokens(string text) => Words().Matches(Normalize(text)).Select(m => m.Value).ToArray();
}

public sealed class LexicalComparer
{
    public double Compare(string a, string b)
    {
        a = TextNormalizer.Normalize(a); b = TextNormalizer.Normalize(b);
        if (a.Length == 0 || b.Length == 0) return 0;
        // Full-string distance prevents a tiny matching fragment from receiving an unjustified perfect lexical score.
        var full = Fuzz.Ratio(a, b) / 100d;
        var tokens = Fuzz.TokenSortRatio(a, b) / 100d;
        var phrases = Fuzz.TokenSetRatio(a, b) / 100d;
        return Math.Clamp(.5 * full + .3 * tokens + .2 * phrases, 0, 1);
    }
}

public sealed class KeywordMatcher
{
    private static readonly HashSet<string> Negations = ["не", "ні", "ніколи", "без", "немає", "неможливо"];
    public KeywordMatch[] Match(string text, KeywordSpec[] concepts)
    {
        var words = TextNormalizer.Tokens(text);
        return concepts.Select(c =>
        {
            string? match = null; var negated = false;
            foreach (var form in new[] { c.Term }.Concat(c.Synonyms))
            {
                var tokens = TextNormalizer.Tokens(form);
                if (tokens.Length == 0) continue;
                for (var i = 0; i <= words.Length - tokens.Length; i++)
                {
                    var equal = tokens.Select((t, j) => TokenMatches(words[i + j], t)).All(v => v);
                    if (!equal) continue;
                    match = string.Join(' ', words.Skip(i).Take(tokens.Length));
                    negated |= words.Skip(Math.Max(0, i - 3)).Take(Math.Min(3, i)).Any(Negations.Contains);
                }
            }
            return new KeywordMatch(c.Term, c.Weight, match is not null, match, negated);
        }).ToArray();
    }
    private static bool TokenMatches(string actual, string expected)
    {
        if (actual == expected) return true;
        // Long words tolerate small spelling differences. Short terms and abbreviations require an exact synonym.
        return actual.Length >= 5 && expected.Length >= 5 && Math.Abs(actual.Length - expected.Length) <= 2 && Fuzz.Ratio(actual, expected) >= 84;
    }
    public static double Coverage(KeywordMatch[] matches)
    {
        var total = matches.Sum(m => m.Weight);
        return total <= 0 ? 0 : Math.Clamp(matches.Where(m => m.Found && !m.Negated).Sum(m => m.Weight) / total, 0, 1);
    }
    public static bool HasNegation(string text) => TextNormalizer.Tokens(text).Any(Negations.Contains);
}

// ML.NET feature extraction is deliberately separate from the ONNX semantic score. It is an extension point for calibration.
public sealed class MlTextFeatures
{
    public sealed class Input { public string Text { get; set; } = ""; }
    public sealed class Output { [ColumnName("Features")] public float[] Features { get; set; } = []; }
    public float[][] Transform(string[] texts)
    {
        if (texts.Length == 0) return [];
        var ml = new MLContext(seed: 42);
        var data = ml.Data.LoadFromEnumerable(texts.Select(t => new Input { Text = TextNormalizer.Normalize(t) }));
        var model = ml.Transforms.Text.FeaturizeText("Features", nameof(Input.Text)).Fit(data);
        return ml.Data.CreateEnumerable<Output>(model.Transform(data), reuseRowObject: false).Select(o => o.Features).ToArray();
    }
}
