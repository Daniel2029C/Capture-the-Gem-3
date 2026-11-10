#pragma once
// Every message type and its payload layout. This is the file you will edit most.
//
// Conventions:
//  - One struct per message, with `void Write(ByteWriter&) const` and `bool Read(ByteReader&)`.
//  - Read returns r.Ok(); a malformed payload is dropped, never trusted.
//  - A type's channel (below) decides whether it is retransmitted. Add new types at the end of their group.
//  - Anything the client sends is UNTRUSTED input: validate ranges on the server, never apply it blindly.

#include "Serialization.h"

namespace ctg::net {

enum class PacketType : uint8_t {
    // Handshake / connection
    ConnectRequest = 1,
    Challenge = 2,
    ChallengeResponse = 3,
    Accept = 4,
    Reject = 5,
    Disconnect = 6,
    Ping = 7,
    Pong = 8,

    // Client -> server, in match
    Input = 20,
    ShopBuy = 21,
    // TODO(you): Surrender, ChatLine, PingMarker

    // Server -> client, in match
    Snapshot = 40,
    OwnerState = 41,
    EntitySpawn = 42,
    EntityDespawn = 43,
    HitEvent = 44,
    DeathEvent = 45,
    TileChange = 46,
    ShopResult = 47,
    ObjectiveEvent = 48,
    MatchEvent = 49,
    // TODO(you): Announcement, BuffEvent, SpellCast, particles (cosmetic channel)

    Count
};

enum class Channel : uint8_t {
    /// Fire and forget. Newer data supersedes lost data (snapshots, inputs, pings).
    Unreliable,
    /// Retransmitted until acknowledged, delivered in order (spawns, hits, tile edits, purchases).
    ReliableOrdered,
};

inline Channel ChannelOf(PacketType type) {
    switch (type) {
        case PacketType::ShopBuy:
        case PacketType::EntitySpawn:
        case PacketType::EntityDespawn:
        case PacketType::HitEvent:
        case PacketType::DeathEvent:
        case PacketType::TileChange:
        case PacketType::ShopResult:
        case PacketType::ObjectiveEvent:
        case PacketType::MatchEvent:
            return Channel::ReliableOrdered;
        default:
            return Channel::Unreliable; // handshake packets are retried by the client instead
    }
}

enum class RejectReason : uint8_t { BadVersion = 1, BadTicket = 2, ServerFull = 3, MatchOver = 4 };

// ---------------------------------------------------------------- handshake
//
// Three-step handshake so a spoofed source address cannot make the server do work or amplify traffic:
//   client: ConnectRequest(clientSalt)  ->  server: Challenge(clientSalt, serverSalt)
//   client: ChallengeResponse(clientSalt ^ serverSalt)  ->  server: Accept

struct ConnectRequest {
    uint16_t protocolVersion = 0;
    uint64_t ticket = 0;       // issued by the matchmaker; proves this player belongs in this match
    uint64_t clientSalt = 0;

    void Write(ByteWriter& w) const { w.U16(protocolVersion); w.U64(ticket); w.U64(clientSalt); }
    bool Read(ByteReader& r) { protocolVersion = r.U16(); ticket = r.U64(); clientSalt = r.U64(); return r.Ok(); }
};

struct Challenge {
    uint64_t clientSalt = 0;
    uint64_t serverSalt = 0;

    void Write(ByteWriter& w) const { w.U64(clientSalt); w.U64(serverSalt); }
    bool Read(ByteReader& r) { clientSalt = r.U64(); serverSalt = r.U64(); return r.Ok(); }
};

struct ChallengeResponse {
    uint64_t combinedSalt = 0; // clientSalt ^ serverSalt

    void Write(ByteWriter& w) const { w.U64(combinedSalt); }
    bool Read(ByteReader& r) { combinedSalt = r.U64(); return r.Ok(); }
};

struct Accept {
    uint8_t playerId = 0;
    uint8_t team = 0;
    uint8_t tickRate = kTickRateOnWire;
    uint32_t serverTick = 0;
    uint32_t mapHash = 0;      // the client verifies it loaded the same map file
    uint32_t matchSeed = 0;

    static constexpr uint8_t kTickRateOnWire = 60;

    void Write(ByteWriter& w) const { w.U8(playerId); w.U8(team); w.U8(tickRate); w.U32(serverTick); w.U32(mapHash); w.U32(matchSeed); }
    bool Read(ByteReader& r) {
        playerId = r.U8(); team = r.U8(); tickRate = r.U8();
        serverTick = r.U32(); mapHash = r.U32(); matchSeed = r.U32();
        return r.Ok();
    }
};

struct Reject {
    RejectReason reason = RejectReason::BadVersion;

    void Write(ByteWriter& w) const { w.U8(static_cast<uint8_t>(reason)); }
    bool Read(ByteReader& r) { reason = static_cast<RejectReason>(r.U8()); return r.Ok(); }
};

struct Disconnect {
    uint8_t reason = 0;

    void Write(ByteWriter& w) const { w.U8(reason); }
    bool Read(ByteReader& r) { reason = r.U8(); return r.Ok(); }
};

struct Ping {
    uint32_t clientTimeMs = 0;

    void Write(ByteWriter& w) const { w.U32(clientTimeMs); }
    bool Read(ByteReader& r) { clientTimeMs = r.U32(); return r.Ok(); }
};

struct Pong {
    uint32_t clientTimeMs = 0; // echoed back so the client can compute RTT
    uint32_t serverTick = 0;   // lets the client estimate the server's current tick

    void Write(ByteWriter& w) const { w.U32(clientTimeMs); w.U32(serverTick); }
    bool Read(ByteReader& r) { clientTimeMs = r.U32(); serverTick = r.U32(); return r.Ok(); }
};

// ---------------------------------------------------------------- client -> server

/// One tick of player controls. Mirrors CTG.Input.InputFrame in the Unity project.
struct InputCmd {
    enum Buttons : uint16_t {
        Left = 1 << 0, Right = 1 << 1, Jump = 1 << 2, Down = 1 << 3,
        UseItem = 1 << 4, UseAlt = 1 << 5, HookFire = 1 << 6,
    };

    uint32_t tick = 0;            // the tick this input is FOR (client's estimate of the server tick)
    uint16_t buttons = 0;         // Buttons bitfield
    uint8_t abilityPressed = 0;   // bit n = ability n+1 pressed this tick
    uint8_t abilityHeld = 0;
    uint8_t spellPressed = 0;
    int8_t hotbarKey = -1;        // 0-9 or -1
    int8_t hotbarScroll = 0;      // -1, 0, +1
    float cursorX = 0, cursorY = 0; // world units. TODO(you): quantize (e.g. 1/16 tile as int16) to save bytes

    void Write(ByteWriter& w) const {
        w.U32(tick); w.U16(buttons); w.U8(abilityPressed); w.U8(abilityHeld); w.U8(spellPressed);
        w.I8(hotbarKey); w.I8(hotbarScroll); w.F32(cursorX); w.F32(cursorY);
    }
    bool Read(ByteReader& r) {
        tick = r.U32(); buttons = r.U16(); abilityPressed = r.U8(); abilityHeld = r.U8(); spellPressed = r.U8();
        hotbarKey = r.I8(); hotbarScroll = r.I8(); cursorX = r.F32(); cursorY = r.F32();
        return r.Ok();
    }
};

/// The newest few inputs in one packet, so one lost datagram never loses a player action.
struct InputPacket {
    static constexpr int kMaxCommands = 5;
    uint8_t count = 0;
    InputCmd cmds[kMaxCommands]; // oldest first

    void Write(ByteWriter& w) const { w.U8(count); for (int i = 0; i < count; i++) cmds[i].Write(w); }
    bool Read(ByteReader& r) {
        count = r.U8();
        if (count == 0 || count > kMaxCommands) return false;
        for (int i = 0; i < count; i++) if (!cmds[i].Read(r)) return false;
        return r.Ok();
    }
};

struct ShopBuy {
    uint8_t itemId = 0;

    void Write(ByteWriter& w) const { w.U8(itemId); }
    bool Read(ByteReader& r) { itemId = r.U8(); return r.Ok(); }
};

// ---------------------------------------------------------------- server -> client
//
// TODO(you): define these. Suggested contents are in Server/README.md section "Server -> client messages".
//   Snapshot, OwnerState, EntitySpawn, EntityDespawn, HitEvent, DeathEvent, TileChange,
//   ShopResult, ObjectiveEvent, MatchEvent

} // namespace ctg::net
