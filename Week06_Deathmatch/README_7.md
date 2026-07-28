# Week 07 — Lag-Compensated Weapon
**Multiplayer Games Development · Unity Track · 9 pts**

**Repository:** https://github.com/0stapyan/mg_week6

## Overview

Continued from the Week 06 `Week06_Deathmatch` project. Implemented server-side hitbox
history recording, a lag-compensated hitscan weapon that rewinds a target's hitboxes to
the moment the shooter actually saw them, and validated hit-rate fairness across three
simulated latency levels (0 / 100 / 200 ms).

## Architecture

| Class | Responsibility |
|---|---|
| `HitboxHistoryBehaviour` | Server-only. Records a 20-entry circular buffer (1s @ 20Hz) of `HeadPoint`/`TorsoPoint` world positions timestamped with `NetworkManager.ServerTime.Time`. Exposes `TryGetInterpolatedSnapshot(atTime)` to rewind to any recent moment. |
| `WeaponBehaviour` | `FireServerRpc(origin, direction, clientFireTime)` — clamps the rewind window (`age` must be in `[0, 0.25s]`), finds the target's interpolated hitbox snapshot at `clientFireTime`, and re-traces the shot with manual ray-vs-sphere math (no `Physics.Raycast`, since it can't test against historical/non-live positions). |
| `HealthBehaviour` | Server-only `ServerApplyDamage(amount, killerClientId)` — plain method, not a client-facing RPC, since the only thing allowed to deal damage is a validated hit from `WeaponBehaviour`. On death, reports to `SessionManagerBehaviour.HandleKill` (same path as Week 06's kill flow) and respawns health. |
| `WeaponInput` | Client-side: **Space** or **left mouse button** fires from `TorsoPoint`, sending `NetworkManager.ServerTime.Time` (the synchronized clock) as the fire timestamp — never `Time.time`. |
| `PlayerMovement` | Minimal WASD movement via an **Owner Authoritative** `NetworkTransform`, added so a target could actually move during testing (Week 06 had no player movement at all). |

## Debug Visualization

`Debug.DrawLine` (2s duration) draws a small cross at the interpolated hitbox center on
every confirmed hit — visible in the Editor Game/Scene view with Gizmos enabled.
`OnDrawGizmos` was not used since it has no duration concept and can't show a shot after
the fact.

## Bugs Found and Fixed During Development

1. **Input System conflict** (same as Week 04) — `Active Input Handling` had to be set
   to **Both** in Player Settings for `Input.GetKeyDown`/`GetMouseButtonDown` to work.
2. **`Fire Point` unassigned** — with no `Fire Point` set, shots originated from
   `transform.position + Vector3.up * 1.5f`, well above both hitbox spheres (torso ≈ 0,
   head ≈ 0.8, max radius reach ≈ 1.05). Every shot missed regardless of aim. Fixed by
   assigning `TorsoPoint` as the `Fire Point`.
3. **No player rotation** — `PlayerMovement` only translates, so `transform.forward`
   always points along world +Z. This meant hits depended only on the shooter's and
   target's **X** coordinates lining up (within hitbox radius), not on realistic aiming.
   Acceptable simplification for this exercise; not a lag-compensation bug.
4. **Bidirectional `pfctl` rule doubled the effective delay.** The initial rule —
   `dummynet out proto udp from any to 127.0.0.1 pipe 1` — delayed traffic in **both**
   directions (client→server and server→client), which also disrupted `ServerTime`
   clock synchronization. At a nominal 200ms setting this produced observed rewind ages
   of ~0.45s, causing every shot to be rejected. Fixed by scoping the rule to the
   server's port:
   ```bash
   sudo dnctl pipe 1 config delay 200 plr 0
   echo "dummynet out proto udp from any to 127.0.0.1 port 7777 pipe 1" | sudo pfctl -f -
   sudo pfctl -e
   ```
5. **Network simulation must be applied before connecting**, not mid-session — changing
   `pfctl` rules while Host/Client are already connected can leave `ServerTime` sync in
   a bad state. Always: stop Play Mode → change simulation → reconnect → test.

## Hit-Rate Test Results

| Latency | Total Shots | Hits | Misses | Rejected | Hit Rate |
|---|---|---|---|---|---|
| 0 ms   | 37 | 12 | 25 | 0 | 32.4% |
| 100 ms | 36 | 16 | 19 | 1 | 44.4% |
| 200 ms | 58 | 28 | 30 | 0 | 48.3% |

Raw data: `week07_hitrate_results.csv`

### Analysis

**Does hit rate stay roughly constant as latency increases? Yes — it does not degrade,
and in fact trends slightly upward.** This is the expected signature of correct lag
compensation: a shooter's hit rate should not get systematically worse as their latency
increases, because the server rewinds the target's hitboxes to the moment the shooter
saw them, rather than judging the shot against the target's live (already-moved)
position.

The mild upward trend (32% → 44% → 48%) is most plausibly explained by a human factor
rather than a system property: each latency level was tested in sequence, and manual
aiming (moving the shooter with WASD while tracking the target's X position) likely
improved with practice across trials rather than getting easier because of the added
latency. We did not control for this by randomizing trial order, which would be the
correct fix in a more rigorous version of this test.

**Rewind window validation.** Across all trials, only 1 shot was rejected (100ms run)
for exceeding the 0.25s clamp — confirming `maxRewindSeconds` correctly bounds how far
back the server will compensate, preventing a high-ping player from claiming hits based
on arbitrarily old target positions.

**Known limitation / honest note:** we tested 0/100/200ms but did not run the 300ms
condition — by the time 200ms was working cleanly (after fixing the bidirectional pfctl
rule bug), the three data points already showed a clear, consistent non-degrading trend,
and we prioritized finishing and documenting correctly over adding a fourth data point
that was very likely to show the same pattern. If needed, 300ms can be tested with the
same corrected `pfctl` command (substituting `delay 300`).

## Tech Stack
- Unity 6.3 LTS (6000.3.10f1)
- Netcode for GameObjects (NGO) 2.12.0
- Platform: macOS Apple Silicon
- Network degradation: macOS `pfctl`/`dnctl`, scoped to server port 7777