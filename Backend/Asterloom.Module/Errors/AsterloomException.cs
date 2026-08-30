namespace Asterloom.Modules.Errors;

public enum AsterloomErrorKind
{
    Internal = 0,
    InvalidArgument = 1,
    NotFound = 2,
    AlreadyExists = 3,
    Conflict = 4,
    FailedPrecondition = 5,
    Unauthenticated = 6,
    PermissionDenied = 7,
}

public sealed class AsterloomException : Exception
{
    public AsterloomException()
        : this(
            AsterloomErrorKind.Internal,
            "internal_error",
            "An unexpected error occurred.")
    {
    }

    public AsterloomException(string message)
        : this(AsterloomErrorKind.Internal, "internal_error", message)
    {
    }

    public AsterloomException(string message, Exception innerException)
        : this(
            AsterloomErrorKind.Internal,
            "internal_error",
            message,
            innerException: innerException)
    {
    }

    public AsterloomException(
        AsterloomErrorKind kind,
        string errorCode,
        string message,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? fieldErrors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        Kind = kind;
        ErrorCode = errorCode;
        FieldErrors = fieldErrors
            ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    }

    public AsterloomErrorKind Kind { get; }

    public string ErrorCode { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors { get; }
}
