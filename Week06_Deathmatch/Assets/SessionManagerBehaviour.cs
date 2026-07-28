using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-only match orchestration. Owns the RULES of the match:
///   - assigns each newly-connected client to a team
///   - decides what happens when a kill occurs (who gets +1 kill, +1 death,
///     which team's score goes up)
///
/// This class does NOT own any replicated data itself (that lives on
/// PlayerDataBehaviour / MatchStateBehaviour) and does NOT touch UI (that's
/// ScoreboardUI's job). It's the "referee" — it makes decisions and tells the
/// data-holding classes to update themselves.
/// </summary>
public class SessionManagerBehaviour : NetworkBehaviour
{
    public static SessionManagerBehaviour Instance { get; private set; }

    private int nextTeamId; // alternates 0, 1, 0, 1, ... as clients connect

    private void Awake()
    {
        // Scene-placed singleton — exists before the network starts, per NGO's
        // recommended pattern for this kind of manager.
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        var playerData = FindPlayerData(clientId);
        if (playerData == null)
        {
            Debug.LogWarning($"[SessionManager] No PlayerDataBehaviour found for client {clientId} yet.");
            return;
        }

        playerData.TeamId.Value = nextTeamId;
        nextTeamId = 1 - nextTeamId;

        Debug.Log($"[SessionManager] Client {clientId} assigned to team {playerData.TeamId.Value}");
    }

    /// <summary>
    /// Called on the server whenever a player dies (in this exercise: via the
    /// K-key test trigger on PlayerDataBehaviour, standing in for real
    /// combat). Updates kill/death counts and the killer's team score.
    /// </summary>
    public void HandleKill(ulong victimClientId, ulong killerClientId)
    {
        if (!IsServer) return;

        var victim = FindPlayerData(victimClientId);
        var killer = FindPlayerData(killerClientId);

        if (victim != null)
        {
            victim.Deaths.Value += 1;
        }

        if (killer != null)
        {
            killer.Kills.Value += 1;

            var matchState = MatchStateBehaviour.Instance;
            int team = killer.TeamId.Value;
            if (matchState != null && team >= 0 && team < matchState.TeamScores.Count)
            {
                matchState.TeamScores[team] += 1;
            }
        }

        Debug.Log($"[SessionManager] Kill processed — killer: {killerClientId}, victim: {victimClientId}");
    }

    private PlayerDataBehaviour FindPlayerData(ulong clientId)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) &&
            client.PlayerObject != null)
        {
            return client.PlayerObject.GetComponent<PlayerDataBehaviour>();
        }
        return null;
    }
}
