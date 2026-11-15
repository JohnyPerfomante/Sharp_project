using PLAVS.Analysis;
using PLAVS.Application;
using PLAVS.Domain;
using Xunit;

namespace PLAVS.UnitTests;

public sealed class AnalysisTests
{
    private static readonly SettingsSpec Settings = new(Guid.NewGuid(), 1, .25, .55, .20, 80, 55);
    [Theory]
    [InlineData("  ОБ’ЄКТ\t НЕ  зникає\n", "об'єкт не зникає")]
    [InlineData("памʼять\u200b — дані", "пам'ять - дані")]
    [InlineData("ＦＩＦＯ", "fifo")]
    public void NormalizationPreservesUkrainianAndNegation(string text, string expected)
        => Assert.Equal(expected, TextNormalizer.Normalize(text));
    [Fact] public void TokenizationPreservesMeaningfulTerms()
        => Assert.Equal(["c", "#", "не", "об'єкт"], TextNormalizer.Tokens("C# не об’єкт."));
    [Fact] public void FuzzyComparisonHandlesTypos()
    {
        var comparer = new LexicalComparer();
        Assert.Equal(1, comparer.Compare("Клас описує об’єкт", "клас описує об'єкт"));
        Assert.True(comparer.Compare("процес має власну пам'ять", "процес має власну пам'ят") > .85);
        Assert.Equal(0, comparer.Compare("", "дані"));
        Assert.True(comparer.Compare("стек", "стек зберігає елементи за правилом останнім прийшов першим вийшов") < .6);
    }
    [Fact] public void KeywordsUseWeightsSynonymsAndTokenBoundaries()
    {
        var matcher = new KeywordMatcher();
        KeywordSpec[] concepts = [new("черга", ["fifo"], 2), new("стек", [], 1), new("ip", [], 1)];
        var matches = matcher.Match("FIFO та стек, а також тип", concepts);
        Assert.True(matches[0].Found); Assert.Equal("fifo", matches[0].MatchedText);
        Assert.True(matches[1].Found); Assert.False(matches[2].Found);
        Assert.Equal(.75, KeywordMatcher.Coverage(matches));
    }
    [Fact] public void NegatedKeywordIsNotCountedAsCoverage()
    {
        var matches = new KeywordMatcher().Match("Це не стек", [new("стек", [], 2)]);
        Assert.True(matches[0].Found); Assert.True(matches[0].Negated); Assert.Equal(0, KeywordMatcher.Coverage(matches));
    }
    [Fact] public void WeightedScoreMatchesSpecifiedFormula()
        => Assert.Equal(79.5, AnalysisEngine.Score(.7, .8, .9, Settings), 8);
    [Theory]
    [InlineData(.2,.5,.2)] [InlineData(-.1,.9,.2)] [InlineData(.8,0,.2)] [InlineData(double.NaN,.8,.2)]
    public void InvalidWeightsAreRejected(double a, double b, double c)
        => Assert.Throws<AppException>(() => (Settings with { Alpha = a, Beta = b, Gamma = c }).Validate());
    [Theory]
    [InlineData(80,MatchCategory.High)] [InlineData(79.99,MatchCategory.Partial)] [InlineData(55,MatchCategory.Partial)] [InlineData(54.99,MatchCategory.Low)]
    public void CategoriesRespectBoundaries(double score, MatchCategory expected)
        => Assert.Equal(expected, AnalysisEngine.Categorize(score, Settings));
    [Fact] public void CosineRejectsInvalidVectorsAndClampsNegativeSimilarity()
    {
        Assert.Equal(1, AnalysisEngine.Cosine([1,0],[1,0]));
        Assert.Equal(0, AnalysisEngine.Cosine([1,0],[-1,0]));
        Assert.Throws<InvalidOperationException>(()=>AnalysisEngine.Cosine([0,0],[1,0]));
        Assert.Throws<InvalidOperationException>(()=>AnalysisEngine.Cosine([float.NaN],[1]));
    }
    [Fact] public async Task UnavailableSemanticsDoesNotInventAScore()
    {
        var engine = new AnalysisEngine(new MissingEncoder(), new(), new());
        var result = await engine.AnalyzeAsync("стек", Question([new(Guid.NewGuid(),"стек")]), Settings);
        Assert.Null(result.Semantic); Assert.Null(result.Score); Assert.True(result.NeedsReview);
        Assert.Equal(MatchCategory.Unavailable, result.Category); Assert.NotNull(result.Details.SemanticError);
        Assert.Equal(1, result.Lexical);
    }
    [Fact] public async Task InferenceFailureBecomesReviewRatherThanDataLoss()
    {
        var result = await new AnalysisEngine(new FailingEncoder(), new(), new()).AnalyzeAsync("стек",Question([new(Guid.NewGuid(),"стек")]),Settings);
        Assert.Null(result.Score); Assert.True(result.NeedsReview);
    }
    [Fact] public async Task ChoosesCoherentCandidateRatherThanIndependentMaxima()
    {
        const string text = "стек має вершину";
        var first = new ReferenceSpec(Guid.NewGuid(),"стек має одну вершину");
        var second = new ReferenceSpec(Guid.NewGuid(),"елемент вилучається останнім");
        var encoder = new DictionaryEncoder(new Dictionary<string,float[]> { [text]=[1,0], [first.Text]=[0,1], [second.Text]=[1,0] });
        var result = await new AnalysisEngine(encoder,new(),new()).AnalyzeAsync(text, Question([first,second]), Settings);
        Assert.Equal(second.Id,result.ReferenceId);
        Assert.Equal(new LexicalComparer().Compare(text,second.Text),result.Lexical);
        Assert.True(result.Lexical < new LexicalComparer().Compare(text,first.Text));
        Assert.Equal(AnalysisEngine.Score(result.Lexical,result.Semantic!.Value,result.Keywords,Settings),result.Score!.Value,2);
    }
    [Fact] public async Task TruncationRequiresReviewEvenForPerfectSimilarity()
    {
        var encoder = new DictionaryEncoder(new() { ["стек"]=[1,0] }, true);
        var result = await new AnalysisEngine(encoder,new(),new()).AnalyzeAsync("стек",Question([new(Guid.NewGuid(),"стек")]),Settings);
        Assert.True(result.NeedsReview); Assert.True(result.Details.Truncated); Assert.Equal(100,result.Score);
    }
    [Fact] public void MlNetCreatesActualFeatureVectors()
    {
        var features = new MlTextFeatures().Transform(["стек і черга", "база даних та таблиця"]);
        Assert.Equal(2,features.Length); Assert.NotEmpty(features[0]); Assert.Equal(features[0].Length,features[1].Length);
    }
    [Fact] public void MissingOnnxFilesAreReported()
    {
        using var encoder = new OnnxSentenceEncoder(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()));
        Assert.False(encoder.Status.Available);
    }
    private static QuestionSnapshot Question(ReferenceSpec[] references) => new(Guid.NewGuid(),"Що таке стек?",1,10,references,[new("стек",[],1)]);
    private sealed class MissingEncoder : ISemanticEncoder
    {
        public SemanticStatus Status => new(false,"test-double","Модель недоступна.");
        public Task<Embedding> EncodeAsync(string text,bool cache,CancellationToken ct=default)=>throw new InvalidOperationException();
    }
    private sealed class FailingEncoder : ISemanticEncoder
    {
        public SemanticStatus Status => new(true,"test-double","test");
        public Task<Embedding> EncodeAsync(string text,bool cache,CancellationToken ct=default)=>throw new InvalidOperationException("test inference failure");
    }
    private sealed class DictionaryEncoder(Dictionary<string,float[]> vectors,bool truncated=false) : ISemanticEncoder
    {
        public SemanticStatus Status => new(true,"test-double","test");
        public Task<Embedding> EncodeAsync(string text,bool cache,CancellationToken ct=default)=>Task.FromResult(new Embedding(vectors[text],truncated));
    }
}
