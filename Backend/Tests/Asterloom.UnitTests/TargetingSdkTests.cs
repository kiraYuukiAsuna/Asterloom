using System.Text.Json;
using Asterloom.Sdk.Targeting;
using Asterloom.Targeting;

namespace Asterloom.UnitTests;

public sealed class TargetingSdkTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void SdkFacadeMatchesEveryServerGoldenVector()
    {
        var document = JsonSerializer.Deserialize<GoldenVectorDocument>(
            File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "Docs",
                "Protocol",
                "targeting-bucketing-v1-vectors.json")),
            JsonOptions)!;

        foreach (var vector in document.Vectors)
        {
            Assert.Equal(
                vector.Bucket,
                AsterloomTargetingEvaluator.ComputeBucket(
                    vector.Namespace,
                    vector.Salt,
                    vector.TargetingKey));
        }
    }

    [Fact]
    public void SdkFacadeEvaluatesWithoutProtocolDependenciesAndRedactsSalt()
    {
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var rule = new TargetingRule(
            TargetingMatchMode.All,
            [
                new TargetingCondition(
                    "plan",
                    "subscription.plan",
                    TargetingValueKind.Text,
                    TargetingOperator.Equals,
                    [TargetingValue.From("enterprise")]),
            ]);
        var context = new TargetingEvaluationContext(
            "target",
            applicationId,
            environmentId,
            attributes: new Dictionary<string, TargetingValue>
            {
                ["subscription.plan"] = TargetingValue.From("enterprise"),
            });
        var preview = new AsterloomTargetingBucketPreview(
            "feature",
            "new-home",
            "secret-salt",
            [new TargetingBucketAllocation("enabled", 0, 100_000)]);

        Assert.True(AsterloomTargetingEvaluator.Evaluate(rule, context).Matched);
        Assert.DoesNotContain("secret-salt", preview.ToString());
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Docs", "Architecture.md")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Asterloom repository root.");
    }

    private sealed record GoldenVectorDocument(
        int Version,
        string Algorithm,
        IReadOnlyList<GoldenVector> Vectors);

    private sealed record GoldenVector(
        string Name,
        string Namespace,
        string Salt,
        string TargetingKey,
        string FirstEightBytesHex,
        uint Bucket);
}
