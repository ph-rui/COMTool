#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace zmodem {

using Bytes = std::vector<uint8_t>;

uint16_t crc16(const uint8_t *data, size_t len);
uint32_t crc32(const uint8_t *data, size_t len);
inline uint16_t crc16(const Bytes &data)
{
    return crc16(data.data(), data.size());
}
inline uint32_t crc32(const Bytes &data)
{
    return crc32(data.data(), data.size());
}

std::vector<uint8_t> packCrc16(uint16_t value);
uint16_t unpackCrc16(const uint8_t *data);
std::vector<uint8_t> packCrc32(uint32_t value);
uint32_t unpackCrc32(const uint8_t *data);

} // namespace zmodem
