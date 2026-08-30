namespace Asterloom.ReferenceApp.Client;

internal sealed record DiagnosticResult(
    string Capability,
    bool Succeeded,
    long DurationMilliseconds,
    string Detail,
    string ErrorCode,
    string Error);
