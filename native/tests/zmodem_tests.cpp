#include "zmodem/codec.h"
#include "zmodem/detector.h"
#include "zmodem/errors.h"
#include "zmodem/filename.h"
#include "zmodem/io.h"
#include "zmodem/receiver.h"
#include "zmodem/sender.h"

#include <QDir>
#include <QFile>
#include <QTemporaryDir>

#include <condition_variable>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <thread>

using namespace zmodem;

namespace {

int failures = 0;

void check(bool condition, const char *message)
{
    if (!condition) {
        std::fprintf(stderr, "FAIL: %s\n", message);
        ++failures;
    }
}

void checkEqual(size_t actual, size_t expected, const char *message)
{
    if (actual != expected) {
        std::fprintf(stderr, "FAIL: %s (%zu != %zu)\n", message, actual, expected);
        ++failures;
    }
}

Bytes bytes(std::initializer_list<uint8_t> values)
{
    return Bytes(values);
}

Bytes ascii(const char *text)
{
    return Bytes(text, text + std::strlen(text));
}

class Pipe
{
public:
    void write(const Bytes &data)
    {
        std::lock_guard<std::mutex> lock(mutex_);
        data_.insert(data_.end(), data.begin(), data.end());
        condition_.notify_all();
    }

    Bytes readSome(size_t maxBytes, double timeoutSeconds)
    {
        std::unique_lock<std::mutex> lock(mutex_);
        if (data_.empty()) {
            const auto timeout = std::chrono::milliseconds(
                static_cast<long>(timeoutSeconds * 1000.0));
            if (!condition_.wait_for(lock, timeout, [&] { return !data_.empty() || closed_; })) {
                throw ZmodemTimeout("test pipe timeout");
            }
        }
        if (data_.empty()) {
            throw ZmodemTimeout("test pipe closed");
        }
        const size_t count = std::min(maxBytes, data_.size());
        Bytes result(data_.begin(), data_.begin() + static_cast<std::ptrdiff_t>(count));
        data_.erase(data_.begin(), data_.begin() + static_cast<std::ptrdiff_t>(count));
        return result;
    }

    Bytes drain()
    {
        std::lock_guard<std::mutex> lock(mutex_);
        Bytes result = data_;
        data_.clear();
        return result;
    }

    void close()
    {
        std::lock_guard<std::mutex> lock(mutex_);
        closed_ = true;
        condition_.notify_all();
    }

private:
    std::mutex mutex_;
    std::condition_variable condition_;
    Bytes data_;
    bool closed_ = false;
};

class TestIO : public ZmodemIO
{
public:
    TestIO(Pipe &input, Pipe &output) : input_(input), output_(output) {}

    void write(const Bytes &data) override { output_.write(data); }
    Bytes readSome(size_t maxBytes, double timeoutSeconds) override
    {
        return input_.readSome(maxBytes, timeoutSeconds);
    }
    bool cancelled() const override { return false; }
    Bytes drain() override { return input_.drain(); }

private:
    Pipe &input_;
    Pipe &output_;
};

void testCrc()
{
    const Bytes text = ascii("123456789");
    check(crc16(text) == 0x31C3, "CRC16 vector");
    check(crc32(text) == 0xCBF43926u, "CRC32 vector");
}

void testZdle()
{
    Bytes data;
    for (int i = 0; i < 256; ++i) {
        data.push_back(static_cast<uint8_t>(i));
    }
    const Bytes encoded = zdleEncode(data);
    check(zdleDecode(encoded) == data, "ZDLE roundtrip");
    check(encoded.size() > data.size(), "ZDLE escapes control bytes");
}

void testHeaders()
{
    for (int crcLen : {16, 32}) {
        const Header original{FRAME_ZFILE, {1, 2, 3, 4}, crcLen, {}};
        const Bytes encoded = crcLen == 16 ? encodeHexHeader(FRAME_ZFILE, original.data)
                                           : encodeBinaryHeader(FRAME_ZFILE, original.data, crcLen);
        const HeaderParseResult parsed = parseHeaderAt(encoded, 0);
        check(parsed.status == ParseStatus::Ok, "header parse");
        check(parsed.header.frameType == FRAME_ZFILE, "header type");
        check(parsed.header.data == original.data, "header payload");
        check(parsed.consumed == encoded.size(), "header consumed");
    }

    const Bytes encoded = encodeHexHeader(FRAME_ZRINIT, {0, 0, 0, 0x21});
    Bytes truncated(encoded.begin(), encoded.end() - 2);
    check(parseHeaderAt(truncated, 0).status == ParseStatus::NeedMore, "truncated header");
    Bytes corrupt = encoded;
    corrupt[8] = corrupt[8] == 'a' ? 'b' : 'a';
    check(parseHeaderAt(corrupt, 0).status == ParseStatus::Invalid, "corrupt header");
}

void testSubpacket()
{
    Bytes payload;
    for (int i = 0; i < 4096; ++i) {
        payload.push_back(static_cast<uint8_t>((i * 37) & 0xFF));
    }
    for (int crcLen : {16, 32}) {
        const Bytes encoded = encodeSubpacket(payload, SUBPACKET_ZCRCW, crcLen);
        const auto parsed = parseSubpacket(encoded, crcLen);
        check(parsed.has_value(), "subpacket parse");
        check(parsed->first.payload == payload, "subpacket payload");
        check(parsed->first.frameEnd == SUBPACKET_ZCRCW, "subpacket end");
        check(parsed->second == encoded.size(), "subpacket consumed");
    }
}

void testDetector()
{
    const Bytes header = encodeHexHeader(FRAME_ZRINIT, {0, 0, 0, ZRINIT_CANFC32});
    ZmodemDetector detector;
    const Bytes first = ascii("hello");
    DetectionResult result = detector.feed(first);
    check(result.terminalData == first, "detector passes text");
    check(!result.hasDetection, "detector no false positive");

    Bytes split = first;
    split.insert(split.end(), header.begin(), header.begin() + 3);
    result = detector.feed(split);
    check(result.terminalData == first, "detector holds partial header");
    check(!result.hasDetection, "detector waits for complete header");

    Bytes rest(header.begin() + 3, header.end());
    rest.insert(rest.end(), {'t', 'a', 'i', 'l'});
    result = detector.feed(rest);
    check(result.hasDetection, "detector finds header");
    check(result.detection.direction == Detection::Direction::Send, "detector direction");
    check(result.detection.remainder == ascii("tail"), "detector remainder");
}

void testFilename()
{
    check(sanitizeBasename(QStringLiteral("../../etc/passwd")) == QStringLiteral("passwd"),
          "path traversal stripped");
    check(sanitizeBasename(QStringLiteral("CON")) == QStringLiteral("CON_"), "reserved name");
    check(sanitizeBasename(QStringLiteral("a:b?c")) == QStringLiteral("a_b_c"), "invalid chars");
    check(sanitizeBasename(QString()).isEmpty() == false, "empty name fallback");
}

void testMetadata()
{
    Bytes payload = ascii("hello.bin");
    payload.push_back(0);
    const Bytes attrs = ascii("123 14524770400 751 0 0 0");
    payload.insert(payload.end(), attrs.begin(), attrs.end());
    const FileMeta meta = parseZfileMetadata(payload);
    check(meta.name == QStringLiteral("hello.bin"), "metadata name");
    check(meta.size == 123, "metadata size");
    check(meta.mtime == 1700000000, "metadata octal mtime");
    check(meta.mode == 0751, "metadata octal mode");
}

void testTransfer(size_t payloadSize)
{
    Pipe toReceiver;
    Pipe toSender;
    TestIO senderIo(toSender, toReceiver);
    TestIO receiverIo(toReceiver, toSender);

    QTemporaryDir sourceDir;
    QTemporaryDir outputDir;
    check(sourceDir.isValid() && outputDir.isValid(), "temp dirs");

    const QString sourcePath = sourceDir.filePath(QStringLiteral("payload.bin"));
    Bytes payload;
    for (size_t i = 0; i < payloadSize; ++i) {
        payload.push_back(static_cast<uint8_t>((i * 13 + 7) & 0xFF));
    }
    {
        QFile file(sourcePath);
        check(file.open(QIODevice::WriteOnly), "source open");
        check(file.write(reinterpret_cast<const char *>(payload.data()),
                         static_cast<qint64>(payload.size())) == static_cast<qint64>(payload.size()),
              "source write");
    }

    std::exception_ptr senderError;
    std::exception_ptr receiverError;
    ReceiveResult received;

    std::thread senderThread([&] {
        try {
            ZmodemSender sender(senderIo, {sourcePath});
            sender.run();
        } catch (...) {
            senderError = std::current_exception();
            toReceiver.close();
        }
    });
    std::thread receiverThread([&] {
        try {
            ZmodemReceiver receiver(receiverIo, outputDir.path());
            received = receiver.run();
        } catch (...) {
            receiverError = std::current_exception();
            toSender.close();
        }
    });

    senderThread.join();
    receiverThread.join();
    toReceiver.close();
    toSender.close();

    if (senderError) {
        try {
            std::rethrow_exception(senderError);
        } catch (const std::exception &error) {
            std::fprintf(stderr, "sender error: %s\n", error.what());
        }
        ++failures;
    }
    if (receiverError) {
        try {
            std::rethrow_exception(receiverError);
        } catch (const std::exception &error) {
            std::fprintf(stderr, "receiver error: %s\n", error.what());
        }
        ++failures;
    }
    check(received.files.size() == 1, "one file received");
    if (!received.files.empty()) {
        QFile file(received.files.front().path);
        check(file.open(QIODevice::ReadOnly), "received open");
        const QByteArray data = file.readAll();
        check(static_cast<size_t>(data.size()) == payload.size(), "received size");
        check(payload.empty() ||
                  std::memcmp(data.constData(), payload.data(), payload.size()) == 0,
              "received contents");
    }
}

} // namespace

int main()
{
    testCrc();
    testZdle();
    testHeaders();
    testSubpacket();
    testDetector();
    testFilename();
    testMetadata();
    testTransfer(0);
    testTransfer(262144);

    if (failures == 0) {
        std::printf("zmodem_tests: all checks passed\n");
        return 0;
    }
    std::fprintf(stderr, "zmodem_tests: %d failure(s)\n", failures);
    return 1;
}
