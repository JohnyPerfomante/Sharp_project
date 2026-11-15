using PLAVS.Application;
using PLAVS.Domain;

namespace PLAVS.Analysis;

public sealed class AnalysisEngine(ISemanticEncoder encoder, LexicalComparer lexical, KeywordMatcher keywords) : IAnalysisEngine
{
    public const string Version = "plavs-1.0.0";
    public async Task<AnalysisOutcome> AnalyzeAsync(string text, QuestionSnapshot question, SettingsSpec settings, CancellationToken ct = default)
    {
        settings.Validate();
        var normalized = TextNormalizer.Normalize(text);
        var concepts = keywords.Match(normalized, question.Keywords);
        var coverage = KeywordMatcher.Coverage(concepts);
        var warnings = new List<string>();
        var reasons = new List<string>();
        var missing = concepts.Where(k => !k.Found || k.Negated).Select(k => k.Term).ToArray();
        if (missing.Length > 0) reasons.Add("Не підтверджено ключові поняття: " + string.Join(", ", missing) + ".");
        if (concepts.Any(k => k.Negated)) warnings.Add("Поряд із ключовим поняттям виявлено заперечення. Це евристична ознака можливої суперечності, яку має перевірити викладач.");
        if (KeywordMatcher.HasNegation(normalized) && question.References.Any(r => !KeywordMatcher.HasNegation(r.Text)))
            warnings.Add("Заперечення відрізняється від принаймні одного еталона; зміст потребує ручної перевірки.");
        double bestL = 0; double? bestM = null; double? bestScore = null; ReferenceSpec? best = null;
        string? error = null; var truncated = false;
        try
        {
            if (normalized.Length == 0) throw new InvalidOperationException("Відповідь порожня; автоматичне оцінювання недоступне.");
            if (!encoder.Status.Available) throw new InvalidOperationException(encoder.Status.Message);
            var answer = await encoder.EncodeAsync(normalized, false, ct);
            truncated |= answer.Truncated;
            foreach (var reference in question.References)
            {
                ct.ThrowIfCancellationRequested();
                var l = lexical.Compare(normalized, reference.Text);
                var referenceEmbedding = await encoder.EncodeAsync(TextNormalizer.Normalize(reference.Text), true, ct);
                truncated |= referenceEmbedding.Truncated;
                var m = Cosine(answer.Vector, referenceEmbedding.Vector);
                var score = Score(l, m, coverage, settings);
                // Choose one complete candidate tuple, never independent maxima from different references.
                if (!bestScore.HasValue || score > bestScore)
                { bestL = l; bestM = m; bestScore = score; best = reference; }
            }
            if (best is null) throw new InvalidOperationException("Немає активних еталонів для порівняння.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or ArgumentException)
        {
            // No lexical surrogate, random score or weight redistribution is used when semantics fail.
            error = ex is Microsoft.ML.OnnxRuntime.OnnxRuntimeException ? "Помилка локального ONNX-інференсу." : ex.Message;
            bestM = null; bestScore = null;
            best = question.References.OrderByDescending(r => lexical.Compare(normalized, r.Text)).FirstOrDefault();
            bestL = best is null ? 0 : lexical.Compare(normalized, best.Text);
            reasons.Add("Семантичний компонент недоступний. Підсумковий бал не обчислено; потрібна ручна перевірка.");
        }
        if (truncated) warnings.Add("Текст перевищив 128 токенів моделі. Усічений фрагмент не є достатнім для автоматичного рішення.");
        if (bestM is >= .8) reasons.Add("Модель визначила високу семантичну близькість до вибраного еталона.");
        if (bestL < .4) reasons.Add("Низька лексична схожість із вибраним еталоном.");
        var category = bestScore.HasValue ? Categorize(bestScore.Value, settings) : MatchCategory.Unavailable;
        var needsReview = category != MatchCategory.High || warnings.Count > 0 || coverage < .6 || question.Keywords.Length == 0;
        if (!needsReview) reasons.Add("Висока попередня відповідність. Остаточне академічне рішення залишається за викладачем.");
        return new(bestL, bestM, coverage, bestScore.HasValue ? Math.Round(bestScore.Value, 2) : null, category, needsReview,
            best?.Id, encoder.Status.ModelVersion, Version,
            new(concepts, reasons.ToArray(), warnings.ToArray(), error, best?.Text, settings, question.Revision, truncated));
    }
    public static double Score(double l, double m, double k, SettingsSpec s)
    {
        s.Validate();
        if (new[] { l, m, k }.Any(v => !double.IsFinite(v) || v is < 0 or > 1)) throw new ArgumentOutOfRangeException(nameof(l));
        return 100 * (s.Alpha * l + s.Beta * m + s.Gamma * k);
    }
    public static MatchCategory Categorize(double score, SettingsSpec s) => score >= s.HighThreshold ? MatchCategory.High : score >= s.PartialThreshold ? MatchCategory.Partial : MatchCategory.Low;
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length || a.Any(v => !float.IsFinite(v)) || b.Any(v => !float.IsFinite(v))) throw new InvalidOperationException("Некоректний embedding-вектор.");
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; }
        if (aa < 1e-12 || bb < 1e-12) throw new InvalidOperationException("Порожній embedding-вектор.");
        return Math.Clamp(dot / Math.Sqrt(aa * bb), 0, 1);
    }
}
