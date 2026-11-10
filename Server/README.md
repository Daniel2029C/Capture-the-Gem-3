# CTG2 backend: architecture and packet layer

This document explains the skeleton in `Server/`. Everything marked `TODO(you)` in the source is meant to be written by you.

## 1. The big picture

```
 Unity client ──TCP/HTTPS──▶ Gateway ─▶ Matchmaker ─▶ Orchestrator ──spawns──▶ Game server (one process per match)
      │                                                                               ▲
      └──────────────────────────── UDP (this skeleton) ──────────────────────────────┘
```

| Piece | Protocol | State it owns | Status |
|---|---|---|---|
| Gateway / auth | TCP (HTTPS or gRPC) | accounts, sessions | not started |
| Matchmaker | TCP | queues, parties | not started |
| Orchestrator | TCP | list of running match servers | not started |
| **Game server** | **UDP** | one match: the authoritative simulation | **skeleton here** |
| Persistence | TCP | results, stats | not started |

Reliability on TCP services is free, so they need none of the packet layer below. The game server is where
latency matters, hence custom UDP. Build the game server first: it is the hard part and everything else is
ordinary request/response work.

**Why one process per match:** a crash or a bad match only kills ten players, matches scale by adding machines,
and the sim never needs locking. A match is single-threaded: one loop, 60 ticks per second.

## 2. Layers of the game server

```
 main.cpp            starts GameServer on a port
 server/GameServer   the loop, the handshake, input queues, and the hooks where the simulation plugs in
 net/Connection      per-peer transport state: sequence numbers, acks, (reliable delivery: yours to write)
 net/UdpSocket       non-blocking sendto/recvfrom
 protocol/Datagram   envelope header + sequence maths
 protocol/Packets    every message type and its byte layout
 protocol/Serialization  ByteWriter / ByteReader
```

Dependencies only point downward. `protocol/` has no OS or socket code, so the same headers can be reused by a
C++ client, a test tool, or ported field-for-field to C# for Unity.

## 3. The wire format

Every UDP datagram:

```
 magic u16 | seq u16 | ack u16 | ackBits u32 | message* 
```

- `magic` filters stray traffic. `kProtocolVersion` is checked in the handshake.
- `seq` numbers this datagram. `ack` + `ackBits` tell the peer which of ITS datagrams we received
  (`ack` = newest, bit n = `ack-1-n`). Acks piggyback on all traffic, so there are no separate ack packets
  while the game is running. This is the standard scheme from Glenn Fiedler's "Reliable UDP" writing.
- Sequence numbers are 16-bit and wrap, so compare with `SeqGreater`, never `>`.
- Size is capped at 1200 bytes so the OS never fragments (a fragmented datagram is lost if any piece is lost).

Each message inside:

```
 type u8 | [reliableSeq u16 — only for ReliableOrdered types] | length u16 | payload
```

A datagram can carry several messages (batching is a TODO in `SendRaw`). `length` lets the receiver skip
unknown types and keeps a parse bug in one message from corrupting the next.

## 4. Channels (the key design decision)

`ChannelOf(type)` in `Packets.h` sets how each type is delivered.

- **Unreliable:** if it's lost, don't resend. Used for anything where the next one replaces it: inputs (the last 5
  ride in every packet), snapshots, pings. Resending a stale snapshot would be worse than skipping it.
- **ReliableOrdered:** resend until acked, deliver in order. Used for things that must happen exactly once and
  in sequence: spawns, hits, deaths, tile edits, purchases, objective events.

Do not make everything reliable. A reliable channel stalls behind a lost packet (head-of-line blocking), which
adds lag to everything queued behind it. Keep gameplay state on unreliable snapshots; use reliable only for events.

## 5. Handshake

```
 C: ConnectRequest(version, ticket, clientSalt)
 S: Challenge(clientSalt, serverSalt)
 C: ChallengeResponse(clientSalt ^ serverSalt)
 S: Accept(playerId, team, tickRate, serverTick, mapHash, seed)      (or Reject(reason))
```

The challenge exists because UDP source addresses can be forged. Without it, one forged packet makes the server
allocate state and answer a victim (amplification). The server keeps no real state until the response proves the
client receives at the address it claims. Handshake packets are unreliable on purpose: the client simply retries.

`ticket` is how the matchmaker says "this player belongs in this match". Validation is a TODO.

## 6. The simulation loop and where networking meets gameplay

```
 while running:
     PumpNetwork()                  read every waiting datagram, fill per-player input queues
     while a tick is due:           fixed 1/60 s, catch up if late (capped)
         StepSimulation()           consume one InputCmd per player, step the world
         SendSnapshots()
```

Rules this enforces:
1. The network never touches game state directly. It only appends to `pendingInputs`.
2. The sim never reads the socket. This keeps the sim deterministic given its inputs, which is what makes
   replays, testing and later client prediction possible.
3. The client sends **inputs, never positions**. The server decides the outcome. That one rule is most of
   your anti-cheat.

## 7. What the client must do (Unity side, later)

- **Prediction:** run the local player's motor immediately on input, remember each input with its tick.
- **Reconciliation:** each snapshot says "I processed your inputs through tick N and you were here". Roll back
  to that state, re-apply inputs after N. If the result matches, nothing visible; if not, a correction.
- **Interpolation:** other players are drawn ~2 snapshots in the past, blended between snapshots, so they move
  smoothly despite jitter.
- **Time sync:** `Pong.serverTick` plus RTT gives the client an estimate of the server tick, so `InputCmd.tick`
  lands slightly ahead of arrival and the server's input queue is never empty.

## 8. Server -> client messages (still to define)

Suggested contents, in priority order:

- **Snapshot** (unreliable, every tick or every 2nd): server tick, then per visible entity `id, position, velocity,
  facing, hp, flags (grounded, hooked, staggered), held item`. Delta against the last snapshot the client acked.
- **OwnerState** (unreliable, only to the owner): `lastProcessedInputTick`, shards, exp, level, ability charges and
  cooldowns, spell cooldowns, sprint meter, Apex buff ticks left. This is *private* info; never broadcast it.
- **EntitySpawn / Despawn** (reliable): projectile, Drakon, boss, gem. Projectiles are cheap to send as
  `owner, type, origin, velocity, spawnTick` and let both sides simulate them.
- **HitEvent / DeathEvent** (reliable): target, source, damage, knockback; death carries killer + assisters.
- **TileChange** (reliable): `x, y, newTile`, batched. Plus a "full tile log since map load" path for late joiners.
- **ShopResult** (reliable): ok / reason + new balances.
- **ObjectiveEvent** (reliable): altar channel start/interrupt/complete, barrier hp + break + first-break winner, gem
  pick/drop/return/capture, offering counts (team-visible), Drakon telegraph (position + countdown), boss attack telegraph.
- **MatchEvent** (reliable): phase changes, Apex Shift, buff applied/ended, overtime, end of match.

## 9. Order of work I'd suggest

1. Read through `Connection.h`, `GameServer.cpp`. Write `OnDatagramAcked` (RTT + reliable resend queue) and the
   receive-side in-order delivery. This is the core of the transport and a good first piece to own.
2. Write the C# mirror of `Serialization` + `Datagram` + handshake packets in Unity, and a client that connects to
   this server and shows its player id. First end-to-end proof.
3. Add Snapshot with positions only; run a bare player motor on the server; interpolate remote players in Unity.
4. Add prediction/reconciliation for the local player.
5. Then the event packets, one gameplay system at a time (combat -> tiles -> economy -> objectives).

## 10. Building and testing

```
 clang++ -std=c++20 -Wall -Wextra -o build/ctg_gameserver src/server/main.cpp src/server/GameServer.cpp src/net/UdpSocket.cpp
 ./build/ctg_gameserver 7777
```

(`CMakeLists.txt` is included for when you install CMake.) The handshake, ping/pong and version reject were
verified with a throwaway Python UDP client; everything past that is untested because it isn't written yet.
