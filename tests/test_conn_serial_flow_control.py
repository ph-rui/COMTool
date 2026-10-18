from COMTool.conn.conn_serial import Serial


class FakeCom:
    def __init__(self, xonxoff=False, rtscts=False, dsrdtr=False):
        self.is_open = True
        self.xonxoff = xonxoff
        self.rtscts = rtscts
        self.dsrdtr = dsrdtr


def _connection(fake):
    connection = Serial()
    connection.onInit({})
    connection.com = fake
    return connection


def test_zmodem_flow_control_enables_and_restores_xonxoff():
    fake = FakeCom(xonxoff=False)
    connection = _connection(fake)

    connection.ctrl("zmodem_flow", True)
    assert fake.xonxoff is True

    connection.ctrl("zmodem_flow", False)
    assert fake.xonxoff is False


def test_zmodem_flow_control_restores_previous_setting():
    fake = FakeCom(xonxoff=True)
    connection = _connection(fake)

    connection.ctrl("zmodem_flow", True)
    connection.ctrl("zmodem_flow", False)

    assert fake.xonxoff is True


def test_zmodem_flow_control_keeps_hardware_flow_control():
    fake = FakeCom(xonxoff=False, rtscts=True)
    connection = _connection(fake)

    connection.ctrl("zmodem_flow", True)

    assert fake.xonxoff is False

    connection.ctrl("zmodem_flow", False)
    assert fake.xonxoff is False


def test_zmodem_flow_control_configures_open_port_only():
    fake = FakeCom()
    fake.is_open = False
    connection = _connection(fake)

    connection.ctrl("zmodem_flow", True)

    assert fake.xonxoff is False
