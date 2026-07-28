using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative health for a networked character. Damage is only
/// ever applied via ServerApplyDamage — a plain server-only method, not a
/// client-facing ServerRpc — because the only thing allowed to deal damage
/// this week is a validated, rewound weapon hit (WeaponBehaviour.FireServerRpc,
/// which already runs on the server). There is deliberately no path for a
/// client to directly request damage on itself or anyone else.
/// </summary>
public class HealthBehaviour : NetworkBehaviour
{
    [SerializeField] private int maxHealth = 100;

    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            CurrentHealth.Value = maxHealth;
        }
    }

    /// <summary>
    /// Server-only. Applies damage and, if it kills the target, reports the
    /// kill to SessionManagerBehaviour (same kill-handling path as Week 06's
    /// K-key test trigger) and resets health so testing can continue without
    /// restarting the session.
    /// </summary>
    public void ServerApplyDamage(int amount, ulong killerClientId)
    {
        if (!IsServer) return;
        if (amount <= 0) return;

        CurrentHealth.Value = Mathf.Max(0, CurrentHealth.Value - amount);

        if (CurrentHealth.Value == 0)
        {
            SessionManagerBehaviour.Instance?.HandleKill(OwnerClientId, killerClientId);
            CurrentHealth.Value = maxHealth; // simple respawn-in-place for repeated testing
        }
    }
}
