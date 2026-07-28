using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Maintains a short server-side history of this character's hitbox
/// positions (head + torso), so a shot can be rewound to the moment the
/// shooter actually saw the target, rather than judged against the target's
/// current (possibly already-moved) position.
///
/// Recording runs on the server only — it's the only machine that will ever
/// rewind this data. Uses a fixed-size circular buffer of ~20 entries
/// (1 second of history at 20 Hz), and NetworkManager.ServerTime.Time as the
/// timestamp — never Time.time, which is a local, unsynchronized clock and
/// would make cross-machine timestamps meaningless.
/// </summary>
public class HitboxHistoryBehaviour : NetworkBehaviour
{
    public struct Snapshot
    {
        public double time;
        public Vector3 headPosition;
        public Vector3 torsoPosition;
    }

    private const int BufferSize = 20;
    private const float RecordIntervalSeconds = 0.05f; // 20 Hz -> 1s of history

    [SerializeField] private Transform headPoint;
    [SerializeField] private Transform torsoPoint;

    private readonly Snapshot[] buffer = new Snapshot[BufferSize];
    private int writeIndex;
    private int count;
    private float timeSinceLastRecord;

    private void Update()
    {
        if (!IsServer) return;

        timeSinceLastRecord += Time.deltaTime;
        if (timeSinceLastRecord < RecordIntervalSeconds) return;
        timeSinceLastRecord = 0f;

        Record();
    }

    private void Record()
    {
        var snapshot = new Snapshot
        {
            time = NetworkManager.Singleton.ServerTime.Time,
            headPosition = headPoint != null ? headPoint.position : transform.position + Vector3.up * 1.6f,
            torsoPosition = torsoPoint != null ? torsoPoint.position : transform.position + Vector3.up * 1.0f
        };

        buffer[writeIndex] = snapshot;
        writeIndex = (writeIndex + 1) % BufferSize;
        count = Mathf.Min(count + 1, BufferSize);
    }

    /// <summary>
    /// Finds the two recorded snapshots that bracket the given timestamp and
    /// linearly interpolates between them. Returns false if there isn't
    /// enough history yet, or the requested time is older than everything
    /// currently buffered (rewind window exceeded — caller should already
    /// have rejected that case before calling this).
    /// </summary>
    public bool TryGetInterpolatedSnapshot(double atTime, out Snapshot result)
    {
        result = default;
        if (count < 2) return false;

        Snapshot newer = default;
        bool haveNewer = false;

        for (int i = 0; i < count; i++)
        {
            int index = (writeIndex - 1 - i + BufferSize) % BufferSize;
            Snapshot candidate = buffer[index];

            if (!haveNewer)
            {
                newer = candidate;
                haveNewer = true;
                continue;
            }

            Snapshot older = candidate;

            if (older.time <= atTime && atTime <= newer.time)
            {
                float t = (newer.time - older.time) > 0.0001
                    ? (float)((atTime - older.time) / (newer.time - older.time))
                    : 0f;

                result = new Snapshot
                {
                    time = atTime,
                    headPosition = Vector3.Lerp(older.headPosition, newer.headPosition, t),
                    torsoPosition = Vector3.Lerp(older.torsoPosition, newer.torsoPosition, t)
                };
                return true;
            }

            newer = older;
        }

        return false; // atTime is older than our entire buffer
    }
}
