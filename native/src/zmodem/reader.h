#pragma once

#include "zmodem/codec.h"
#include "zmodem/io.h"

#include <optional>

namespace zmodem {

class ZmodemReader
{
public:
    explicit ZmodemReader(ZmodemIO &io, double readTimeout = 30.0);

    Header readHeader(std::optional<double> timeout = std::nullopt);
    Subpacket readSubpacket(int crcLen);
    Bytes readRaw(size_t count);
    void clearBuffer();
    void prepend(const Bytes &data);
    Bytes drain();

    Bytes &buffer() { return buffer_; }
    const Bytes &buffer() const { return buffer_; }

private:
    void readMore(std::optional<double> timeout = std::nullopt);
    void checkCancel();

    ZmodemIO &io_;
    double readTimeout_;
    Bytes buffer_;
};

} // namespace zmodem
