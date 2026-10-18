#pragma once

#include "zmodem/codec.h"

namespace zmodem {

struct Detection
{
    enum class Direction
    {
        Receive,
        Send,
    };

    Direction direction = Direction::Receive;
    Header header;
    Bytes remainder;
};

struct DetectionResult
{
    Bytes terminalData;
    bool hasDetection = false;
    Detection detection;
};

class ZmodemDetector
{
public:
    void reset();
    DetectionResult feed(const Bytes &data);

private:
    Bytes pending_;
};

} // namespace zmodem
