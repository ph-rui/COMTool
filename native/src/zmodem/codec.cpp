#include "zmodem/codec.h"
#include "zmodem/errors.h"

#include <algorithm>
#include <cstring>

namespace zmodem {
namespace {

const uint8_t HEX_PREFIX[] = {'*', '*', ZDLE, ZHEX};
const uint8_t BIN16_PREFIX[] = {'*', ZDLE, ZBIN};
const uint8_t BIN32_PREFIX[] = {'*', ZDLE, ZBIN32};

bool isEscapeByte(uint8_t value, bool escapeCtrl, bool escape8bit, uint8_t previous)
{
    if (value == ZDLE || value == XON || value == XOFF || value == XON_HIGH ||
        value == XOFF_HIGH || value == 0x10 || value == 0x90) {
        return true;
    }
    if (escapeCtrl && (value & 0x7F) < 0x20) {
        return true;
    }
    if (escape8bit && (value & 0x80)) {
        return true;
    }
    if ((value == 0x0D || value == 0x8D) && (previous & 0x7F) == 0x40) {
        return true;
    }
    return false;
}

uint8_t decodeZdleByte(uint8_t value)
{
    if (value == 0x6C) {
        return 0x7F;
    }
    if (value == 0x6D) {
        return 0xFF;
    }
    return static_cast<uint8_t>(value ^ 0x40);
}

bool isPrefixOf(const Bytes &data, size_t offset, const uint8_t *prefix, size_t length)
{
    const size_t available = std::min(length, data.size() - offset);
    return std::memcmp(prefix, data.data() + offset, available) == 0;
}

int hexDigit(uint8_t value)
{
    if (value >= '0' && value <= '9') {
        return value - '0';
    }
    if (value >= 'a' && value <= 'f') {
        return value - 'a' + 10;
    }
    if (value >= 'A' && value <= 'F') {
        return value - 'A' + 10;
    }
    return -1;
}

std::optional<std::pair<Bytes, size_t>> decodeZdleN(const Bytes &data, size_t start, size_t count,
                                                    bool skipXonOff)
{
    Bytes out;
    out.reserve(count);
    size_t index = start;
    while (out.size() < count) {
        if (index >= data.size()) {
            return std::nullopt;
        }
        const uint8_t value = data[index++];
        if (value == ZDLE) {
            if (index >= data.size()) {
                return std::nullopt;
            }
            out.push_back(decodeZdleByte(data[index++]));
        } else if (skipXonOff && (value == XON || value == XOFF || value == XON_HIGH ||
                                  value == XOFF_HIGH)) {
            continue;
        } else {
            out.push_back(value);
        }
    }
    return std::make_pair(std::move(out), index - start);
}

HeaderParseResult parseHexHeader(const Bytes &data, size_t offset)
{
    HeaderParseResult result;
    const size_t start = offset + sizeof(HEX_PREFIX);
    const size_t hexEnd = start + 14;
    if (data.size() < hexEnd) {
        result.status = ParseStatus::NeedMore;
        return result;
    }

    uint8_t rawBody[7];
    for (size_t i = 0; i < 7; ++i) {
        const int high = hexDigit(data[start + i * 2]);
        const int low = hexDigit(data[start + i * 2 + 1]);
        if (high < 0 || low < 0) {
            result.status = ParseStatus::Invalid;
            return result;
        }
        rawBody[i] = static_cast<uint8_t>((high << 4) | low);
    }

    size_t index = hexEnd;
    if (index >= data.size()) {
        result.status = ParseStatus::NeedMore;
        return result;
    }
    if (data[index] == 0x0D) {
        if (index + 1 >= data.size()) {
            result.status = ParseStatus::NeedMore;
            return result;
        }
        if (data[index + 1] != 0x0A && data[index + 1] != 0x8A) {
            result.status = ParseStatus::Invalid;
            return result;
        }
        index += 2;
    } else if (data[index] == 0x0A) {
        index += 1;
    } else {
        result.status = ParseStatus::Invalid;
        return result;
    }
    if (index < data.size() && data[index] == XON) {
        index += 1;
    }

    const uint16_t expected = crc16(rawBody, 5);
    const uint16_t got = static_cast<uint16_t>((rawBody[5] << 8) | rawBody[6]);
    if (expected != got) {
        result.status = ParseStatus::Invalid;
        return result;
    }

    result.status = ParseStatus::Ok;
    result.header.frameType = rawBody[0];
    std::copy(rawBody + 1, rawBody + 5, result.header.data.begin());
    result.header.crcLen = 16;
    result.header.raw.assign(data.begin() + offset, data.begin() + index);
    result.consumed = index - offset;
    return result;
}

HeaderParseResult parseBinaryHeader(const Bytes &data, size_t offset, int crcLen)
{
    HeaderParseResult result;
    const size_t prefixLen = sizeof(BIN16_PREFIX);
    const auto decoded = decodeZdleN(data, offset + prefixLen, 5 + static_cast<size_t>(crcLen / 8), true);
    if (!decoded) {
        result.status = ParseStatus::NeedMore;
        return result;
    }
    const Bytes &bodyAndCrc = decoded->first;
    const size_t encodedLen = decoded->second;
    const uint16_t expected16 = crc16(bodyAndCrc.data(), 5);
    uint32_t expected32 = crc32(bodyAndCrc.data(), 5);
    if (crcLen == 16) {
        const uint16_t got = static_cast<uint16_t>((bodyAndCrc[5] << 8) | bodyAndCrc[6]);
        if (expected16 != got) {
            result.status = ParseStatus::Invalid;
            return result;
        }
    } else {
        const uint32_t got = static_cast<uint32_t>(bodyAndCrc[5]) |
                             (static_cast<uint32_t>(bodyAndCrc[6]) << 8) |
                             (static_cast<uint32_t>(bodyAndCrc[7]) << 16) |
                             (static_cast<uint32_t>(bodyAndCrc[8]) << 24);
        if (expected32 != got) {
            result.status = ParseStatus::Invalid;
            return result;
        }
    }

    const size_t consumed = prefixLen + encodedLen;
    result.status = ParseStatus::Ok;
    result.header.frameType = bodyAndCrc[0];
    std::copy(bodyAndCrc.begin() + 1, bodyAndCrc.begin() + 5, result.header.data.begin());
    result.header.crcLen = crcLen;
    result.header.raw.assign(data.begin() + offset, data.begin() + offset + consumed);
    result.consumed = consumed;
    return result;
}

} // namespace

bool isSubpacketEnd(uint8_t value)
{
    return value == SUBPACKET_ZCRCE || value == SUBPACKET_ZCRCG ||
           value == SUBPACKET_ZCRCQ || value == SUBPACKET_ZCRCW;
}

Bytes zdleEncode(const Bytes &data, bool escapeCtrl, bool escape8bit)
{
    Bytes out;
    out.reserve(data.size() + data.size() / 32 + 8);
    uint8_t previous = 0;
    for (uint8_t value : data) {
        if (isEscapeByte(value, escapeCtrl, escape8bit, previous)) {
            out.push_back(ZDLE);
            out.push_back(static_cast<uint8_t>(value ^ 0x40));
        } else {
            out.push_back(value);
        }
        previous = value;
    }
    return out;
}

Bytes zdleDecode(const Bytes &data, bool ignoreXonOff)
{
    Bytes out;
    out.reserve(data.size());
    size_t index = 0;
    while (index < data.size()) {
        const uint8_t value = data[index++];
        if (value == ZDLE) {
            if (index >= data.size()) {
                throw ZmodemError("trailing ZDLE");
            }
            out.push_back(decodeZdleByte(data[index++]));
        } else if (ignoreXonOff &&
                   (value == XON || value == XOFF || value == XON_HIGH || value == XOFF_HIGH)) {
            continue;
        } else {
            out.push_back(value);
        }
    }
    return out;
}

HeaderParseResult parseHeaderAt(const Bytes &data, size_t offset)
{
    HeaderParseResult result;
    if (offset >= data.size()) {
        result.status = ParseStatus::NeedMore;
        return result;
    }

    auto startsWith = [&](const uint8_t *prefix, size_t length) {
        return data.size() - offset >= length &&
               std::memcmp(data.data() + offset, prefix, length) == 0;
    };
    if (startsWith(HEX_PREFIX, sizeof(HEX_PREFIX))) {
        return parseHexHeader(data, offset);
    }
    if (startsWith(BIN32_PREFIX, sizeof(BIN32_PREFIX))) {
        return parseBinaryHeader(data, offset, 32);
    }
    if (startsWith(BIN16_PREFIX, sizeof(BIN16_PREFIX))) {
        return parseBinaryHeader(data, offset, 16);
    }

    if (isPrefixOf(data, offset, HEX_PREFIX, sizeof(HEX_PREFIX)) ||
        isPrefixOf(data, offset, BIN16_PREFIX, sizeof(BIN16_PREFIX)) ||
        isPrefixOf(data, offset, BIN32_PREFIX, sizeof(BIN32_PREFIX))) {
        result.status = ParseStatus::NeedMore;
        return result;
    }
    result.status = ParseStatus::Invalid;
    return result;
}

std::optional<std::pair<Header, size_t>> parseHeader(const Bytes &data)
{
    size_t index = 0;
    while (index < data.size()) {
        const auto star = std::find(data.begin() + static_cast<std::ptrdiff_t>(index), data.end(),
                                    static_cast<uint8_t>('*'));
        if (star == data.end()) {
            return std::nullopt;
        }
        const size_t starIndex = static_cast<size_t>(star - data.begin());
        const HeaderParseResult result = parseHeaderAt(data, starIndex);
        if (result.status == ParseStatus::Ok) {
            return std::make_pair(result.header, starIndex + result.consumed);
        }
        if (result.status == ParseStatus::NeedMore) {
            return std::nullopt;
        }
        index = starIndex + 1;
    }
    return std::nullopt;
}

Bytes encodeHexHeader(uint8_t frameType, const std::array<uint8_t, 4> &data)
{
    uint8_t body[7];
    body[0] = frameType;
    std::copy(data.begin(), data.end(), body + 1);
    const uint16_t crc = crc16(body, 5);
    body[5] = static_cast<uint8_t>(crc >> 8);
    body[6] = static_cast<uint8_t>(crc & 0xFF);

    static const char digits[] = "0123456789abcdef";
    Bytes out(HEX_PREFIX, HEX_PREFIX + sizeof(HEX_PREFIX));
    out.reserve(out.size() + 16);
    for (uint8_t value : body) {
        out.push_back(static_cast<uint8_t>(digits[value >> 4]));
        out.push_back(static_cast<uint8_t>(digits[value & 0x0F]));
    }
    out.push_back('\r');
    out.push_back('\n');
    out.push_back(XON);
    return out;
}

Bytes encodeBinaryHeader(uint8_t frameType, const std::array<uint8_t, 4> &data, int crcLen)
{
    Bytes body;
    body.reserve(5 + static_cast<size_t>(crcLen / 8));
    body.push_back(frameType);
    body.insert(body.end(), data.begin(), data.end());
    if (crcLen == 16) {
        const auto crc = packCrc16(crc16(body));
        body.insert(body.end(), crc.begin(), crc.end());
    } else if (crcLen == 32) {
        const auto crc = packCrc32(crc32(body));
        body.insert(body.end(), crc.begin(), crc.end());
    } else {
        throw ZmodemError("crc_len must be 16 or 32");
    }

    const uint8_t *prefix = crcLen == 16 ? BIN16_PREFIX : BIN32_PREFIX;
    Bytes out(prefix, prefix + sizeof(BIN16_PREFIX));
    const Bytes encoded = zdleEncode(body);
    out.insert(out.end(), encoded.begin(), encoded.end());
    return out;
}

Bytes encodeSubpacket(const Bytes &payload, uint8_t frameEnd, int crcLen)
{
    if (!isSubpacketEnd(frameEnd)) {
        throw ZmodemError("invalid subpacket frame end");
    }
    Bytes body = payload;
    body.push_back(frameEnd);
    Bytes crcBytes;
    if (crcLen == 16) {
        crcBytes = packCrc16(crc16(body));
    } else if (crcLen == 32) {
        crcBytes = packCrc32(crc32(body));
    } else {
        throw ZmodemError("crc_len must be 16 or 32");
    }

    Bytes out = zdleEncode(payload);
    out.push_back(ZDLE);
    out.push_back(frameEnd);
    const Bytes encodedCrc = zdleEncode(crcBytes);
    out.insert(out.end(), encodedCrc.begin(), encodedCrc.end());
    return out;
}

std::optional<std::pair<Subpacket, size_t>> parseSubpacket(const Bytes &data, int crcLen)
{
    if (crcLen != 16 && crcLen != 32) {
        throw ZmodemError("crc_len must be 16 or 32");
    }

    size_t delimiter = data.size();
    for (size_t index = 0; index < data.size();) {
        const uint8_t value = data[index];
        if (value == ZDLE) {
            if (index + 1 >= data.size()) {
                return std::nullopt;
            }
            if (isSubpacketEnd(data[index + 1])) {
                delimiter = index;
                break;
            }
            index += 2;
        } else {
            index += 1;
        }
    }
    if (delimiter == data.size()) {
        return std::nullopt;
    }

    const uint8_t frameEnd = data[delimiter + 1];
    const Bytes payloadEncoded(data.begin(), data.begin() + static_cast<std::ptrdiff_t>(delimiter));
    const Bytes payload = zdleDecode(payloadEncoded, true);

    const auto decodedCrc = decodeZdleN(data, delimiter + 2, static_cast<size_t>(crcLen / 8), true);
    if (!decodedCrc) {
        return std::nullopt;
    }
    const Bytes &crcBytes = decodedCrc->first;
    const size_t encodedCrcLen = decodedCrc->second;

    Bytes body = payload;
    body.push_back(frameEnd);
    if (crcLen == 16) {
        const uint16_t expected = crc16(body);
        const uint16_t got = static_cast<uint16_t>((crcBytes[0] << 8) | crcBytes[1]);
        if (expected != got) {
            throw ZmodemError("bad subpacket CRC");
        }
    } else {
        const uint32_t expected = crc32(body);
        const uint32_t got = static_cast<uint32_t>(crcBytes[0]) |
                             (static_cast<uint32_t>(crcBytes[1]) << 8) |
                             (static_cast<uint32_t>(crcBytes[2]) << 16) |
                             (static_cast<uint32_t>(crcBytes[3]) << 24);
        if (expected != got) {
            throw ZmodemError("bad subpacket CRC");
        }
    }

    Subpacket packet;
    packet.frameEnd = frameEnd;
    packet.payload = payload;
    packet.crcLen = crcLen;
    packet.rawLen = delimiter + 2 + encodedCrcLen;
    return std::make_pair(std::move(packet), delimiter + 2 + encodedCrcLen);
}

std::array<uint8_t, 4> packOffset(uint32_t offset)
{
    return {static_cast<uint8_t>(offset & 0xFF), static_cast<uint8_t>((offset >> 8) & 0xFF),
            static_cast<uint8_t>((offset >> 16) & 0xFF),
            static_cast<uint8_t>((offset >> 24) & 0xFF)};
}

uint32_t unpackOffset(const std::array<uint8_t, 4> &data)
{
    return static_cast<uint32_t>(data[0]) | (static_cast<uint32_t>(data[1]) << 8) |
           (static_cast<uint32_t>(data[2]) << 16) | (static_cast<uint32_t>(data[3]) << 24);
}

Bytes stripIgnoredBytes(const Bytes &data)
{
    Bytes out;
    out.reserve(data.size());
    for (uint8_t value : data) {
        if (value == XON || value == XOFF || value == XON_HIGH || value == XOFF_HIGH) {
            continue;
        }
        out.push_back(value);
    }
    return out;
}

} // namespace zmodem
