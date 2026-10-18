#include "zmodem/reader.h"
#include "zmodem/errors.h"

#include <algorithm>
#include <cstring>

namespace zmodem {

ZmodemReader::ZmodemReader(ZmodemIO &io, double readTimeout)
    : io_(io), readTimeout_(readTimeout)
{
}

void ZmodemReader::readMore(std::optional<double> timeout)
{
    const double wait = timeout.value_or(readTimeout_);
    Bytes data = io_.readSome(8192, wait);
    if (data.empty()) {
        throw ZmodemError("ZMODEM stream closed");
    }
    buffer_.insert(buffer_.end(), data.begin(), data.end());
}

void ZmodemReader::checkCancel()
{
    static const uint8_t canRun[] = {ZDLE, ZDLE, ZDLE, ZDLE, ZDLE, ZDLE, ZDLE, ZDLE};
    if (buffer_.size() >= sizeof(canRun) &&
        std::search(buffer_.begin(), buffer_.end(), std::begin(canRun), std::end(canRun)) !=
            buffer_.end()) {
        throw ZmodemAbort("peer cancelled the transfer");
    }
}

Header ZmodemReader::readHeader(std::optional<double> timeout)
{
    while (true) {
        if (buffer_.empty()) {
            readMore(timeout);
            continue;
        }
        checkCancel();

        const auto star = std::find(buffer_.begin(), buffer_.end(), static_cast<uint8_t>('*'));
        if (star == buffer_.end()) {
            const size_t keep = std::min<size_t>(7, buffer_.size());
            Bytes tail(buffer_.end() - static_cast<std::ptrdiff_t>(keep), buffer_.end());
            buffer_.swap(tail);
            readMore(timeout);
            continue;
        }
        const size_t starIndex = static_cast<size_t>(star - buffer_.begin());
        if (starIndex > 0) {
            buffer_.erase(buffer_.begin(), buffer_.begin() + static_cast<std::ptrdiff_t>(starIndex));
        }

        const HeaderParseResult result = parseHeaderAt(buffer_, 0);
        if (result.status == ParseStatus::Ok) {
            buffer_.erase(buffer_.begin(),
                          buffer_.begin() + static_cast<std::ptrdiff_t>(result.consumed));
            return result.header;
        }
        if (result.status == ParseStatus::NeedMore) {
            readMore(timeout);
            continue;
        }
        buffer_.erase(buffer_.begin());
    }
}

Subpacket ZmodemReader::readSubpacket(int crcLen)
{
    while (true) {
        if (buffer_.empty()) {
            readMore();
            continue;
        }
        checkCancel();
        const auto parsed = parseSubpacket(buffer_, crcLen);
        if (parsed) {
            buffer_.erase(buffer_.begin(),
                          buffer_.begin() + static_cast<std::ptrdiff_t>(parsed->second));
            return parsed->first;
        }
        readMore();
    }
}

Bytes ZmodemReader::readRaw(size_t count)
{
    while (buffer_.size() < count) {
        readMore();
    }
    Bytes data(buffer_.begin(), buffer_.begin() + static_cast<std::ptrdiff_t>(count));
    buffer_.erase(buffer_.begin(), buffer_.begin() + static_cast<std::ptrdiff_t>(count));
    return data;
}

void ZmodemReader::clearBuffer()
{
    buffer_.clear();
}

void ZmodemReader::prepend(const Bytes &data)
{
    if (data.empty()) {
        return;
    }
    Bytes merged = data;
    merged.insert(merged.end(), buffer_.begin(), buffer_.end());
    buffer_.swap(merged);
}

Bytes ZmodemReader::drain()
{
    Bytes result = buffer_;
    buffer_.clear();
    const Bytes rest = io_.drain();
    result.insert(result.end(), rest.begin(), rest.end());
    return result;
}

} // namespace zmodem
