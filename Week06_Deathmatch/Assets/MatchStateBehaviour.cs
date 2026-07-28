using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pure replicated shared state for the match — team scores and the
/// countdown timer. This class owns no game RULES (SessionManagerBehaviour
/// decides when scores change); it only holds the values, replicates them,
/// runs the countdown, and announces when the match ends.
/// </summary>
public class MatchStateBehaviour : NetworkBehaviour
{
    public static MatchStateBehaviour Instance { get; private set; }

    // Field initializers are required — an uninitialized NetworkVariable /
    // NetworkList is null at runtime and will throw.
    public NetworkList<int> TeamScores = new NetworkList<int>(new int[] { 0, 0 });
    public NetworkVariable<float> RemainingMatchTime = new NetworkVariable<float>(300f);

    private bool matchEnded;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        // Only the server drives the countdown. Without this guard, every
        // client would also try to write RemainingMatchTime.Value and log
        // permission errors every frame (it defaults to server-only write).
        if (!IsServer) return;
        if (matchEnded) return;

        RemainingMatchTime.Value -= Time.deltaTime;

        if (RemainingMatchTime.Value <= 0f)
        {
            RemainingMatchTime.Value = 0f;
            matchEnded = true;
            EndMatchClientRpc();
        }
    }

    [ClientRpc]
    private void EndMatchClientRpc()
    {
        string winner = TeamScores[0] == TeamScores[1]
            ? "Draw"
            : (TeamScores[0] > TeamScores[1] ? "Team 0" : "Team 1");

        Debug.Log($"[MatchState] Match ended. Score: {TeamScores[0]} - {TeamScores[1]}. Winner: {winner}");
    }
}
