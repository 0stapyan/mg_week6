using UnityEngine;

/// <summary>
/// Single shared format for every server-side rejection, so the whole
/// project's security-relevant logs are greppable under one prefix instead
/// of each script inventing its own message shape. Uses LogWarning (not
/// LogError) — per the brief's own pitfall warning, flooding logs at high
/// severity for routine rejections makes the real, serious violations
/// harder to spot. Reserve anything louder for patterns that repeat.
/// </summary>
public static class SecurityLog
{
    public static void LogRejection(ulong clientId, string action, string reason)
    {
        Debug.LogWarning($"[SecurityAudit] {System.DateTime.UtcNow:O} | client={clientId} | " +
                          $"action={action} | reason={reason}");
    }
}