namespace WallpaperControl;

internal enum TrackingProviderFailure
{
    MissingCredential, InvalidCredential, Unauthorized, Forbidden, RateLimit, QuotaExhausted,
    InvalidTrackingNumber, TrackerNotFound, ServiceError, Timeout, NetworkError, InvalidResponse
}

/// <summary>Safe to log: never retains transport exceptions, response messages, URLs or credentials.</summary>
internal sealed class TrackingProviderException : Exception
{
    internal TrackingProviderFailure Failure { get; }
    internal int? HttpStatusCode { get; }
    internal TrackingProviderException(TrackingProviderFailure failure, int? httpStatusCode = null)
        : base($"Tracking provider failure: {failure}.")
    { Failure = failure; HttpStatusCode = httpStatusCode; }
}
