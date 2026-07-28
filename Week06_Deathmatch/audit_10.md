# Week 10 — Server-Side Security Audit
**Multiplayer Games Development · Unity Track · 9 pts**

Audit of the project as it stood after Week 09 (dedicated server + Weeks 06/07/08
features combined). Five issues identified and hardened below.

---

## 1. Weapon Fire Spam (Denial of Service)

**Vulnerability:** `WeaponBehaviour.FireServerRpc` had no limit on how often a client
could call it. A modified client (or a simple script hooking the input call) could
invoke it hundreds of times per second.

**Attack scenario:** A player scripts their client to spam `FireServerRpc` as fast as
the network allows. Each call runs a loop over every connected player doing
ray-vs-sphere math and hitbox-history lookups — cheap individually, but at thousands of
calls/sec from one abusive client this becomes a real CPU cost on the server, degrading
the experience for every other player (a single-client DoS against the shared server).

**Mitigation:** Added a token-bucket `RpcRateLimiter` (8 calls/sec sustained, burst 12)
keyed per `clientId`. Calls beyond the budget are rejected and logged, no further
processing occurs.

**Why server-side:** A client-side "don't let me click too fast" cooldown is trivially
bypassed by anyone who edits or replaces the client binary. The server is the only
machine an attacker cannot directly control, so it's the only place a limit can't be
disabled by the attacker themselves.

---

## 2. Test-Kill Trigger Spam

**Vulnerability:** `PlayerDataBehaviour.RequestTestKillServerRpc` (the K-key test
trigger from Week 06) had the same unlimited-call problem as #1, but here the impact is
worse than wasted CPU: each successful call directly mutates game state (kills, deaths,
team score) via `SessionManagerBehaviour.HandleKill`.

**Attack scenario:** Spamming this RPC lets one client rack up an arbitrary number of
kills/team-score increments per second, trivially winning any match regardless of
actual play.

**Mitigation:** Same `RpcRateLimiter` pattern, tuned much stricter (2/sec, burst 3) since
this is meant to fire at most a couple of times in ordinary use, not continuously.

**Why server-side:** This directly demonstrates the brief's pitfall about confusing
ownership with argument/frequency validation — `RequireOwnership` (default, since this
RPC has no `RequireOwnership = false`) only controls *who* may call it, not *how often*
or with *what effect*. A legitimate, correctly-owned caller can still abuse frequency.

---

## 3. Speed-Hack / Position Teleport via `PlayerMovement`

**Vulnerability:** Before this week, `PlayerMovement` used an **Owner Authoritative**
`NetworkTransform` — the owning client wrote `transform.position` directly every frame,
and the server never validated (or even looked at) the result before it replicated to
everyone else. A modified client could set its own position to anywhere in the scene,
instantly, with zero server pushback.

**Attack scenario:** A cheat client sets `transform.position` directly to stand behind
an opponent, teleport across the map instantly, or dodge incoming fire by "speed
hacking" (moving many multiples of the intended max speed every frame).

**Mitigation:** Converted the whole movement path to **Server Authoritative**. The
client now only ever sends a *requested* position via `RequestMoveServerRpc`; the
server tracks its own last-accepted position/time per player and rejects (correcting
the client back) any request whose distance exceeds
`speed × elapsed_server_time × safetyMargin`.

**Why server-side:** This is the textbook case for why client-side validation alone is
insufficient — the client *is* the adversary in this scenario. Any check living only in
client code is exactly the code an attacker modifies or deletes first.

---

## 4. Non-Finite (NaN / Infinity) Position Injection

**Vulnerability:** Even after adding a distance check, a malicious client could send
`Vector3(float.NaN, 0, 0)` or `Vector3(Infinity, 0, 0)` as the requested position.
Depending on how downstream code uses that position (interpolation, `Vector3.Distance`,
UI text, physics), this can silently corrupt replicated state for every client, or in
some engines/versions crash physics or rendering outright.

**Attack scenario:** A crafted RPC payload with non-finite floats bypasses naive
"is this too far away" checks (since `NaN` comparisons are always false, a distance
check using `>` may simply fail to reject it) and propagates a corrupted, unrenderable
transform to every connected client.

**Mitigation:** Explicit `IsFinite()` check on every component of the requested position,
rejecting and correcting before the distance/bounds checks even run.

**Why server-side:** The malformed value only needs to reach the server once to
corrupt shared, replicated state for everyone — a client-only check does nothing to
protect the other players from a different, non-compliant client.

---

## 5. Out-of-World-Bounds Movement

**Vulnerability:** No check existed anywhere for whether a requested position was
inside the intended playable area at all.

**Attack scenario:** A player moves (or teleports) far outside the designed level
bounds — trivially escaping objective areas, hiding outside normal camera/collision
ranges, or in a game with level streaming, forcing every other client to try to load
geometry that was never meant to be visible simultaneously.

**Mitigation:** Reject any requested position with `|x| > 50` or `|z| > 50` (the
project's playable extent), correcting the client back to their last valid position.

**Why server-side:** World-bounds rules are part of the game's shared truth, same
category as the speed check in #3 — the server is the only participant with no
incentive to lie about where "in bounds" is.

---

## Summary Table

| # | Vulnerability | Mitigation | Server log action name |
|---|---|---|---|
| 1 | Weapon fire spam | Rate limit (8/s, burst 12) | `Fire` |
| 2 | Test-kill spam | Rate limit (2/s, burst 3) | `TestKill` |
| 3 | Speed-hack / teleport | Delta-vs-elapsed-time check, correct-back | `RequestMove` |
| 4 | NaN/Infinity injection | Finite-value check, correct-back | `RequestMove` |
| 5 | Out-of-bounds position | World bounds check, correct-back | `RequestMove` |

All rejections use the shared `SecurityLog.LogRejection(clientId, action, reason)`
helper, logged at `LogWarning` severity with a UTC timestamp, client ID, action name,
and specific reason — see attached screenshot of server console output showing rejected
requests during testing.