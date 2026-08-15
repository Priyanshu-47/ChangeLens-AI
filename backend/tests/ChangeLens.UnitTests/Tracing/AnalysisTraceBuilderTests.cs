using System.Text.Json;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Tracing;
using ChangeLens.Domain.Analysis;

namespace ChangeLens.UnitTests.Tracing;

public sealed class AnalysisTraceBuilderTests
{
    [Fact]
    public void Stages_RecordRealDurations()
    {
        var builder = new AnalysisTraceBuilder();

        using (builder.Stage("Context"))
        {
            Thread.Sleep(5);
        }

        var stage = Assert.Single(builder.Stages);
        Assert.Equal("Context", stage.Name);
        Assert.Equal("Completed", stage.Status);
        Assert.NotNull(stage.StartedAtUtc);
        Assert.NotNull(stage.CompletedAtUtc);
        Assert.True(stage.DurationMs >= 5, $"duration should be >= 5ms, was {stage.DurationMs}");
    }

    [Fact]
    public void Fail_RecordsNormalizedCategory_AndMarksLastStageFailed()
    {
        var builder = new AnalysisTraceBuilder();
        using (builder.Stage("AI Analysis"))
        {
        }

        builder.Fail(AnalysisFailureCode.LlmRateLimited, "rate limited");

        Assert.Equal(AnalysisFailureCategory.RateLimit, builder.FailureCategory);
        Assert.Equal(AnalysisFailureCode.LlmRateLimited, builder.FailureCode);
        var stage = Assert.Single(builder.Stages);
        Assert.Equal("Failed", stage.Status);
        Assert.Equal(AnalysisFailureCode.LlmRateLimited, stage.Metadata!["failureCode"]);
        Assert.Equal(AnalysisFailureCategory.RateLimit, stage.Metadata["failureCategory"]);
    }

    [Theory]
    [InlineData(AnalysisFailureCode.AiValidationFailed, AnalysisFailureCategory.Validation)]
    [InlineData(AnalysisFailureCode.LlmRateLimited, AnalysisFailureCategory.RateLimit)]
    [InlineData(AnalysisFailureCode.AiTimeout, AnalysisFailureCategory.Timeout)]
    [InlineData(AnalysisFailureCode.JobTimeout, AnalysisFailureCategory.Timeout)]
    [InlineData(AnalysisFailureCode.AiUnavailable, AnalysisFailureCategory.AiProvider)]
    [InlineData(AnalysisFailureCode.Internal, AnalysisFailureCategory.Internal)]
    public void FailureCategory_MapsEachCode(string code, string expected)
    {
        Assert.Equal(expected, AnalysisFailureCategory.For(code));
    }

    [Fact]
    public void SetRetrieval_AttachesRetrievalTrace()
    {
        var builder = new AnalysisTraceBuilder();
        builder.SetRetrieval(new RetrievalTraceDto
        {
            Queries = ["JWT rotation"],
            CandidateCount = 4,
            SelectedCount = 2,
            MaxChunks = 20,
            MaxCharsPerChunk = 12000,
            Items =
            [
                new RetrievalTraceItemDto
                {
                    Id = "chunk:abc",
                    DocumentType = "Runbook",
                    Path = "auth-001-jwt-key-rotation.md",
                    KeywordRank = 1,
                    VectorScore = 0.9
                }
            ]
        });

        Assert.NotNull(builder.Retrieval);
        Assert.Equal(2, builder.Retrieval!.SelectedCount);
        Assert.Equal("chunk:abc", builder.Retrieval.Items[0].Id);
    }

    [Fact]
    public void Serialize_UsesCamelCase_AndIncludesSchemaVersion()
    {
        var builder = new AnalysisTraceBuilder();
        using (builder.Stage("Context"))
        {
        }

        var json = builder.Serialize();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("trace-v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("trace-v1", builder.Schema);
        // camelCase keys (Web defaults): every property name starts lowercase.
        Assert.All(root.EnumerateObject(), p => Assert.True(char.IsLower(p.Name[0]), p.Name));
        Assert.Equal("Context", root.GetProperty("stages")[0].GetProperty("name").GetString());
        Assert.True(root.TryGetProperty("totalDurationMs", out _));
    }
}
