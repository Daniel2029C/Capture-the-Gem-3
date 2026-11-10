#pragma once
// One match's authoritative server: owns the UDP socket, the connections, and the fixed 60 Hz loop.
//
// Every loop iteration:   receive datagrams -> (fixed ticks) consume inputs, step the simulation, send snapshots.
// Networking and simulation are kept apart: Receive* only fills queues; StepSimulation reads them.

#include "../net/Connection.h"

#include <deque>
#include <memory>
#include <unordered_map>

namespace ctg::server {

using namespace ctg::net;

/// What the simulation keeps per connected player.
struct PlayerSlot {
    std::unique_ptr<Connection> connection;
    uint32_t lastProcessedInputTick = 0;   // echoed to the client so it can discard acknowledged predictions
    std::deque<InputCmd> pendingInputs;    // received, not yet consumed (ordered by tick)
    // TODO(you): the server-side player state (position, velocity, health, economy...). Eventually this is where
    // the Unity CTG.Gameplay code (or its C++ port) is driven from.
};

class GameServer {
public:
    explicit GameServer(uint16_t port, uint32_t mapHash) : m_port(port), m_mapHash(mapHash) {}

    bool Start();
    /// Runs until `running` becomes false.
    void Run(const volatile bool& running);

private:
    // ---- network ----
    void PumpNetwork();
    void OnDatagram(const Address& from, const uint8_t* data, size_t size);
    void OnMessage(const Address& from, PacketType type, ByteReader& payload);

    void HandleConnectRequest(const Address& from, ByteReader& r);
    void HandleChallengeResponse(const Address& from, ByteReader& r);
    void HandleInput(Connection& conn, ByteReader& r);
    void HandlePing(Connection& conn, ByteReader& r);

    template <typename T>
    void Send(Connection& conn, PacketType type, const T& message);
    void SendRaw(Connection& conn, PacketType type, const std::vector<uint8_t>& payload);
    void SendRejectTo(const Address& to, RejectReason reason);

    void DropTimedOut();
    Connection* Find(const Address& a);

    // ---- simulation ----
    void StepSimulation();
    void SendSnapshots();

    double Now() const;

    uint16_t m_port;
    uint32_t m_mapHash;
    UdpSocket m_socket;
    uint32_t m_tick = 0;
    double m_startTime = 0.0;

    std::unordered_map<uint64_t, std::unique_ptr<Connection>> m_pending;  // keyed by Address::Key, mid-handshake
    std::unordered_map<uint8_t, PlayerSlot> m_players;                    // keyed by playerId
    std::unordered_map<uint64_t, uint8_t> m_playerByAddress;
    uint8_t m_nextPlayerId = 0;
};

} // namespace ctg::server
