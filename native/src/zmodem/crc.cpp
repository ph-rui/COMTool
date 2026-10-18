#include "zmodem/crc.h"

#include <array>

namespace zmodem {
namespace {

std::array<uint16_t, 256> makeCrc16Table()
{
    std::array<uint16_t, 256> table{};
    for (unsigned value = 0; value < 256; ++value) {
        uint16_t crc = static_cast<uint16_t>(value << 8);
        for (int bit = 0; bit < 8; ++bit) {
            if (crc & 0x8000) {
                crc = static_cast<uint16_t>((crc << 1) ^ 0x1021);
            } else {
                crc = static_cast<uint16_t>(crc << 1);
            }
        }
        table[value] = crc;
    }
    return table;
}

std::array<uint32_t, 256> makeCrc32Table()
{
    std::array<uint32_t, 256> table{};
    for (unsigned value = 0; value < 256; ++value) {
        uint32_t crc = value;
        for (int bit = 0; bit < 8; ++bit) {
            if (crc & 1) {
                crc = (crc >> 1) ^ 0xEDB88320u;
            } else {
                crc >>= 1;
            }
        }
        table[value] = crc;
    }
    return table;
}

const std::array<uint16_t, 256> CRC16_TABLE = makeCrc16Table();
const std::array<uint32_t, 256> CRC32_TABLE = makeCrc32Table();

} // namespace

uint16_t crc16(const uint8_t *data, size_t len)
{
    uint16_t crc = 0;
    for (size_t i = 0; i < len; ++i) {
        crc = static_cast<uint16_t>((crc << 8) ^ CRC16_TABLE[((crc >> 8) ^ data[i]) & 0xFF]);
    }
    return crc;
}

uint32_t crc32(const uint8_t *data, size_t len)
{
    uint32_t crc = 0xFFFFFFFFu;
    for (size_t i = 0; i < len; ++i) {
        crc = (crc >> 8) ^ CRC32_TABLE[(crc ^ data[i]) & 0xFF];
    }
    return crc ^ 0xFFFFFFFFu;
}

std::vector<uint8_t> packCrc16(uint16_t value)
{
    return {static_cast<uint8_t>(value >> 8), static_cast<uint8_t>(value & 0xFF)};
}

uint16_t unpackCrc16(const uint8_t *data)
{
    return static_cast<uint16_t>((data[0] << 8) | data[1]);
}

std::vector<uint8_t> packCrc32(uint32_t value)
{
    return {static_cast<uint8_t>(value & 0xFF), static_cast<uint8_t>((value >> 8) & 0xFF),
            static_cast<uint8_t>((value >> 16) & 0xFF),
            static_cast<uint8_t>((value >> 24) & 0xFF)};
}

uint32_t unpackCrc32(const uint8_t *data)
{
    return static_cast<uint32_t>(data[0]) | (static_cast<uint32_t>(data[1]) << 8) |
           (static_cast<uint32_t>(data[2]) << 16) | (static_cast<uint32_t>(data[3]) << 24);
}

} // namespace zmodem
