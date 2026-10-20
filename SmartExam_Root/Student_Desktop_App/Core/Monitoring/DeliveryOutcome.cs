namespace Student_Desktop_App.Core.Monitoring;

/// <summary>What became of one monitoring upload (an event batch or a capture).</summary>
public enum DeliveryOutcome
{
    /// <summary>2xx: the server has it (or already had it: batches are idempotent).</summary>
    Delivered,
    /// <summary>No usable answer (network down, timeout, 401 while refreshing, 429, 5xx): keep it and retry later.</summary>
    Offline,
    /// <summary>The server refused it for good (session over, invalid, too large): drop it.</summary>
    Rejected
}

public static class Delivery
{
    /// <summary>Maps an HTTP status (null = no response at all) onto an outcome.</summary>
    public static DeliveryOutcome Classify(int? statusCode)
    {
        if (statusCode is null) return DeliveryOutcome.Offline;
        var code = statusCode.Value;
        if (code is >= 200 and < 300) return DeliveryOutcome.Delivered;
        if (code is 401 or 408 or 429 || code >= 500) return DeliveryOutcome.Offline;
        return DeliveryOutcome.Rejected;
    }
}
