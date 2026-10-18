#pragma once

#include <stdexcept>
#include <string>

namespace zmodem {

class ZmodemError : public std::runtime_error
{
public:
    explicit ZmodemError(const std::string &message) : std::runtime_error(message) {}
};

class ZmodemTimeout : public ZmodemError
{
public:
    explicit ZmodemTimeout(const std::string &message) : ZmodemError(message) {}
};

class ZmodemAbort : public ZmodemError
{
public:
    explicit ZmodemAbort(const std::string &message) : ZmodemError(message) {}
};

} // namespace zmodem
