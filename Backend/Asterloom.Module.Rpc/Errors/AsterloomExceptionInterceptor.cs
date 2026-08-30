using System.Diagnostics;
using Asterloom.Modules.Diagnostics;
using Asterloom.Modules.Errors;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using RpcStatus = Google.Rpc.Status;

namespace Asterloom.Modules.Rpc.Errors;

internal sealed class AsterloomExceptionInterceptor(
    ILogger<AsterloomExceptionInterceptor> logger,
    IEnumerable<ITechnicalDiagnosticSink> diagnosticSinks,
    TimeProvider timeProvider) : Interceptor
{
    private static readonly Action<ILogger, string, string, Exception?> LogUnhandledRpcFailure =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1001, nameof(LogUnhandledRpcFailure)),
            "Unhandled RPC failure for {GrpcMethod}. Request ID: {RequestId}.");
    private static readonly Action<ILogger, string, Exception?> LogDiagnosticCaptureFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1002, nameof(LogDiagnosticCaptureFailure)),
            "Failed to capture a technical diagnostic for request {RequestId}.");

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation) =>
        ExecuteAsync(() => continuation(request, context), context, request as IMessage);

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation) =>
        ExecuteAsync(() => continuation(requestStream, context), context, request: null);

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation) =>
        ExecuteAsync(
            () => continuation(request, responseStream, context),
            context,
            request as IMessage);

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation) =>
        ExecuteAsync(
            () => continuation(requestStream, responseStream, context),
            context,
            request: null);

    private async Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        ServerCallContext context,
        IMessage? request)
    {
        try
        {
            return await action();
        }
        catch (Exception exception)
        {
            throw await MapExceptionAsync(exception, context, request);
        }
    }

    private async Task ExecuteAsync(
        Func<Task> action,
        ServerCallContext context,
        IMessage? request)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            throw await MapExceptionAsync(exception, context, request);
        }
    }

    private async Task<RpcException> MapExceptionAsync(
        Exception exception,
        ServerCallContext context,
        IMessage? request)
    {
        if (exception is RpcException rpcException)
        {
            return rpcException;
        }

        var requestId = context.GetHttpContext().TraceIdentifier;
        if (exception is AsterloomException asterloomException)
        {
            return CreateRpcException(
                MapStatusCode(asterloomException.Kind),
                asterloomException.ErrorCode,
                asterloomException.Message,
                requestId,
                asterloomException.FieldErrors);
        }

        if (exception is OperationCanceledException
            && context.CancellationToken.IsCancellationRequested)
        {
            return CreateRpcException(
                StatusCode.Cancelled,
                "request_cancelled",
                "The request was cancelled.",
                requestId);
        }

        LogUnhandledRpcFailure(logger, context.Method, requestId, exception);
        await CaptureDiagnosticAsync(exception, context, request, requestId);
        return CreateRpcException(
            StatusCode.Internal,
            "internal_error",
            "An unexpected error occurred.",
            requestId);
    }

    private async Task CaptureDiagnosticAsync(
        Exception exception,
        ServerCallContext context,
        IMessage? request,
        string requestId)
    {
        if (!diagnosticSinks.Any())
        {
            return;
        }

        var activity = Activity.Current;
        var now = timeProvider.GetUtcNow();
        var diagnostic = new TechnicalDiagnostic(
            Guid.CreateVersion7(now),
            ParseOptionalId(request, "tenant_id"),
            ParseOptionalId(request, "application_id"),
            ParseOptionalId(request, "environment_id"),
            "Asterloom.Server",
            Truncate(exception.GetType().FullName ?? exception.GetType().Name, 300),
            Truncate(exception.Message, 1_000),
            Truncate(context.Method, 500),
            activity?.TraceId.ToHexString() ?? string.Empty,
            activity?.SpanId.ToHexString() ?? string.Empty,
            Truncate(requestId, 200),
            now);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        foreach (var sink in diagnosticSinks)
        {
            try
            {
                await sink.RecordAsync(diagnostic, timeout.Token);
            }
            catch (Exception sinkException)
            {
                LogDiagnosticCaptureFailure(logger, requestId, sinkException);
            }
        }
    }

    private static Guid? ParseOptionalId(IMessage? message, string fieldName)
    {
        var field = message?.Descriptor.FindFieldByName(fieldName);
        var value = field?.Accessor.GetValue(message!) as string;
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static RpcException CreateRpcException(
        StatusCode statusCode,
        string errorCode,
        string message,
        string requestId,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? fieldErrors = null)
    {
        var errorInfo = new ErrorInfo
        {
            Domain = "asterloom.io",
            Reason = errorCode,
        };
        errorInfo.Metadata.Add("requestId", requestId);

        var richStatus = new RpcStatus
        {
            Code = (int)statusCode,
            Message = message,
        };
        richStatus.Details.Add(Any.Pack(errorInfo));

        if (fieldErrors is { Count: > 0 })
        {
            var badRequest = new BadRequest();
            foreach (var fieldError in fieldErrors)
            {
                badRequest.FieldViolations.AddRange(
                    fieldError.Value.Select(description => new BadRequest.Types.FieldViolation
                    {
                        Field = fieldError.Key,
                        Description = description,
                    }));
            }

            richStatus.Details.Add(Any.Pack(badRequest));
        }

        var trailers = new Metadata
        {
            { "grpc-status-details-bin", richStatus.ToByteArray() },
            { "x-asterloom-error-code", errorCode },
            { "x-request-id", requestId },
        };
        return new RpcException(new Grpc.Core.Status(statusCode, message), trailers);
    }

    private static StatusCode MapStatusCode(AsterloomErrorKind kind) => kind switch
    {
        AsterloomErrorKind.InvalidArgument => StatusCode.InvalidArgument,
        AsterloomErrorKind.NotFound => StatusCode.NotFound,
        AsterloomErrorKind.AlreadyExists => StatusCode.AlreadyExists,
        AsterloomErrorKind.Conflict => StatusCode.Aborted,
        AsterloomErrorKind.FailedPrecondition => StatusCode.FailedPrecondition,
        AsterloomErrorKind.Unauthenticated => StatusCode.Unauthenticated,
        AsterloomErrorKind.PermissionDenied => StatusCode.PermissionDenied,
        _ => StatusCode.Internal,
    };
}
