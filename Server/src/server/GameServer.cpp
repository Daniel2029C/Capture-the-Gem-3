#include "GameServer.h"

#include <chrono>
#include <cstdio>
#include <random>
#include <thread>

namespace ctg::server {

namespace {
constexpr double kTimeoutSeconds = 10.0;
constexpr double kTickSeconds = 1.0 / kTickRate;
constexpr size_t kMaxPlayers = 10;

uint64_t RandomSalt() {
    static std::mt19937_64 rng{std::random_device{}()};
    return rng();
}
} // namespace

double GameServer::Now() const {
    using clock = std::chrono::steady_clock;
    return std::chrono::duration<double>(clock::now().time_since_epoch()).count() - m_startTime;
}

bool GameServer::Start() {
    if (!m_socket.Open(m_port)) { std::fprintf(stderr, "could not bind UDP port %u\n", m_port); return false; }
    m_startTime = 0.0;
    m_startTime = Now();
    std::printf("listening on UDP %u\n", m_port);
    return true;
}

void GameServer::Run(const volatile bool& running) {
    double nextTick = Now();
    while (running) {
        PumpNetwork();

        // Fixed timestep: run as many ticks as wall time says are due (capped so a stall cannot spiral).
        int ran = 0;
        while (Now() >= nextTick && ran < 5) {
            StepSimulation();
            SendSnapshots();
            m_tick++;
            nextTick += kTickSeconds;
            ran++;
        }
        if (ran == 5) nextTick = Now();

        DropTimedOut();
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
}

// ============================================================ receiving

void GameServer::PumpNetwork() {
    uint8_t buffer[kMaxDatagramBytes + 64];
    Address from;
    while (size_t n = m_socket.Receive(from, buffer, sizeof(buffer))) OnDatagram(from, buffer, n);
}

Connection* GameServer::Find(const Address& a) {
    if (auto it = m_pending.find(a.Key()); it != m_pending.end()) return it->second.get();
    if (auto it = m_playerByAddress.find(a.Key()); it != m_playerByAddress.end()) return m_players[it->second].connection.get();
    return nullptr;
}

void GameServer::OnDatagram(const Address& from, const uint8_t* data, size_t size) {
    ByteReader r(data, size);
    DatagramHeader header;
    if (!header.Read(r)) return;  // not ours, or truncated: ignore silently

    if (Connection* conn = Find(from)) conn->OnDatagramReceived(header, Now());

    while (r.Remaining() > 0) {
        auto type = static_cast<PacketType>(r.U8());
        if (type >= PacketType::Count) return;
        if (ChannelOf(type) == Channel::ReliableOrdered) {
            uint16_t reliableSeq = r.U16();
            (void)reliableSeq;  // TODO(you): in-order delivery + duplicate suppression (see Connection::OnDatagramAcked)
        }
        uint16_t length = r.U16();
        ByteReader payload = r.Slice(length);
        if (!r.Ok()) return;
        OnMessage(from, type, payload);
    }
}

void GameServer::OnMessage(const Address& from, PacketType type, ByteReader& payload) {
    // Before the handshake finishes, only handshake packets are meaningful.
    if (type == PacketType::ConnectRequest) { HandleConnectRequest(from, payload); return; }
    if (type == PacketType::ChallengeResponse) { HandleChallengeResponse(from, payload); return; }

    Connection* conn = Find(from);
    if (!conn || conn->state != ConnectionState::Connected) return;

    switch (type) {
        case PacketType::Input: HandleInput(*conn, payload); break;
        case PacketType::Ping: HandlePing(*conn, payload); break;
        case PacketType::Disconnect: {
            m_players.erase(conn->playerId);  // note: erases the Connection too; do not touch `conn` afterwards
            m_playerByAddress.erase(from.Key());
            break;
        }
        // TODO(you): ShopBuy and the other client commands.
        default: break;
    }
}

void GameServer::HandleConnectRequest(const Address& from, ByteReader& r) {
    ConnectRequest req;
    if (!req.Read(r)) return;

    if (req.protocolVersion != kProtocolVersion) { SendRejectTo(from, RejectReason::BadVersion); return; }
    if (m_players.size() >= kMaxPlayers) { SendRejectTo(from, RejectReason::ServerFull); return; }
    // TODO(you): validate req.ticket against what the matchmaker issued for this match (shared secret / signed token).
    // TODO(you): rate-limit by source address; this path is reachable by anyone on the internet.

    auto& slot = m_pending[from.Key()];
    if (!slot) {
        slot = std::make_unique<Connection>();
        slot->peer = from;
        slot->serverSalt = RandomSalt();
    }
    slot->clientSalt = req.clientSalt;  // a client retrying with a new salt just replaces it
    slot->lastReceiveTime = Now();

    Challenge challenge{slot->clientSalt, slot->serverSalt};
    Send(*slot, PacketType::Challenge, challenge);
}

void GameServer::HandleChallengeResponse(const Address& from, ByteReader& r) {
    ChallengeResponse resp;
    if (!resp.Read(r)) return;

    auto it = m_pending.find(from.Key());
    if (it == m_pending.end()) return;
    Connection& pending = *it->second;
    if (resp.combinedSalt != (pending.clientSalt ^ pending.serverSalt)) return;  // wrong: spoofed or stale

    uint8_t id = m_nextPlayerId++;
    pending.state = ConnectionState::Connected;
    pending.playerId = id;

    PlayerSlot& slot = m_players[id];
    slot.connection = std::move(it->second);
    m_pending.erase(it);
    m_playerByAddress[from.Key()] = id;

    Accept accept;
    accept.playerId = id;
    accept.team = (id % 2) ? 2 : 1;  // TODO(you): real team assignment from the matchmaker's roster (Team enum: Red 1, Blue 2, Monster 3)
    accept.serverTick = m_tick;
    accept.mapHash = m_mapHash;
    accept.matchSeed = 0;            // TODO(you): per-match seed
    Send(*slot.connection, PacketType::Accept, accept);
    std::printf("player %u connected\n", id);
}

void GameServer::HandleInput(Connection& conn, ByteReader& r) {
    InputPacket packet;
    if (!packet.Read(r)) return;

    PlayerSlot& slot = m_players[conn.playerId];
    for (int i = 0; i < packet.count; i++) {
        const InputCmd& cmd = packet.cmds[i];
        // Redundant resends: skip anything we already have or already consumed.
        if (cmd.tick <= slot.lastProcessedInputTick) continue;
        if (!slot.pendingInputs.empty() && cmd.tick <= slot.pendingInputs.back().tick) continue;
        // TODO(you): sanity-check cmd.tick (reject inputs far in the future / flood), clamp cursor, cap queue length.
        slot.pendingInputs.push_back(cmd);
    }
}

void GameServer::HandlePing(Connection& conn, ByteReader& r) {
    Ping ping;
    if (!ping.Read(r)) return;
    Send(conn, PacketType::Pong, Pong{ping.clientTimeMs, m_tick});
}

void GameServer::DropTimedOut() {
    double now = Now();
    for (auto it = m_pending.begin(); it != m_pending.end();)
        it = (now - it->second->lastReceiveTime > kTimeoutSeconds) ? m_pending.erase(it) : std::next(it);

    for (auto it = m_players.begin(); it != m_players.end();) {
        if (now - it->second.connection->lastReceiveTime > kTimeoutSeconds) {
            std::printf("player %u timed out\n", it->first);
            m_playerByAddress.erase(it->second.connection->peer.Key());
            it = m_players.erase(it);  // TODO(you): keep the slot for a reconnect window instead of freeing it
        } else {
            ++it;
        }
    }
}

// ============================================================ sending

template <typename T>
void GameServer::Send(Connection& conn, PacketType type, const T& message) {
    std::vector<uint8_t> payload;
    ByteWriter w(payload);
    message.Write(w);
    SendRaw(conn, type, payload);
}

void GameServer::SendRaw(Connection& conn, PacketType type, const std::vector<uint8_t>& payload) {
    // TODO(you): batch several messages into one datagram, and keep a copy of reliable messages for resending.
    std::vector<uint8_t> datagram;
    ByteWriter w(datagram);
    conn.NextHeader().Write(w);
    w.U8(static_cast<uint8_t>(type));
    if (ChannelOf(type) == Channel::ReliableOrdered) w.U16(conn.NextReliableSeq());
    w.U16(static_cast<uint16_t>(payload.size()));
    datagram.insert(datagram.end(), payload.begin(), payload.end());
    m_socket.Send(conn.peer, datagram.data(), datagram.size());
}

void GameServer::SendRejectTo(const Address& to, RejectReason reason) {
    // No Connection exists for this peer, so build a one-off datagram with a blank header.
    std::vector<uint8_t> datagram;
    ByteWriter w(datagram);
    DatagramHeader{}.Write(w);
    w.U8(static_cast<uint8_t>(PacketType::Reject));
    w.U16(1);
    Reject{reason}.Write(w);
    m_socket.Send(to, datagram.data(), datagram.size());
}

// ============================================================ simulation hooks

void GameServer::StepSimulation() {
    for (auto& [id, slot] : m_players) {
        // Consume at most one input per tick. If the queue is empty, repeat the last input (or idle).
        // TODO(you): input buffering policy (how many ticks of jitter buffer), what to do when inputs arrive late/early.
        if (!slot.pendingInputs.empty()) {
            InputCmd cmd = slot.pendingInputs.front();
            slot.pendingInputs.pop_front();
            slot.lastProcessedInputTick = cmd.tick;
            // TODO(you): apply `cmd` to this player's motor/items/abilities.
        }
    }
    // TODO(you): step projectiles, mobs, boss, altars, barrier, match clock.
}

void GameServer::SendSnapshots() {
    // TODO(you): build a Snapshot per client (relevant entities only, delta against the client's last acked snapshot)
    // plus an OwnerState including lastProcessedInputTick. Send every tick, or every 2nd tick to halve bandwidth.
}

} // namespace ctg::server
