#pragma once

#include "zmodem/crc.h"

#include <array>
#include <cstdint>
#include <optional>
#include <utility>

namespace zmodem {

constexpr uint8_t ZPAD = 0x2A;
constexpr uint8_t ZDLE = 0x18;
constexpr uint8_t ZBIN = 0x41;
constexpr uint8_t ZHEX = 0x42;
constexpr uint8_t ZBIN32 = 0x43;

constexpr uint8_t XON = 0x11;
constexpr uint8_t XOFF = 0x13;
constexpr uint8_t XON_HIGH = 0x91;
constexpr uint8_t XOFF_HIGH = 0x93;

constexpr uint8_t FRAME_ZRQINIT = 0;
constexpr uint8_t FRAME_ZRINIT = 1;
constexpr uint8_t FRAME_ZSINIT = 2;
constexpr uint8_t FRAME_ZACK = 3;
constexpr uint8_t FRAME_ZFILE = 4;
constexpr uint8_t FRAME_ZSKIP = 5;
constexpr uint8_t FRAME_ZNAK = 6;
constexpr uint8_t FRAME_ZABORT = 7;
constexpr uint8_t FRAME_ZFIN = 8;
constexpr uint8_t FRAME_ZRPOS = 9;
constexpr uint8_t FRAME_ZDATA = 10;
constexpr uint8_t FRAME_ZEOF = 11;
constexpr uint8_t FRAME_ZFERR = 12;
constexpr uint8_t FRAME_ZCRC = 13;
constexpr uint8_t FRAME_ZCHALLENGE = 14;
constexpr uint8_t FRAME_ZCOMPL = 15;
constexpr uint8_t FRAME_ZCAN = 16;
constexpr uint8_t FRAME_ZFREECNT = 17;
constexpr uint8_t FRAME_ZCOMMAND = 18;
constexpr uint8_t FRAME_ZSTDERR = 19;

constexpr uint8_t SUBPACKET_ZCRCE = 0x68;
constexpr uint8_t SUBPACKET_ZCRCG = 0x69;
constexpr uint8_t SUBPACKET_ZCRCQ = 0x6A;
constexpr uint8_t SUBPACKET_ZCRCW = 0x6B;

constexpr uint8_t ZRINIT_CANFDX = 0x01;
constexpr uint8_t ZRINIT_CANOVIO = 0x02;
constexpr uint8_t ZRINIT_CANBRK = 0x04;
constexpr uint8_t ZRINIT_CANCRY = 0x08;
constexpr uint8_t ZRINIT_CANLZW = 0x10;
constexpr uint8_t ZRINIT_CANFC32 = 0x20;
constexpr uint8_t ZRINIT_ESCCTL = 0x40;
constexpr uint8_t ZRINIT_ESC8 = 0x80;

struct Header
{
    uint8_t frameType = 0;
    std::array<uint8_t, 4> data{};
    int crcLen = 16;
    Bytes raw;
};

enum class ParseStatus
{
    Ok,
    NeedMore,
    Invalid,
};

struct HeaderParseResult
{
    ParseStatus status = ParseStatus::NeedMore;
    Header header;
    size_t consumed = 0;
};

struct Subpacket
{
    uint8_t frameEnd = 0;
    Bytes payload;
    int crcLen = 16;
    size_t rawLen = 0;

    bool frameEndFrame() const
    {
        return frameEnd == SUBPACKET_ZCRCE || frameEnd == SUBPACKET_ZCRCW;
    }
    bool ackExpected() const
    {
        return frameEnd == SUBPACKET_ZCRCQ || frameEnd == SUBPACKET_ZCRCW;
    }
};

bool isSubpacketEnd(uint8_t value);

Bytes zdleEncode(const Bytes &data, bool escapeCtrl = false, bool escape8bit = false);
Bytes zdleDecode(const Bytes &data, bool ignoreXonOff = false);

HeaderParseResult parseHeaderAt(const Bytes &data, size_t offset = 0);
std::optional<std::pair<Header, size_t>> parseHeader(const Bytes &data);

Bytes encodeHexHeader(uint8_t frameType, const std::array<uint8_t, 4> &data = {});
Bytes encodeBinaryHeader(uint8_t frameType, const std::array<uint8_t, 4> &data = {},
                         int crcLen = 16);
Bytes encodeSubpacket(const Bytes &payload, uint8_t frameEnd, int crcLen = 16);

// Throws ZmodemError on a complete packet with a bad CRC.  Returns nullopt
// when more bytes are needed.
std::optional<std::pair<Subpacket, size_t>> parseSubpacket(const Bytes &data, int crcLen = 16);

std::array<uint8_t, 4> packOffset(uint32_t offset);
uint32_t unpackOffset(const std::array<uint8_t, 4> &data);

Bytes stripIgnoredBytes(const Bytes &data);

} // namespace zmodem
