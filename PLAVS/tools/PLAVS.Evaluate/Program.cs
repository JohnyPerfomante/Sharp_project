using System.Diagnostics;
using System.Runtime.InteropServices;
using PLAVS.Analysis;
using PLAVS.Application;
using PLAVS.Domain;

string Argument(string name,string fallback) { var i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback; }
var root=Directory.GetCurrentDirectory();
using var encoder=new OnnxSentenceEncoder(Argument("--model-dir",OnnxSentenceEncoder.DefaultDirectory));
if(!encoder.Status.Available){Console.Error.WriteLine(encoder.Status.Message);return 2;}
if(args.Contains("--tokens")){Console.WriteLine(JsonDefaults.Write(encoder.TokenIds(Argument("--tokens",""))));return 0;}
var golden=JsonDefaults.Read<TokenCase[]>(await File.ReadAllTextAsync(Path.Combine(root,"database/seed/tokenizer-golden.json")));
foreach(var sample in golden)
    if(!encoder.TokenIds(sample.Text).SequenceEqual(sample.Ids)) throw new InvalidOperationException("Токенізація не відповідає офіційному контрольному рядку.");
var seeds=JsonDefaults.Read<Seed[]>(await File.ReadAllTextAsync(Argument("--questions",Path.Combine(root,"database/seed/questions.json"))));
var cases=JsonDefaults.Read<Case[]>(await File.ReadAllTextAsync(Argument("--dataset",Path.Combine(root,"database/seed/evaluation.json"))));
var engine=new AnalysisEngine(encoder,new(),new());
var settings=new SettingsSpec(Guid.Empty,1,.25,.55,.20,80,55);
// Warm up the native inference session before latency measurements.
await encoder.EncodeAsync("Перевірка локальної моделі українською мовою.",false);
var results=new List<CaseResult>();
foreach(var item in cases)
{
    var seed=seeds.Single(q=>q.Code==item.QuestionCode);
    // Pending seed variants are intentionally not supplied as accepted references.
    var question=new QuestionSnapshot(Guid.Empty,seed.Text,1,10,[new(Guid.Empty,seed.Reference)],seed.Keywords);
    var watch=Stopwatch.StartNew();var result=await engine.AnalyzeAsync(item.Text,question,settings);watch.Stop();
    results.Add(new(item.Id,item.Kind,item.ExpectedCategory,result.Category,item.ProposedScore,item.ExpertScore,result.Score,result.NeedsReview,watch.Elapsed.TotalMilliseconds));
}
MatchCategory[] labels=[MatchCategory.High,MatchCategory.Partial,MatchCategory.Low];
var matrix=labels.Select(expected=>labels.Select(predicted=>results.Count(r=>r.Expected==expected&&r.Predicted==predicted)).ToArray()).ToArray();
var perClass=labels.Select(label=>
{
    var tp=results.Count(r=>r.Expected==label&&r.Predicted==label);
    var fp=results.Count(r=>r.Expected!=label&&r.Predicted==label);
    var fn=results.Count(r=>r.Expected==label&&r.Predicted!=label);
    var precision=tp+fp==0?0:(double)tp/(tp+fp);var recall=tp+fn==0?0:(double)tp/(tp+fn);
    return new {Label=label,Precision=precision,Recall=recall,F1=precision+recall==0?0:2*precision*recall/(precision+recall)};
}).ToArray();
var latencies=results.Select(r=>r.Milliseconds).Order().ToArray();
var ua=await encoder.EncodeAsync("Стек повертає останній доданий елемент першим.",false);
var para=await encoder.EncodeAsync("Останній елемент надходження вилучається зі стека раніше за інші.",false);
var unrelated=await encoder.EncodeAsync("Сьогодні на вулиці дощова погода, а завтра буде сонячно.",false);
var benchmarkSeed=seeds.First();
var benchmarkQuestion=new QuestionSnapshot(Guid.Empty,benchmarkSeed.Text,1,10,[new(Guid.Empty,benchmarkSeed.Reference)],benchmarkSeed.Keywords);
var benchmarkTimes=new List<double>();
var benchmarkText=string.Join(" ",Enumerable.Repeat("Алгоритм — скінченна послідовність однозначних кроків із визначеним результатом.",20))[..1000];
for(var i=0;i<100;i++)
{
    var watch=Stopwatch.StartNew();await engine.AnalyzeAsync(benchmarkText,benchmarkQuestion,settings);watch.Stop();benchmarkTimes.Add(watch.Elapsed.TotalMilliseconds);
}
benchmarkTimes.Sort();
var report=new {GeneratedAt=DateTimeOffset.UtcNow,Model=encoder.Status.ModelVersion,Algorithm=AnalysisEngine.Version,Settings=settings,
    Hardware=new {OS=RuntimeInformation.OSDescription,Architecture=RuntimeInformation.ProcessArchitecture.ToString(),LogicalProcessors=Environment.ProcessorCount},
    LabelStatus=cases.All(c=>c.ExpertScore.HasValue)?"ExpertScoresProvided":"DraftForTeacher: authored expectations, not independent expert validation",
    Total=results.Count,Accuracy=(double)results.Count(r=>r.Expected==r.Predicted)/results.Count,Classes=perClass,
    MatrixLabels=labels,ConfusionMatrix=matrix,FalseAcceptance=results.Count(r=>r.Expected==MatchCategory.Low&&r.Predicted==MatchCategory.High),
    WrongAutomaticallyHighWithoutReview=results.Count(r=>r.Expected==MatchCategory.Low&&r.Predicted==MatchCategory.High&&!r.NeedsReview),
    FalseRejectionOfParaphrases=results.Count(r=>r.Kind=="Paraphrase"&&r.Predicted==MatchCategory.Low),
    ReviewRate=(double)results.Count(r=>r.NeedsReview)/results.Count,
    Latency=new {P95Milliseconds=latencies[(int)Math.Ceiling(latencies.Length*.95)-1],MaxMilliseconds=latencies[^1],IncludesFirstReferenceEmbedding=true},
    Benchmark1000Characters=new {Runs=100,P95Milliseconds=benchmarkTimes[94],MaxMilliseconds=benchmarkTimes[^1],References=1,Note="Warm model, sequential single-client workload; long inputs are truncated at 128 tokens and require review."},
    TokenizerGoldenCasesPassed=golden.Length,
    UkrainianSmoke=new {Dimensions=ua.Vector.Length,ParaphraseSimilarity=AnalysisEngine.Cosine(ua.Vector,para.Vector),UnrelatedSimilarity=AnalysisEngine.Cosine(ua.Vector,unrelated.Vector)},Results=results};
var output=Argument("--output",Path.Combine(root,"artifacts/evaluation.json"));Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
await File.WriteAllTextAsync(output,System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions(JsonDefaults.Options){WriteIndented=true}));
Console.WriteLine($"Cases: {report.Total}; accuracy vs draft labels: {report.Accuracy:P1}; p95 {report.Latency.P95Milliseconds:F1} ms; output: {output}");
Console.WriteLine($"Ukrainian smoke: paraphrase {report.UkrainianSmoke.ParaphraseSimilarity:F3}, unrelated {report.UkrainianSmoke.UnrelatedSimilarity:F3}");
return 0;
sealed record Seed(string Code,string Text,string Reference,KeywordSpec[] Keywords);
sealed record Case(string Id,string QuestionCode,string Kind,string Text,MatchCategory ExpectedCategory,double ProposedScore,double? ExpertScore);
sealed record CaseResult(string Id,string Kind,MatchCategory Expected,MatchCategory Predicted,double ProposedScore,double? ExpertScore,double? ActualScore,bool NeedsReview,double Milliseconds);
sealed record TokenCase(string Text,int[] Ids);
