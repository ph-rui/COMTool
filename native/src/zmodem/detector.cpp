#include "zmodem/detector.h"

#include <algorithm>

namespace zmodem {

void ZmodemDetector::reset()
{
    pending_.clear();
}

DetectionResult ZmodemDetector::feed(const Bytes &data)
{
    DetectionResult result;
    if (data.empty()) {
        return result;
    }

    Bytes buffer = pending_;
    pending_.clear();
    buffer.insert(buffer.end(), data.begin(), data.end());

    size_t index = 0;
    while (index < buffer.size()) {
        const auto star = std::find(buffer.begin() + static_cast<std::ptrdiff_t>(index), buffer.end(),
                                    static_cast<uint8_t>('*'));
        if (star == buffer.end()) {
            result.terminalData.insert(result.terminalData.end(),
                                       buffer.begin() + static_cast<std::ptrdiff_t>(index),
                                       buffer.end());
            break;
        }
        const size_t starIndex = static_cast<size_t>(star - buffer.begin());
        result.terminalData.insert(result.terminalData.end(),
                                   buffer.begin() + static_cast<std::ptrdiff_t>(index),
                                   buffer.begin() + static_cast<std::ptrdiff_t>(starIndex));

        const HeaderParseResult parsed = parseHeaderAt(buffer, starIndex);
        if (parsed.status == ParseStatus::Ok) {
            if (parsed.header.frameType == FRAME_ZRQINIT ||
                parsed.header.frameType == FRAME_ZRINIT) {
                result.hasDetection = true;
                result.detection.direction = parsed.header.frameType == FRAME_ZRQINIT
                                                 ? Detection::Direction::Receive
                                                 : Detection::Direction::Send;
                result.detection.header = parsed.header;
                result.detection.remainder.assign(
                    buffer.begin() + static_cast<std::ptrdiff_t>(starIndex + parsed.consumed),
                    buffer.end());
                return result;
            }
            result.terminalData.insert(
                result.terminalData.end(), buffer.begin() + static_cast<std::ptrdiff_t>(starIndex),
                buffer.begin() + static_cast<std::ptrdiff_t>(starIndex + parsed.consumed));
            index = starIndex + parsed.consumed;
            continue;
        }
        if (parsed.status == ParseStatus::NeedMore) {
            pending_.assign(buffer.begin() + static_cast<std::ptrdiff_t>(starIndex), buffer.end());
            return result;
        }
        result.terminalData.push_back(buffer[starIndex]);
        index = starIndex + 1;
    }
    return result;
}

} // namespace zmodem
