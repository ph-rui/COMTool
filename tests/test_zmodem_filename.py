import os

from COMTool.zmodem.filename import (
    FileMeta,
    decode_zmodem_name,
    parse_zfile_metadata,
    sanitize_basename,
    unique_temp_path,
)


def test_decode_utf8_name():
    assert decode_zmodem_name("中文.bin".encode("utf-8")) == "中文.bin"


def test_decode_invalid_bytes_uses_replacement():
    text = decode_zmodem_name(b"\xffabc")
    assert "\udcff" in text
    assert sanitize_basename(text) == "_abc"


def test_sanitize_strips_paths_and_windows_chars():
    assert sanitize_basename("../../etc/passwd") == "passwd"
    assert sanitize_basename("dir\\sub\\file.txt") == "file.txt"
    assert sanitize_basename('a<b>c:d"e|f?g*h') == "a_b_c_d_e_f_g_h"
    assert sanitize_basename("a\x00b\x1fc") == "abc"
    assert sanitize_basename("  name.txt  ") == "name.txt"
    assert sanitize_basename("name.txt.") == "name.txt"


def test_sanitize_empty_and_reserved_names():
    assert sanitize_basename("") == "download"
    assert sanitize_basename(".") == "download"
    assert sanitize_basename("..") == "download"
    assert sanitize_basename("CON") == "CON_"
    assert sanitize_basename("com1.txt") == "com1.txt_"


def test_sanitize_limits_length():
    result = sanitize_basename("a" * 400 + ".bin")
    assert len(result) <= 200
    assert result.endswith(".bin")


def test_unique_temp_path(tmp_path):
    first = unique_temp_path(str(tmp_path), "file.txt")
    assert os.path.basename(first) == "file.txt"
    with open(first, "w") as handle:
        handle.write("x")
    second = unique_temp_path(str(tmp_path), "file.txt")
    assert second != first
    assert os.path.basename(second).startswith("file")
    assert os.path.basename(second).endswith(".txt")


def test_parse_zfile_metadata():
    meta = parse_zfile_metadata(b"file.bin\x00123 1700000000 100644 0")
    assert isinstance(meta, FileMeta)
    assert meta.name == "file.bin"
    assert meta.size == 123
    assert meta.mtime == int("1700000000", 8)
    assert meta.mode == int("100644", 8)
    assert meta.serial == 0


def test_parse_zfile_metadata_name_with_spaces():
    meta = parse_zfile_metadata(b"my file.txt\x0042 0 0 0")
    assert meta.name == "my file.txt"
    assert meta.size == 42


def test_parse_zfile_metadata_missing_size():
    meta = parse_zfile_metadata(b"file.bin\x00")
    assert meta.name == "file.bin"
    assert meta.size is None


def test_parse_zfile_metadata_bad_size():
    meta = parse_zfile_metadata(b"file.bin\x00abc 0 0 0")
    assert meta.name == "file.bin"
    assert meta.size is None
