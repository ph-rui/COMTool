import hashlib
import os

import pytest

from COMTool.zmodem.sender import ZmodemSender
from tests.zmodem_peer import LrzszPeer


@pytest.fixture
def peer():
    instance = LrzszPeer()
    if not instance.available:
        pytest.skip("lrzsz not available; set COMTOOL_LRZSZ_DIR")
    try:
        yield instance
    finally:
        instance.close()


def _sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(65536), b""):
            digest.update(chunk)
    return digest.hexdigest()


def test_send_single_file_to_lrz(peer, tmp_path):
    source = tmp_path / "source.bin"
    source.write_bytes(os.urandom(256 * 1024))
    output = tmp_path / "output"
    output.mkdir()

    peer.start_receiver(cwd=str(output))
    sender = ZmodemSender(peer.io, [str(source)])
    sender.run()

    assert peer.wait(timeout=20) == 0
    received = output / "source.bin"
    assert received.exists()
    assert _sha256(str(received)) == _sha256(str(source))


def test_send_multiple_files_to_lrz(peer, tmp_path):
    first = tmp_path / "first file.bin"
    second = tmp_path / "second.bin"
    first.write_bytes(os.urandom(64 * 1024))
    second.write_bytes(os.urandom(96 * 1024))
    output = tmp_path / "output"
    output.mkdir()

    peer.start_receiver(cwd=str(output))
    sender = ZmodemSender(peer.io, [str(first), str(second)])
    sender.run()

    assert peer.wait(timeout=20) == 0
    assert _sha256(str(output / "first file.bin")) == _sha256(str(first))
    assert _sha256(str(output / "second.bin")) == _sha256(str(second))


def test_send_one_megabyte_to_lrz(peer, tmp_path):
    source = tmp_path / "large.bin"
    source.write_bytes(os.urandom(1024 * 1024))
    output = tmp_path / "output"
    output.mkdir()

    peer.start_receiver(cwd=str(output))
    sender = ZmodemSender(peer.io, [str(source)])
    sender.run()

    assert peer.wait(timeout=20) == 0
    assert _sha256(str(output / "large.bin")) == _sha256(str(source))


def test_send_after_delayed_start(peer, tmp_path):
    source = tmp_path / "delayed.bin"
    source.write_bytes(os.urandom(32 * 1024))
    output = tmp_path / "output"
    output.mkdir()

    peer.start_receiver(cwd=str(output))
    import time

    time.sleep(2)
    sender = ZmodemSender(peer.io, [str(source)])
    sender.run()

    assert peer.wait(timeout=20) == 0
    assert _sha256(str(output / "delayed.bin")) == _sha256(str(source))
