using System.Globalization;
using System.Text;
using Asterloom.Modules.Errors;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Auditing;

public sealed class AuditManagementService(IAuditStore store, TimeProvider timeProvider)
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private const int DefaultExportRows = 1000;
    private const int MaximumExportRows = 10_000;

    public async Task<AuditListResult> ListAsync(
        int pageSize,
        string? pageToken,
        string? actorId,
        string? operation,
        AuditOutcome? outcome,
        string? requestId,
        DateTimeOffset? fromAt,
        DateTimeOffset? toAt,
        CancellationToken cancellationToken)
    {
        var request = CreateRequest(
            pageSize,
            pageToken,
            actorId,
            operation,
            outcome,
            requestId,
            fromAt,
            toAt);
        var page = await store.ListAsync(request, cancellationToken);
        return new AuditListResult(
            page.Items,
            page.HasMore
                ? EncodeOffset(request.Offset + page.Items.Count)
                : string.Empty);
    }

    public async Task<AsterloomAuditEvent> GetAsync(
        string auditEventId,
        CancellationToken cancellationToken) =>
        await store.GetAsync(ParseId(auditEventId), cancellationToken)
        ?? throw new AsterloomException(
            AsterloomErrorKind.NotFound,
            "audit_event_not_found",
            "The audit event was not found.");

    public async Task<AuditExportResult> ExportAsync(
        string? actorId,
        string? operation,
        AuditOutcome? outcome,
        string? requestId,
        DateTimeOffset? fromAt,
        DateTimeOffset? toAt,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        var rowLimit = maximumRows == 0 ? DefaultExportRows : maximumRows;
        if (rowLimit is < 1 or > MaximumExportRows)
        {
            throw Invalid(
                "maximumRows",
                $"Maximum rows must be between 1 and {MaximumExportRows}.");
        }

        var rows = new List<AsterloomAuditEvent>(Math.Min(rowLimit, 1024));
        var offset = 0;
        while (rows.Count < rowLimit)
        {
            var request = CreateRequest(
                Math.Min(MaximumPageSize, rowLimit - rows.Count),
                EncodeOffset(offset),
                actorId,
                operation,
                outcome,
                requestId,
                fromAt,
                toAt);
            var page = await store.ListAsync(request, cancellationToken);
            rows.AddRange(page.Items);
            if (!page.HasMore || page.Items.Count == 0)
            {
                break;
            }

            offset += page.Items.Count;
        }

        var csv = CreateCsv(rows);
        var stamp = timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return new AuditExportResult(
            $"asterloom-audit-{stamp}.csv",
            "text/csv; charset=utf-8",
            Encoding.UTF8.GetBytes(csv),
            rows.Count);
    }

    private static AuditPageRequest CreateRequest(
        int pageSize,
        string? pageToken,
        string? actorId,
        string? operation,
        AuditOutcome? outcome,
        string? requestId,
        DateTimeOffset? fromAt,
        DateTimeOffset? toAt)
    {
        var normalizedSize = pageSize == 0 ? DefaultPageSize : pageSize;
        if (normalizedSize is < 1 or > MaximumPageSize)
        {
            throw Invalid(
                "pageSize",
                $"Page size must be between 1 and {MaximumPageSize}.");
        }

        if (outcome is not null && outcome is not (
                AuditOutcome.Succeeded or AuditOutcome.Denied or AuditOutcome.Failed))
        {
            throw Invalid("outcome", "Audit outcome is invalid.");
        }

        if (fromAt is not null && toAt is not null && fromAt > toAt)
        {
            throw Invalid("fromAt", "From time must not be after To time.");
        }

        return new AuditPageRequest(
            DecodeOffset(pageToken),
            normalizedSize,
            Normalize(actorId, 200),
            Normalize(operation, 300),
            outcome,
            Normalize(requestId, 200),
            fromAt,
            toAt);
    }

    private static string CreateCsv(IEnumerable<AsterloomAuditEvent> rows)
    {
        var csv = new StringBuilder(
            "created_at,outcome,actor_id,operation,resource_type,resource_id," +
            "tenant_id,application_id,environment_id,request_id,error_code,change_summary\r\n");
        foreach (var row in rows)
        {
            AppendCsvRow(
                csv,
                row.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                row.Outcome.ToString(),
                row.ActorId,
                row.Operation,
                row.ResourceType,
                row.ResourceId,
                row.TenantId?.ToString("D") ?? string.Empty,
                row.ApplicationId?.ToString("D") ?? string.Empty,
                row.EnvironmentId?.ToString("D") ?? string.Empty,
                row.RequestId,
                row.ErrorCode,
                row.ChangeSummary);
        }

        return csv.ToString();
    }

    private static void AppendCsvRow(StringBuilder output, params string[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                output.Append(',');
            }

            var value = NeutralizeSpreadsheetFormula(values[index]);
            output.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }

        output.Append("\r\n");
    }

    private static string NeutralizeSpreadsheetFormula(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value
            : value;

    private static int DecodeOffset(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return 0;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            if (int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                && offset >= 0)
            {
                return offset;
            }
        }
        catch (FormatException)
        {
        }

        throw Invalid("pageToken", "Page token is invalid.");
    }

    private static string EncodeOffset(int offset) => WebEncoders.Base64UrlEncode(
        Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));

    private static string Normalize(string? value, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maximumLength)
        {
            throw Invalid("filter", $"Filter must not exceed {maximumLength} characters.");
        }

        return normalized;
    }

    private static Guid ParseId(string value)
    {
        if (Guid.TryParse(value, out var id) && id != Guid.Empty)
        {
            return id;
        }

        throw Invalid("auditEventId", "A valid identifier is required.");
    }

    private static AsterloomException Invalid(string field, string message) => new(
        AsterloomErrorKind.InvalidArgument,
        "validation_failed",
        "One or more fields are invalid.",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = [message],
        });
}

public sealed record AuditListResult(
    IReadOnlyList<AsterloomAuditEvent> Items,
    string NextPageToken);

public sealed record AuditExportResult(
    string FileName,
    string ContentType,
    byte[] Content,
    int ExportedRows);
