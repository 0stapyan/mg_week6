using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Per-player replicated state — team assignment, kills, deaths. This class
/// only holds and replicates data; it never decides game rules (that's
/// SessionManagerBehaviour's job) and never touches UI directly (that's
/// ScoreboardUI's job). Other systems read these NetworkVariables and/or
/// subscribe to their OnValueChanged events.
/// </summary>
public class PlayerDataBehaviour : NetworkBehaviour
{
    public NetworkVariable<int> TeamId = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> Kills = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> Deaths = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Fired on every machine (server and clients) whenever a
    /// PlayerDataBehaviour spawns. ScoreboardUI subscribes to this so it can
    /// discover players as they connect, including late joiners, without
    /// polling every frame.
    /// </summary>
    public static event Action<PlayerDataBehaviour> AnySpawned;

    public override void OnNetworkSpawn()
    {
        AnySpawned?.Invoke(this);
    }

    // --- Test-only kill trigger ---
    // Real combat/projectiles are out of scope for this framework exercise;
    // the assignment only requires that "on player death, the server calls
    // HandleKill(victimId, killerId)". Press K to simulate killing any
    // currently-known player on a different team, the same way Week 04 used
    // F/G to simulate valid/invalid damage without a real weapon.
    private void Update()
    {
        if (!IsOwner) return;

        if (Input.GetKeyDown(KeyCode.K))
        {
            RequestTestKillServerRpc();
        }
    }

    // Modest budget — this is a manual test trigger, not something that
    // should ever legitimately fire more than a couple times a second.
    private readonly RpcRateLimiter rateLimiter = new RpcRateLimiter(ratePerSecond: 2, burstCapacity: 3);

    [ServerRpc]
    private void RequestTestKillServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (!rateLimiter.TryConsume(senderId, "TestKill", NetworkManager.Singleton.ServerTime.Time))
        {
            SecurityLog.LogRejection(senderId, "TestKill", "rate limit exceeded");
            return;
        }

        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            if (kvp.Key == senderId) continue;

            var otherData = kvp.Value.PlayerObject != null
                ? kvp.Value.PlayerObject.GetComponent<PlayerDataBehaviour>()
                : null;

            if (otherData != null && otherData.TeamId.Value != TeamId.Value)
            {
                SessionManagerBehaviour.Instance.HandleKill(kvp.Key, senderId);
                return;
            }
        }

        Debug.Log("[PlayerData] No opposing-team player found to test-kill.");
    }
}