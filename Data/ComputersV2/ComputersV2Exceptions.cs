using System.Net;

namespace _20strike_ui.Data.ComputersV2;

/// <summary>Base type for all failures raised by the v2 API client.</summary>
public class ComputersV2Exception : Exception
{
    public ComputersV2Exception(string message) : base(message) { }
}

/// <summary>Thrown when the backend does not answer within the per-call timeout.</summary>
public sealed class ComputersV2TimeoutException : ComputersV2Exception
{
    public ComputersV2TimeoutException(string message) : base(message) { }
}

/// <summary>Thrown when the backend returns a non-success status (other than rate limiting).</summary>
public sealed class ComputersV2ApiException : ComputersV2Exception
{
    public HttpStatusCode? StatusCode { get; }
    public ComputersV2ApiException(HttpStatusCode? statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Thrown when the backend signals rate limiting (HTTP 429 or a "Server is busy" style body).
/// Callers should treat it as retryable after a short delay rather than as a hard failure.
/// </summary>
public sealed class ComputersV2RateLimitedException : ComputersV2Exception
{
    public ComputersV2RateLimitedException(string message) : base(message) { }
}