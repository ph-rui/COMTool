from COMTool.serial_sessions import (
    SerialSessionStore,
    normalize_port,
    session_name,
)


def test_load_and_save_round_trip():
    config = {
        "serialSessions": [
            {
                "port": "COM10",
                "name": "COM10  USB Serial",
                "serial": {"port": "COM10", "baudrate": 921600},
            }
        ]
    }
    store = SerialSessionStore(config)
    assert [session["port"] for session in store.sorted_sessions()] == ["COM10"]

    config["serialSessions"] = []
    store.save(config)
    assert config["serialSessions"][0]["serial"]["baudrate"] == 921600


def test_upsert_is_case_insensitive_and_keeps_one_session():
    store = SerialSessionStore()
    store.upsert("COM5", {"port": "COM5", "baudrate": 115200})
    store.upsert("com5", {"port": "com5", "baudrate": 9600})
    sessions = store.sorted_sessions()
    assert len(sessions) == 1
    assert sessions[0]["serial"]["baudrate"] == 9600


def test_prune_only_removes_missing_ports():
    store = SerialSessionStore()
    store.upsert("COM5", {"port": "COM5"})
    store.upsert("COM6", {"port": "COM6"})
    assert store.prune(["COM5"]) == ["COM6"]
    assert store.get("COM5") is not None
    assert store.get("COM6") is None


def test_migrate_terminal_serial_tabs():
    store = SerialSessionStore()
    count = store.migrate_items(
        [
            {
                "name": "terminal",
                "pluginId": "terminal",
                "config": {
                    "conns": {
                        "currConn": "serial",
                        "serial": {"port": "COM99", "baudrate": 115200},
                    }
                },
            },
            {
                "name": "terminal 1",
                "pluginId": "terminal",
                "config": {
                    "conns": {
                        "currConn": "tcp",
                        "serial": {"port": "COM100", "baudrate": 57600},
                    }
                },
            },
            {"name": "graph", "pluginId": "graph", "config": {"conns": {}, "plugin": {}}},
        ]
    )
    assert count == 2
    assert [session["port"] for session in store.sorted_sessions()] == [
        "COM99",
        "COM100",
    ]


def test_update_descriptions_does_not_erase_a_known_label():
    from COMTool.conn.port_utils import PortInfo

    store = SerialSessionStore()
    store.upsert("COM5", {"port": "COM5"}, "COM5  USB Serial")
    assert store.update_descriptions([PortInfo("COM5", "")]) is False
    assert store.get("COM5")["name"] == "COM5  USB Serial"
    assert store.update_descriptions([PortInfo("COM5", "USB Serial CH340 (COM5)")]) is True


def test_natural_sort_and_display_name():
    store = SerialSessionStore()
    store.upsert("COM10", {"port": "COM10"})
    store.upsert("COM2", {"port": "COM2"})
    assert [session["port"] for session in store.sorted_sessions()] == [
        "COM2",
        "COM10",
    ]
    assert session_name("COM5", "USB-SERIAL CH340 (COM5)") == (
        "COM5  USB-SERIAL CH340"
    )
    assert normalize_port("COM5") == "com5"
