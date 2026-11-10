#pragma once
// Thin non-blocking UDP socket (POSIX: macOS/Linux). Windows would need a Winsock variant of the same interface.

#include <cstddef>
#include <cstdint>

namespace ctg::net {

struct Address {
    uint32_t ip = 0;    // network byte order
    uint16_t port = 0;  // host byte order

    bool operator==(const Address& o) const { return ip == o.ip && port == o.port; }
    uint64_t Key() const { return (static_cast<uint64_t>(ip) << 16) | port; }
};

class UdpSocket {
public:
    ~UdpSocket();

    /// Binds to `port` (0 = let the OS choose). Returns false on failure.
    bool Open(uint16_t port);
    void Close();

    bool Send(const Address& to, const uint8_t* data, size_t size);
    /// Returns the datagram size, or 0 when nothing is waiting. Never blocks.
    size_t Receive(Address& from, uint8_t* buffer, size_t capacity);

private:
    int m_fd = -1;
};

} // namespace ctg::net
