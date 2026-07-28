using System;
using System.Collections.Generic;

/// <summary>
/// Reusable server-side rate limiter for ServerRpc calls. Token-bucket
/// algorithm: each (clientId, action) pair gets its own bucket that refills
/// at `ratePerSecond` tokens/sec up to `burstCapacity`. A small burst
/// allowance (per the brief's hint) avoids rejecting legitimate traffic
/// bursts caused by packet batching or a momentary hitch — a hard "N calls
/// per exact second" limiter would be too strict for real network jitter.
///
/// Not a NetworkBehaviour itself — plain C# so any script can own a private
/// instance scoped to whichever RPC(s) it wants to protect, or a shared
/// instance can be used if multiple RPCs on the same object should share
/// one budget (pass a distinct `action` string per RPC either way).
/// </summary>
public class RpcRateLimiter
{
    private class Bucket
    {
        public double tokens;
        public double lastRefillTime;
    }

    private readonly Dictionary<(ulong clientId, string action), Bucket> buckets = new();
    private readonly double ratePerSecond;
    private readonly double burstCapacity;

    public RpcRateLimiter(double ratePerSecond, double burstCapacity)
    {
        this.ratePerSecond = ratePerSecond;
        this.burstCapacity = burstCapacity;
    }

    /// <summary>
    /// Attempts to consume one token for this client+action at the given
    /// server time. Returns true if allowed, false if the caller is over
    /// their rate limit right now.
    /// </summary>
    public bool TryConsume(ulong clientId, string action, double serverTimeNow)
    {
        var key = (clientId, action);

        if (!buckets.TryGetValue(key, out var bucket))
        {
            bucket = new Bucket { tokens = burstCapacity, lastRefillTime = serverTimeNow };
            buckets[key] = bucket;
        }

        double elapsed = Math.Max(0, serverTimeNow - bucket.lastRefillTime);
        bucket.tokens = Math.Min(burstCapacity, bucket.tokens + elapsed * ratePerSecond);
        bucket.lastRefillTime = serverTimeNow;

        if (bucket.tokens >= 1.0)
        {
            bucket.tokens -= 1.0;
            return true;
        }

        return false;
    }
}