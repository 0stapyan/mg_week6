using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// WASD movement — hardened for Week 10. Previously (Weeks 05–09) this used
/// an Owner Authoritative NetworkTransform: the owning client wrote
/// transform.position directly, and the server never saw or validated any
/// of it. That is exactly the "trust the client's position" vulnerability
/// this week's audit is about — a modified client could set transform.position
/// to anything, and every other player would see it, no questions asked.
///
/// Fix: NetworkTransform is now Server Authoritative (set in the Inspector).
/// The client only ever *requests* a move via RequestMoveServerRpc; the
/// server is the only thing that writes transform.position, and only after
/// validating the request against rate limits, world bounds, finite values,
/// and a speed-based distance check.
///
/// Trade-off: without a client-side prediction/anticipation system (that's
/// Week 05's territory, not this week's), movement now waits for a server
/// round trip before becoming visible locally. Acceptable here since the
/// focus this week is correctness of server-side validation, not smoothness.
/// </summary>
[RequireComponent(typeof(NetworkTransform))]
public class PlayerMovement : NetworkBehaviour
{
    [SerializeField] private float speed = 4f;
    [SerializeField] private float speedSafetyMargin = 1.5f; // tolerance for jitter/lag, not free speed-hacking room
    [SerializeField] private float worldBoundExtent = 50f; // playable area is [-50, 50] on X and Z

    // Send interval is decoupled from render framerate — a fixed ~20Hz
    // network tick, matching the spirit of NGO's own tick rate rather than
    // firing an RPC every single Update() frame (which would fight with any
    // reasonable per-client rate limit).
    private const float SendIntervalSeconds = 0.05f;
    private float timeSinceLastSend;

    // 20/s matches the send interval above; a small burst allowance covers
    // an occasional frame hitch causing two requests close together.
    private readonly RpcRateLimiter rateLimiter = new RpcRateLimiter(ratePerSecond: 20, burstCapacity: 10);

    // Server-side authoritative bookkeeping — never trust anything the
    // client reports as "current position"; track our own last-accepted
    // value and validate every new request against it.
    private Vector3 serverLastValidatedPosition;
    private double serverLastValidatedTime;
    private bool serverHasValidatedOnce;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            serverLastValidatedPosition = transform.position;
            serverLastValidatedTime = NetworkManager.Singleton.ServerTime.Time;
            serverHasValidatedOnce = true;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        timeSinceLastSend += Time.deltaTime;
        if (timeSinceLastSend < SendIntervalSeconds) return;

        Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        timeSinceLastSend = 0f;

        if (move.sqrMagnitude < 0.0001f) return; // nothing to request this tick

        Vector3 candidatePosition = transform.position + move * speed * SendIntervalSeconds;
        RequestMoveServerRpc(candidatePosition, NetworkManager.Singleton.ServerTime.Time);
    }

    [ServerRpc]
    private void RequestMoveServerRpc(Vector3 requestedPosition, double clientTime, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        double serverNow = NetworkManager.Singleton.ServerTime.Time;

        // --- Abuse vector 1: RPC spam ---
        if (!rateLimiter.TryConsume(senderId, "RequestMove", serverNow))
        {
            SecurityLog.LogRejection(senderId, "RequestMove", "rate limit exceeded");
            return;
        }

        // --- Abuse vector 2: NaN/Infinity injection ---
        if (!IsFinite(requestedPosition))
        {
            SecurityLog.LogRejection(senderId, "RequestMove", $"non-finite position {requestedPosition}");
            CorrectClient();
            return;
        }

        // --- Abuse vector 3: out-of-bounds teleport ---
        if (Mathf.Abs(requestedPosition.x) > worldBoundExtent || Mathf.Abs(requestedPosition.z) > worldBoundExtent)
        {
            SecurityLog.LogRejection(senderId, "RequestMove",
                $"position {requestedPosition} outside world bounds (±{worldBoundExtent})");
            CorrectClient();
            return;
        }

        // --- Abuse vector 4: speed-hack / teleport via arbitrary position ---
        // Use elapsed SERVER time since our own last accepted update, not a
        // value the client hands us — an arbitrary-length "elapsed time"
        // claim from the client would let a speed-hack pass by just also
        // lying about how much time passed.
        double elapsed = serverHasValidatedOnce
            ? Mathf.Max(0.0001f, (float)(serverNow - serverLastValidatedTime))
            : SendIntervalSeconds;

        float requestedDistance = Vector3.Distance(serverLastValidatedPosition, requestedPosition);
        float maxAllowedDistance = speed * (float)elapsed * speedSafetyMargin;

        if (requestedDistance > maxAllowedDistance)
        {
            SecurityLog.LogRejection(senderId, "RequestMove",
                $"distance {requestedDistance:F2} exceeds max allowed {maxAllowedDistance:F2} " +
                $"(elapsed {elapsed:F3}s, speed {speed})");
            CorrectClient();
            return;
        }

        // Valid — accept and update authoritative state. NetworkTransform
        // (Server Authoritative) replicates this to every client, including
        // the one that sent the request.
        transform.position = requestedPosition;
        serverLastValidatedPosition = requestedPosition;
        serverLastValidatedTime = serverNow;
        serverHasValidatedOnce = true;
    }

    /// <summary>
    /// Snaps the client back to the server's last known-good position.
    /// Per the brief's own pitfall warning: correcting movement without
    /// synchronizing the authoritative state back to the owning client
    /// leaves them out of sync. Simply writing transform.position here is
    /// enough — Server Authoritative NetworkTransform handles replicating
    /// the corrected value back down automatically.
    /// </summary>
    private void CorrectClient()
    {
        transform.position = serverLastValidatedPosition;
    }

    private static bool IsFinite(Vector3 v)
    {
        return !float.IsNaN(v.x) && !float.IsInfinity(v.x)
            && !float.IsNaN(v.y) && !float.IsInfinity(v.y)
            && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    }
}