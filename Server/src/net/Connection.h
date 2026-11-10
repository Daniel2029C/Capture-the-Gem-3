#pragma once
// Per-peer transport state: handshake progress, datagram sequencing, acknowledgements, reliable delivery.
// One Connection exists per remote endpoint on the server (and one on the client).

#include "UdpSocket.h"
#include "../protocol/Datagram.h"
#include "../protocol/Packets.h"

#include <vector>

namespace ctg::net {

enum class ConnectionState : uint8_t {
    Challenging,  // got ConnectRequest, sent Challenge, waiting for ChallengeResponse
    Connected,    // handshake done; gameplay packets accepted
};

class Connection {
public:
    Address peer;
    ConnectionState state = ConnectionState::Challenging;
    uint8_t playerId = 0;
    uint64_t clientSalt = 0;
    uint64_t serverSalt = 0;
    double lastReceiveTime = 0.0;  // seconds; used for timeouts
    double rttSeconds = 0.0;       // smoothed round trip time, filled by OnPong / ack timing

    // ---- receiving side: what we tell the peer we got ----

    /// Call for every valid datagram. Records `seq` into (remoteSeq, remoteAckBits) and processes the peer's acks.
    void OnDatagramReceived(const DatagramHeader& header, double now) {
        lastReceiveTime = now;
        TrackIncoming(header.sequence);
        ProcessAcks(header.ack, header.ackBits);
    }

    // ---- sending side ----

    /// Fills a header for the next outgoing datagram (new sequence number + our current ack state).
    DatagramHeader NextHeader() {
        DatagramHeader h;
        h.sequence = m_localSeq++;
        h.ack = m_remoteSeq;
        h.ackBits = m_remoteAckBits;
        return h;
    }

    /// Reliable-message sequence for the next ReliableOrdered message.
    uint16_t NextReliableSeq() { return m_nextReliableSeq++; }

private:
    void TrackIncoming(uint16_t seq) {
        if (SeqGreater(seq, m_remoteSeq)) {
            // The old newest becomes bit (shift-1); everything else slides up.
            int shift = SeqDiff(seq, m_remoteSeq);
            m_remoteAckBits = (shift >= 32 ? 0u : (m_remoteAckBits << shift)) | (shift <= 32 ? (1u << (shift - 1)) : 0u);
            m_remoteSeq = seq;
        } else {
            int back = SeqDiff(m_remoteSeq, seq);
            if (back >= 1 && back <= 32) m_remoteAckBits |= (1u << (back - 1));
            // back == 0 is a duplicate datagram: nothing to record.
        }
    }

    /// The peer says: "I received your datagram `ack`, and the 32 before it as flagged in `ackBits`".
    void ProcessAcks(uint16_t ack, uint32_t ackBits) {
        OnDatagramAcked(ack);
        for (int i = 0; i < 32; i++)
            if (ackBits & (1u << i)) OnDatagramAcked(static_cast<uint16_t>(ack - 1 - i));
    }

    /// TODO(you): reliable delivery and RTT.
    ///  - When a datagram is sent, remember (datagramSeq -> send time, list of reliable messages it carried).
    ///  - Here, when datagramSeq is acked the FIRST time: update rttSeconds, drop its reliable messages from the resend queue.
    ///  - Elsewhere (a per-tick Update): any reliable message unacked for > ~1.5 x RTT gets re-sent in a new datagram.
    ///  - Receiver side: deliver reliable messages in reliableSeq order, buffering early arrivals, ignoring duplicates.
    void OnDatagramAcked(uint16_t /*datagramSeq*/) {}

    uint16_t m_localSeq = 0;
    uint16_t m_remoteSeq = 0;
    uint32_t m_remoteAckBits = 0;
    uint16_t m_nextReliableSeq = 0;
};

} // namespace ctg::net
