#pragma once
// Byte-level reading and writing. Everything on the wire is little-endian.
// The reader never throws and never reads out of bounds: on underflow it sets a failed flag and returns zeros,
// so packet parsers can read everything first and check Ok() once at the end.

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <vector>

namespace ctg::net {

class ByteWriter {
public:
    explicit ByteWriter(std::vector<uint8_t>& out) : m_out(out) {}

    void U8(uint8_t v) { m_out.push_back(v); }
    void U16(uint16_t v) { U8(static_cast<uint8_t>(v)); U8(static_cast<uint8_t>(v >> 8)); }
    void U32(uint32_t v) { U16(static_cast<uint16_t>(v)); U16(static_cast<uint16_t>(v >> 16)); }
    void U64(uint64_t v) { U32(static_cast<uint32_t>(v)); U32(static_cast<uint32_t>(v >> 32)); }
    void I8(int8_t v) { U8(static_cast<uint8_t>(v)); }
    void I16(int16_t v) { U16(static_cast<uint16_t>(v)); }
    void I32(int32_t v) { U32(static_cast<uint32_t>(v)); }
    void Bool(bool v) { U8(v ? 1 : 0); }
    void F32(float v) { uint32_t bits; std::memcpy(&bits, &v, 4); U32(bits); }

    size_t Size() const { return m_out.size(); }
    /// Overwrites a u16 written earlier (used to back-patch a length prefix).
    void PatchU16(size_t at, uint16_t v) { m_out[at] = static_cast<uint8_t>(v); m_out[at + 1] = static_cast<uint8_t>(v >> 8); }

private:
    std::vector<uint8_t>& m_out;
};

class ByteReader {
public:
    ByteReader(const uint8_t* data, size_t size) : m_data(data), m_size(size) {}

    bool Ok() const { return !m_failed; }
    size_t Remaining() const { return m_size - m_pos; }

    uint8_t U8() { return Need(1) ? m_data[m_pos++] : 0; }
    uint16_t U16() { uint16_t lo = U8(); uint16_t hi = U8(); return static_cast<uint16_t>(lo | (hi << 8)); }
    uint32_t U32() { uint32_t lo = U16(); uint32_t hi = U16(); return lo | (hi << 16); }
    uint64_t U64() { uint64_t lo = U32(); uint64_t hi = U32(); return lo | (hi << 32); }
    int8_t I8() { return static_cast<int8_t>(U8()); }
    int16_t I16() { return static_cast<int16_t>(U16()); }
    int32_t I32() { return static_cast<int32_t>(U32()); }
    bool Bool() { return U8() != 0; }
    float F32() { uint32_t bits = U32(); float v; std::memcpy(&v, &bits, 4); return v; }

    /// A sub-reader over the next `n` bytes; advances this reader past them.
    ByteReader Slice(size_t n) {
        if (!Need(n)) return ByteReader(nullptr, 0);
        ByteReader sub(m_data + m_pos, n);
        m_pos += n;
        return sub;
    }

private:
    bool Need(size_t n) {
        if (m_failed || m_size - m_pos < n) { m_failed = true; return false; }
        return true;
    }

    const uint8_t* m_data;
    size_t m_size;
    size_t m_pos = 0;
    bool m_failed = false;
};

} // namespace ctg::net
