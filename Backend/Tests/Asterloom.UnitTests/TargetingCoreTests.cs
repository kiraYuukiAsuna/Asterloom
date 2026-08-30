using System.Text.Json;
using Asterloom.Targeting;

namespace Asterloom.UnitTests;

public sealed class TargetingCoreTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void BucketingMatchesVersionOneGoldenVectors()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "Protocol",
            "targeting-bucketing-v1-vectors.json");
        var document = JsonSerializer.Deserialize<GoldenVectorDocument>(
            File.ReadAllText(path),
            JsonOptions)!;

        Assert.Equal(1, document.Version);
        Assert.Equal("sha256-first-uint64be-mod-100000", document.Algorithm);
        Assert.Equal(7, document.Vectors.Count);
        foreach (var vector in document.Vectors)
        {
            Assert.Equal(
                vector.Bucket,
                TargetingContract.ComputeBucket(
                    vector.Namespace,
                    vector.Salt,
                    vector.TargetingKey));
        }
    }

    [Fact]
    public void EvaluatorShortCircuitsAndExplainsConditionOutcomes()
    {
        var applicationId = Guid.Parse("aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa");
        var environmentId = Guid.Parse("bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb");
        var context = new TargetingEvaluationContext(
            "stable-user-1",
            applicationId,
            environmentId,
            clientVersion: "1.2.3-beta.2",
            platform: "Windows",
            attributes: new Dictionary<string, TargetingValue>
            {
                ["account.age"] = TargetingValue.From(25d),
            });
        var rule = new TargetingRule(
            TargetingMatchMode.All,
            [
                Condition(
                    "platform",
                    "platform",
                    TargetingValueKind.Text,
                    TargetingOperator.Equals,
                    TargetingValue.From("windows")),
                Condition(
                    "version",
                    "clientVersion",
                    TargetingValueKind.Text,
                    TargetingOperator.SemanticVersionGreaterThan,
                    TargetingValue.From("1.2.3-beta.1")),
                Condition(
                    "age",
                    "account.age",
                    TargetingValueKind.Numeric,
                    TargetingOperator.GreaterThanOrEqual,
                    TargetingValue.From(21d)),
                Condition(
                    "missing",
                    "account.deleted",
                    TargetingValueKind.Truth,
                    TargetingOperator.NotExists),
            ]);

        var result = TargetingEvaluator.Evaluate(rule, context);

        Assert.True(result.Matched);
        Assert.Equal(4, result.Conditions.Count);
        Assert.Equal(
            TargetingConditionReason.MissingAttribute,
            result.Conditions[^1].Reason);

        var shortCircuit = TargetingEvaluator.Evaluate(
            rule with
            {
                Conditions =
                [
                    Condition(
                        "missing-equality",
                        "account.missing",
                        TargetingValueKind.Text,
                        TargetingOperator.NotEquals,
                        TargetingValue.From("value")),
                    rule.Conditions[0],
                ],
            },
            context);
        Assert.False(shortCircuit.Matched);
        Assert.Single(shortCircuit.Conditions);
        Assert.Equal(
            TargetingConditionReason.MissingAttribute,
            shortCircuit.Conditions[0].Reason);
    }

    [Fact]
    public void EvaluatorReportsCustomAttributeTypeMismatch()
    {
        var context = new TargetingEvaluationContext(
            "stable-user",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            attributes: new Dictionary<string, TargetingValue>
            {
                ["account.age"] = TargetingValue.From("twenty-five"),
            });
        var rule = new TargetingRule(
            TargetingMatchMode.Any,
            [
                Condition(
                    "age",
                    "account.age",
                    TargetingValueKind.Numeric,
                    TargetingOperator.GreaterThan,
                    TargetingValue.From(18d)),
            ]);

        var result = TargetingEvaluator.Evaluate(rule, context);

        Assert.False(result.Matched);
        Assert.Equal(TargetingConditionReason.TypeMismatch, result.Conditions[0].Reason);
    }

    [Fact]
    public void ContractRejectsPiiLikeCustomAttributesAndRedactsContext()
    {
        Assert.Throws<ArgumentException>(
            () => TargetingContract.ValidateCustomAttributeName("profile.email"));
        Assert.Throws<ArgumentException>(
            () => TargetingContract.ValidateCustomAttributeName("device_id"));
        TargetingContract.ValidateCustomAttributeName("subscription.plan");

        var context = new TargetingEvaluationContext(
            "secret-target",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            userId: "secret-user",
            attributes: new Dictionary<string, TargetingValue>
            {
                ["subscription.plan"] = TargetingValue.From("enterprise"),
            });

        Assert.DoesNotContain("secret-target", context.ToString());
        Assert.DoesNotContain("secret-user", context.ToString());
        Assert.DoesNotContain("enterprise", context.ToString());
    }

    [Fact]
    public void AllocationRangesAreLeftClosedAndRightOpen()
    {
        TargetingBucketAllocation[] allocations =
        [
            new("preview", 0, 12_500),
            new("stable", 12_500, 100_000),
        ];

        Assert.Equal("preview", TargetingContract.SelectBucketAllocation(0, allocations));
        Assert.Equal("preview", TargetingContract.SelectBucketAllocation(12_499, allocations));
        Assert.Equal("stable", TargetingContract.SelectBucketAllocation(12_500, allocations));
        Assert.Equal("stable", TargetingContract.SelectBucketAllocation(99_999, allocations));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TargetingContract.SelectBucketAllocation(100_000, allocations));
        Assert.Throws<ArgumentException>(() => TargetingContract.SelectBucketAllocation(
            500,
            [new("a", 0, 1_000), new("b", 999, 2_000)]));
    }

    private static TargetingCondition Condition(
        string id,
        string attribute,
        TargetingValueKind kind,
        TargetingOperator operation,
        params TargetingValue[] values) =>
        new(id, attribute, kind, operation, values);

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
