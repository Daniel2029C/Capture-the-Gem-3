#include "UdpSocket.h"

#include <arpa/inet.h>
#include <fcntl.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>

namespace ctg::net {

UdpSocket::~UdpSocket() { Close(); }

bool UdpSocket::Open(uint16_t port) {
    m_fd = ::socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (m_fd < 0) return false;

    int flags = ::fcntl(m_fd, F_GETFL, 0);
    if (flags < 0 || ::fcntl(m_fd, F_SETFL, flags | O_NONBLOCK) < 0) { Close(); return false; }

    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_ANY);
    addr.sin_port = htons(port);
    if (::bind(m_fd, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) < 0) { Close(); return false; }
    return true;
}

void UdpSocket::Close() {
    if (m_fd >= 0) { ::close(m_fd); m_fd = -1; }
}

bool UdpSocket::Send(const Address& to, const uint8_t* data, size_t size) {
    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = to.ip;
    addr.sin_port = htons(to.port);
    ssize_t sent = ::sendto(m_fd, data, size, 0, reinterpret_cast<sockaddr*>(&addr), sizeof(addr));
    return sent == static_cast<ssize_t>(size);
}

size_t UdpSocket::Receive(Address& from, uint8_t* buffer, size_t capacity) {
    sockaddr_in addr{};
    socklen_t len = sizeof(addr);
    ssize_t got = ::recvfrom(m_fd, buffer, capacity, 0, reinterpret_cast<sockaddr*>(&addr), &len);
    if (got <= 0) return 0;
    from.ip = addr.sin_addr.s_addr;
    from.port = ntohs(addr.sin_port);
    return static_cast<size_t>(got);
}

} // namespace ctg::net
