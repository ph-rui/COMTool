import hashlib
import os

import pytest

from COMTool.zmodem.receiver import ZmodemReceiver
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


def test_receive_single_file_from_lsz(peer, tmp_path):
    source = tmp_path / "source.bin"
    source.write_bytes(os.urandom(256 * 1024))
    output = tmp_path / "output"
    output.mkdir()

    peer.start_sender([str(source)], cwd=str(tmp_path))
    receiver = ZmodemReceiver(peer.io, str(output))
    files = receiver.run()

    assert len(files) == 1
    assert files[0].name == "source.bin"
    assert files[0].size == source.stat().st_size
    assert _sha256(files[0].path) == _sha256(str(source))
    assert peer.wait(timeout=20) == 0


def test_receive_multiple_files_from_lsz(peer, tmp_path):
    first = tmp_path / "first file.bin"
    second = tmp_path / "second.bin"
    first.write_bytes(os.urandom(64 * 1024))
    second.write_bytes(os.urandom(96 * 1024))
    output = tmp_path / "output"
    output.mkdir()

    peer.start_sender([str(first), str(second)], cwd=str(tmp_path))
    receiver = ZmodemReceiver(peer.io, str(output))
    files = receiver.run()

    assert [item.name for item in files] == ["first file.bin", "second.bin"]
    assert _sha256(files[0].path) == _sha256(str(first))
    assert _sha256(files[1].path) == _sha256(str(second))
    assert peer.wait(timeout=20) == 0


def test_receive_one_megabyte_from_lsz(peer, tmp_path):
    source = tmp_path / "large.bin"
    source.write_bytes(os.urandom(1024 * 1024))
    output = tmp_path / "output"
    output.mkdir()

    peer.start_sender([str(source)], cwd=str(tmp_path))
    receiver = ZmodemReceiver(peer.io, str(output))
    files = receiver.run()

    assert len(files) == 1
    assert _sha256(files[0].path) == _sha256(str(source))
    assert peer.wait(timeout=20) == 0


def test_receive_after_delayed_start(peer, tmp_path):
    source = tmp_path / "delayed.bin"
    source.write_bytes(os.urandom(32 * 1024))
    output = tmp_path / "output"
    output.mkdir()

    peer.start_sender([str(source)], cwd=str(tmp_path))
    import time

    time.sleep(2)
    receiver = ZmodemReceiver(peer.io, str(output))
    files = receiver.run()

    assert len(files) == 1
    assert _sha256(files[0].path) == _sha256(str(source))
    assert peer.wait(timeout=20) == 0
