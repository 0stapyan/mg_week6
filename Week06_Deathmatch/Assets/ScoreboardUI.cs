using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pure presentation layer. Reads replicated state from MatchStateBehaviour
/// and PlayerDataBehaviour and renders it — it never writes to any
/// NetworkVariable and contains no gameplay rules. Updates are driven
/// entirely by OnValueChanged / OnListChanged events (never polled in
/// Update()), and PlayerDataBehaviour.AnySpawned is used to catch players
/// that join after this UI is already active (late joiners).
/// </summary>
public class ScoreboardUI : MonoBehaviour
{
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private TMP_Text playersText;

    private readonly List<PlayerDataBehaviour> knownPlayers = new List<PlayerDataBehaviour>();

    private void Start()
    {
#if UNITY_SERVER
        // A headless dedicated server has no display — subscribing to
        // events just to update UI text that will never be seen is pointless
        // overhead. This is the Week 09 "#if !UNITY_SERVER" pattern applied
        // to UI instead of the Camera/Audio/Input examples in the brief.
        return;
#endif
        PlayerDataBehaviour.AnySpawned += OnPlayerSpawned;

        // Catch players that spawned before this UI existed (e.g. this UI is
        // instantiated slightly after connection, or on a late joiner's own
        // client where other players already have PlayerDataBehaviour spawned).
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            foreach (var obj in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
            {
                var data = obj.GetComponent<PlayerDataBehaviour>();
                if (data != null) OnPlayerSpawned(data);
            }
        }

        // Safe to read MatchStateBehaviour.Instance here: Unity guarantees
        // every object's Awake() in the scene has already run by the time any
        // Start() runs, so the singleton is never null at this point — unlike
        // OnEnable(), where execution order between different GameObjects
        // isn't guaranteed.
        if (MatchStateBehaviour.Instance != null)
        {
            MatchStateBehaviour.Instance.TeamScores.OnListChanged += OnScoresChanged;
            MatchStateBehaviour.Instance.RemainingMatchTime.OnValueChanged += OnTimeChanged;
            RefreshScoreText();
            RefreshTimeText(MatchStateBehaviour.Instance.RemainingMatchTime.Value);
        }
        else
        {
            Debug.LogWarning("[ScoreboardUI] MatchStateBehaviour.Instance was still null in Start() — " +
                              "check that a MatchState object with MatchStateBehaviour exists in the scene.");
        }
    }

    private void OnDisable()
    {
        PlayerDataBehaviour.AnySpawned -= OnPlayerSpawned;

        if (MatchStateBehaviour.Instance != null)
        {
            MatchStateBehaviour.Instance.TeamScores.OnListChanged -= OnScoresChanged;
            MatchStateBehaviour.Instance.RemainingMatchTime.OnValueChanged -= OnTimeChanged;
        }

        foreach (var player in knownPlayers)
        {
            if (player == null) continue;
            player.Kills.OnValueChanged -= HandlePlayerStatChanged;
            player.Deaths.OnValueChanged -= HandlePlayerStatChanged;
            player.TeamId.OnValueChanged -= HandlePlayerStatChanged;
        }
    }

    private void OnPlayerSpawned(PlayerDataBehaviour player)
    {
        if (knownPlayers.Contains(player)) return;
        knownPlayers.Add(player);

        player.Kills.OnValueChanged += HandlePlayerStatChanged;
        player.Deaths.OnValueChanged += HandlePlayerStatChanged;
        player.TeamId.OnValueChanged += HandlePlayerStatChanged;

        RefreshPlayersText();
    }

    private void HandlePlayerStatChanged(int previous, int current)
    {
        RefreshPlayersText();
    }

    private void OnScoresChanged(NetworkListEvent<int> changeEvent)
    {
        RefreshScoreText();
    }

    private void OnTimeChanged(float previous, float current)
    {
        RefreshTimeText(current);
    }

    private void RefreshScoreText()
    {
        if (scoreText == null || MatchStateBehaviour.Instance == null) return;
        var scores = MatchStateBehaviour.Instance.TeamScores;
        scoreText.text = $"Team 0: {scores[0]}   Team 1: {scores[1]}";
    }

    private void RefreshTimeText(float seconds)
    {
        if (timeText == null) return;
        int m = Mathf.FloorToInt(seconds / 60f);
        int s = Mathf.FloorToInt(seconds % 60f);
        timeText.text = $"{m:00}:{s:00}";
    }

    private void RefreshPlayersText()
    {
        if (playersText == null) return;

        var sb = new StringBuilder();
        foreach (var player in knownPlayers)
        {
            if (player == null) continue;
            sb.AppendLine($"Client {player.OwnerClientId} | Team {player.TeamId.Value} | " +
                          $"K:{player.Kills.Value} D:{player.Deaths.Value}");
        }
        playersText.text = sb.ToString();
    }
}
