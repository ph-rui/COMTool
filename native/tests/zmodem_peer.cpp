// A deliberately small ZMODEM endpoint for interoperability tests against
// lrzsz over a PTY.  It is not part of the GUI application.
#include "zmodem/errors.h"
#include "zmodem/io.h"
#include "zmodem/receiver.h"
#include "zmodem/sender.h"

#include <QCoreApplication>
#include <QDir>
#include <QStringList>

#include <cerrno>
#include <cstdio>
#include <cstring>
#include <poll.h>
#include <termios.h>
#include <unistd.h>

using namespace zmodem;

namespace {

class StdioIO : public ZmodemIO
{
public:
    StdioIO()
    {
        if (isatty(STDIN_FILENO) && tcgetattr(STDIN_FILENO, &saved_) == 0) {
            termios raw = saved_;
            cfmakeraw(&raw);
            raw.c_cflag |= CLOCAL | CREAD;
            tcsetattr(STDIN_FILENO, TCSANOW, &raw);
            rawEnabled_ = true;
        }
    }
    ~StdioIO() override
    {
        if (rawEnabled_) {
            tcsetattr(STDIN_FILENO, TCSANOW, &saved_);
        }
    }

    void write(const Bytes &data) override
    {
        size_t offset = 0;
        while (offset < data.size()) {
            const ssize_t written = ::write(STDOUT_FILENO, data.data() + offset,
                                            data.size() - offset);
            if (written < 0) {
                if (errno == EINTR) {
                    continue;
                }
                throw ZmodemError(std::strerror(errno));
            }
            offset += static_cast<size_t>(written);
        }
    }

    Bytes readSome(size_t maxBytes, double timeoutSeconds) override
    {
        pollfd fd{STDIN_FILENO, POLLIN, 0};
        const int waitMs = timeoutSeconds < 0 ? -1 : static_cast<int>(timeoutSeconds * 1000.0);
        int ready = 0;
        do {
            ready = ::poll(&fd, 1, waitMs);
        } while (ready < 0 && errno == EINTR);
        if (ready < 0) {
            throw ZmodemError(std::strerror(errno));
        }
        if (ready == 0) {
            throw ZmodemTimeout("timed out waiting for ZMODEM data");
        }
        Bytes buffer(maxBytes);
        const ssize_t count = ::read(STDIN_FILENO, buffer.data(), maxBytes);
        if (count < 0) {
            throw ZmodemError(std::strerror(errno));
        }
        if (count == 0) {
            throw ZmodemTimeout("stream closed");
        }
        buffer.resize(static_cast<size_t>(count));
        return buffer;
    }

    bool cancelled() const override { return false; }
    Bytes drain() override { return {}; }

private:
    termios saved_{};
    bool rawEnabled_ = false;
};

} // namespace

int main(int argc, char **argv)
{
    QCoreApplication app(argc, argv);
    QStringList args = app.arguments();
    if (args.size() < 2) {
        std::fprintf(stderr, "usage: %s receive <dir> | send <file>...\n", argv[0]);
        return 2;
    }

    StdioIO io;
    try {
        if (args.at(1) == QLatin1String("receive")) {
            if (args.size() < 3) {
                std::fprintf(stderr, "receive needs an output directory\n");
                return 2;
            }
            QDir().mkpath(args.at(2));
            ZmodemReceiver receiver(io, args.at(2));
            const ReceiveResult result = receiver.run();
            for (const FileInfo &file : result.files) {
                std::printf("RECEIVED\t%s\t%lld\n", file.path.toUtf8().constData(),
                            static_cast<long long>(file.size));
            }
            std::fflush(stdout);
            return 0;
        }
        if (args.at(1) == QLatin1String("send")) {
            const QStringList files = args.mid(2);
            if (files.isEmpty()) {
                std::fprintf(stderr, "send needs at least one file\n");
                return 2;
            }
            ZmodemSender sender(io, files);
            sender.run();
            std::printf("SENT\n");
            std::fflush(stdout);
            return 0;
        }
    } catch (const std::exception &error) {
        std::fprintf(stderr, "zmodem_peer: %s\n", error.what());
        return 1;
    }
    std::fprintf(stderr, "unknown mode: %s\n", args.at(1).toUtf8().constData());
    return 2;
}
