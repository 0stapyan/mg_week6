using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative hitscan weapon with lag compensation. The shooter's
/// client sends where and when they saw the shot happen; the server rewinds
/// every potential target's hitbox history to that timestamp and re-traces
/// the shot against interpolated hitbox spheres.
///
/// Uses manual ray-vs-sphere math (approach (a) from the assignment) rather
/// than moving live colliders: Physics.Raycast only ever traces against
/// LIVE colliders at their CURRENT positions — there's no way to raycast
/// against an arbitrary historical position without either this manual math
/// or physically teleporting colliders (approach (b), more error-prone).
/// </summary>
public class WeaponBehaviour : NetworkBehaviour
{
    [SerializeField] private float headRadius = 0.25f;
    [SerializeField] private float torsoRadius = 0.4f;
    [SerializeField] private float maxRewindSeconds = 0.25f;
    [SerializeField] private int damageOnHit = 34;

    // 8 shots/sec sustained, with a small burst allowance for legitimate
    // packet-batching hiccups — well above any reasonable weapon fire rate
    // for this project, but low enough to stop a scripted fire-spam abuse.
    private readonly RpcRateLimiter rateLimiter = new RpcRateLimiter(ratePerSecond: 8, burstCapacity: 12);

    [ServerRpc]
    public void FireServerRpc(Vector3 origin, Vector3 direction, double clientFireTime, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        double serverNow = NetworkManager.Singleton.ServerTime.Time;

        if (!rateLimiter.TryConsume(senderId, "Fire", serverNow))
        {
            SecurityLog.LogRejection(senderId, "Fire", "rate limit exceeded");
            return;
        }

        double age = serverNow - clientFireTime;

        // Reject shots claiming to be older than we're willing to rewind.
        // Without this clamp, a high-ping player could claim a shot from far
        // enough in the past to hit someone who has long since moved away —
        // the classic "around the corner" lag-comp abuse case. Also reject
        // negative age (a client claiming a shot from the future).
        if (age > maxRewindSeconds || age < 0)
        {
            SecurityLog.LogRejection(senderId, "Fire",
                $"rewind age {age:F3}s outside allowed [0, {maxRewindSeconds}]s");
            return;
        }

        direction.Normalize();

        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            if (kvp.Key == senderId) continue; // no self-hits

            var targetObject = kvp.Value.PlayerObject;
            if (targetObject == null) continue;

            var history = targetObject.GetComponent<HitboxHistoryBehaviour>();
            var health = targetObject.GetComponent<HealthBehaviour>();
            if (history == null || health == null) continue;

            if (!history.TryGetInterpolatedSnapshot(clientFireTime, out var snapshot))
                continue; // not enough history yet, or too old for this target

            if (RaySphereHit(origin, direction, snapshot.headPosition, headRadius))
            {
                DrawDebugHit(snapshot.headPosition, headRadius);
                Debug.Log($"[Weapon] HIT (head) — shooter {senderId} -> target {kvp.Key}, age {age:F3}s");
                health.ServerApplyDamage(damageOnHit * 2, senderId);
                return;
            }

            if (RaySphereHit(origin, direction, snapshot.torsoPosition, torsoRadius))
            {
                DrawDebugHit(snapshot.torsoPosition, torsoRadius);
                Debug.Log($"[Weapon] HIT (torso) — shooter {senderId} -> target {kvp.Key}, age {age:F3}s");
                health.ServerApplyDamage(damageOnHit, senderId);
                return;
            }
        }

        Debug.Log($"[Weapon] Shot from client {senderId} missed (age {age:F3}s).");
    }

    /// <summary>
    /// Manual ray-vs-sphere intersection test. Fast, allocation-free, and
    /// doesn't touch any physics/collider state — safe to call against
    /// positions that don't correspond to any live collider right now.
    /// </summary>
    private static bool RaySphereHit(Vector3 rayOrigin, Vector3 rayDir, Vector3 sphereCenter, float radius)
    {
        Vector3 m = rayOrigin - sphereCenter;
        float b = Vector3.Dot(m, rayDir);
        float c = Vector3.Dot(m, m) - radius * radius;

        if (c > 0f && b > 0f) return false; // origin outside sphere, pointing away

        float discriminant = b * b - c;
        if (discriminant < 0f) return false; // no intersection

        float distance = -b - Mathf.Sqrt(discriminant);
        return distance >= 0f || c <= 0f; // hit in front of origin, or origin already inside sphere
    }

    private void DrawDebugHit(Vector3 center, float radius)
    {
        // Debug.DrawLine persists in the Editor Scene/Game view (with Gizmos
        // toggled on) for the given duration. OnDrawGizmos has no duration
        // concept and only runs in the Scene view, so it can't show a shot
        // that already happened by the time you look.
        Debug.DrawLine(center + Vector3.up * radius, center - Vector3.up * radius, Color.red, 2f);
        Debug.DrawLine(center + Vector3.left * radius, center + Vector3.right * radius, Color.red, 2f);
        Debug.DrawLine(center + Vector3.forward * radius, center + Vector3.back * radius, Color.red, 2f);
    }
}