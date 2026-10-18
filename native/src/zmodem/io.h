#pragma once

#include "zmodem/codec.h"

#include <cstddef>

namespace zmodem {

class ZmodemIO
{
public:
    virtual ~ZmodemIO() = default;

    virtual void write(const Bytes &data) = 0;
    virtual Bytes readSome(size_t maxBytes, double timeoutSeconds) = 0;
    virtual bool cancelled() const = 0;
    virtual Bytes drain() { return {}; }
};

} // namespace zmodem
