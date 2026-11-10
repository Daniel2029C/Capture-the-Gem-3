#pragma once
// The outer envelope of every UDP packet, plus the sequence-number maths it relies on.
//
//   Datagram = DatagramHeader | Message | Message | ...
//   Message  = type(u8) | [reliableSeq(u16), only on reliable types] | length(u16) | payload
//
// The header carries acknowledgements for the OTHER side's datagrams, so ack traffic rides on normal traffic.

#include "Serialization.h"

namespace ctg::net {

constexpr uint16_t kMagic = 0xC762;          // first two bytes of every datagram; cheap filter for stray traffic
constexpr uint16_t kProtocolVersion = 1;     // bump on any wire change; the handshake rejects mismatches
constexpr size_t kMaxDatagramBytes = 1200;   // stays under typical path MTU so the OS never fragments
constexpr int kTickRate = 60;

/// True when `a` is newer than `b`, treating the 16-bit counter as circular (65535 -> 0 is "newer").
inline bool SeqGreater(uint16_t a, uint16_t b) {
    return ((a > b) && (a - b <= 32768)) || ((a < b) && (b - a > 32768));
}

/// Signed distance a - b on the circular counter.
inline int SeqDiff(uint16_t a, uint16_t b) {
    return static_cast<int16_t>(static_cast<uint16_t>(a - b));
}

struct DatagramHeader {
    uint16_t sequence = 0;   // this datagram's number (increments per datagram sent)
    uint16_t ack = 0;        // newest datagram of the peer's that we have received
    uint32_t ackBits = 0;    // bit n set => peer datagram (ack - 1 - n) was also received

    static constexpr size_t kSize = 2 + 2 + 2 + 4;

    void Write(ByteWriter& w) const { w.U16(kMagic); w.U16(sequence); w.U16(ack); w.U32(ackBits); }

    bool Read(ByteReader& r) {
        if (r.U16() != kMagic) return false;
        sequence = r.U16();
        ack = r.U16();
        ackBits = r.U32();
        return r.Ok();
    }
};

} // namespace ctg::net
