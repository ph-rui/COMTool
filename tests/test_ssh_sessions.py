import os
import sys

import pytest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from COMTool.conn.conn_ssh import SSH
from COMTool.ssh_sessions import (
    SshSessionStore,
    ssh_display_name,
    ssh_session_detail,
    ssh_session_key,
)


def test_load_and_save_round_trip():
    config = {
        "sshSessions": [
            {
                "host": "192.168.1.10",
                "port": 22,
                "user": "root",
                "alias": "lab",
                "ssh_key": "/home/me/.ssh/id_rsa",
            }
        ]
    }
    store = SshSessionStore(config)
    session = store.sorted_sessions()[0]
    assert session["key"] == "root@192.168.1.10:22"
    assert ssh_display_name(session) == "lab"
    assert ssh_session_detail(session) == "root@192.168.1.10:22"

    config["sshSessions"] = []
    store.save(config)
    assert config["sshSessions"][0]["ssh_key"] == "/home/me/.ssh/id_rsa"


def test_upsert_keeps_one_session_per_endpoint():
    store = SshSessionStore()
    store.upsert("Example.COM", 22, "root", alias="one")
    store.upsert("example.com", "22", "root", alias="two")
    store.upsert("example.com", 2222, "root", alias="other port")
    store.upsert("example.com", 22, "admin", alias="other user")
    sessions = store.sorted_sessions()
    assert len(sessions) == 3
    assert store.get(ssh_session_key("example.com", 22, "root"))["alias"] == "two"


def test_alias_defaults_to_host_and_detail_keeps_user():
    store = SshSessionStore()
    store.upsert("10.0.0.5", 22, "root")
    session = store.sorted_sessions()[0]
    assert ssh_display_name(session) == "10.0.0.5"
    assert ssh_session_detail(session) == "root@10.0.0.5:22"


def test_migrate_terminal_tabs_and_saved_entries():
    store = SshSessionStore()
    count = store.migrate_items(
        [
            {
                "name": "terminal",
                "pluginId": "terminal",
                "config": {
                    "conns": {
                        "currConn": "ssh",
                        "ssh": {
                            "name": "board",
                            "host": "172.16.66.10",
                            "port": 22,
                            "user": "root",
                            "passwd": "secret",
                            "ssh_key": "",
                            "saved": [
                                {
                                    "host": "172.16.66.11",
                                    "port": 2222,
                                    "user": "debug",
                                    "ssh_key": "/tmp/key",
                                }
                            ],
                        },
                    }
                },
            },
            {
                "name": "terminal 1",
                "pluginId": "terminal",
                "config": {
                    "conns": {
                        "currConn": "serial",
                        "ssh": {"host": "127.0.0.1", "port": 22, "user": "root"},
                    }
                },
            },
            {"name": "graph", "pluginId": "graph", "config": {}},
        ]
    )
    assert count == 2
    assert {session["key"] for session in store.sorted_sessions()} == {
        "root@172.16.66.10:22",
        "debug@172.16.66.11:2222",
    }
    assert store.get("root@172.16.66.10:22")["alias"] == "board"
    assert "passwd" not in store.to_list()[0]


def test_password_token_is_preserved_until_explicitly_cleared():
    store = SshSessionStore()
    store.upsert("host", 22, "root", passwd_enc="encrypted-token")
    store.upsert("host", 22, "root", alias="renamed")
    session = store.get("root@host:22")
    assert session["alias"] == "renamed"
    assert session["passwd_enc"] == "encrypted-token"

    store.upsert("host", 22, "root", passwd_enc="")
    assert store.get("root@host:22")["passwd_enc"] == ""


def test_remove_and_invalid_entries():
    store = SshSessionStore(
        {
            "sshSessions": [
                {"host": "", "port": 22, "user": "root"},
                {"host": "host", "port": "bad", "user": "root"},
            ]
        }
    )
    assert len(store.to_list()) == 1
    assert store.to_list()[0]["port"] == 22
    key = store.to_list()[0]["key"]
    assert store.remove(key) == key
    assert store.to_list() == []


@pytest.mark.parametrize(
    "port, expected",
    [(None, 22), ("", 22), ("0", 22), ("70000", 22), ("2222", 2222)],
)
def test_port_normalization(port, expected):
    store = SshSessionStore()
    store.upsert("host", port, "root")
    assert store.to_list()[0]["port"] == expected


def test_ssh_tab_name_uses_alias_then_host():
    conn = SSH()
    conn.onInit(
        {
            "name": "",
            "host": "192.168.10.20",
            "port": 22,
            "user": "root",
        }
    )
    assert conn.getDisplayName() == "192.168.10.20"
    assert conn.getDisplayDetail() == "root@192.168.10.20:22"

    conn.config["name"] = "target-board"
    assert conn.getDisplayName() == "target-board"
