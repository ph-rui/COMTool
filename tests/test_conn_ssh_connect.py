"""SSH connection parameters that affect speed and terminal colors."""

import os
import sys

import pytest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

pytest.importorskip("PyQt5")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

import paramiko  # noqa: E402
from PyQt5.QtWidgets import QApplication  # noqa: E402

import COMTool.conn.conn_ssh as conn_ssh  # noqa: E402
from COMTool.conn.conn_ssh import SSH, SSH_CONN  # noqa: E402


@pytest.fixture(scope="module")
def app():
    return QApplication.instance() or QApplication([])


class FakeChannel:
    def __init__(self):
        self.pty = None
        self.env = {}
        self.shell = False

    def get_pty(self, **kwargs):
        self.pty = kwargs

    def set_environment_variable(self, name, value):
        self.env[name] = value

    def invoke_shell(self):
        self.shell = True


class FakeTransport:
    def __init__(self, channel):
        self.channel = channel

    def open_session(self):
        return self.channel


class FakeClient:
    last = None

    def __init__(self):
        FakeClient.last = self
        self.kwargs = None
        self.channel = FakeChannel()

    def set_missing_host_key_policy(self, policy):
        self.policy = policy

    def connect(self, host, **kwargs):
        self.host = host
        self.kwargs = kwargs

    def get_transport(self):
        return FakeTransport(self.channel)


@pytest.fixture
def fake_paramiko(monkeypatch):
    monkeypatch.setattr(paramiko, "SSHClient", FakeClient)
    return FakeClient


def test_password_auth_skips_key_and_agent_scan(fake_paramiko):
    conn = SSH_CONN()
    conn.connect("10.0.0.5", 22, "root", "secret")
    client = fake_paramiko.last
    assert client.kwargs["look_for_keys"] is False
    assert client.kwargs["allow_agent"] is False
    assert client.kwargs["timeout"] == 10
    assert client.channel.pty["term"] == "xterm-256color"
    assert client.channel.env["COLORTERM"] == "truecolor"
    assert client.channel.env["COLORFGBG"] == "15;0"
    assert client.channel.shell is True


def test_key_auth_skips_key_and_agent_scan(fake_paramiko):
    conn = SSH_CONN()
    conn.connect("10.0.0.5", 22, "root", "", ssh_key_file="/tmp/key")
    client = fake_paramiko.last
    assert client.kwargs["key_filename"] == "/tmp/key"
    assert client.kwargs["look_for_keys"] is False
    assert client.kwargs["allow_agent"] is False


def test_no_explicit_auth_keeps_agent_and_keys(fake_paramiko):
    conn = SSH_CONN()
    conn.connect("10.0.0.5", 22, "root", "")
    client = fake_paramiko.last
    assert client.kwargs["look_for_keys"] is True
    assert client.kwargs["allow_agent"] is True


class FakeSSHConn:
    last = None

    def __init__(self):
        FakeSSHConn.last = self
        self.password = None

    def connect(self, host, port, user, password, ssh_key_file=None):
        self.password = password


def test_pending_sidebar_password_is_one_shot_and_not_persisted(app, monkeypatch):
    monkeypatch.setattr(conn_ssh, "SSH_CONN", FakeSSHConn)
    config = {
        "host": "10.0.0.5",
        "port": 22,
        "user": "root",
        "passwd": "",
        "ssh_key": "",
    }
    ssh = SSH()
    ssh.onInit(config)
    ssh._pending_password = "secret"
    ssh.checkAndConnect()

    assert FakeSSHConn.last.password == "secret"
    assert config["passwd"] == ""
    assert ssh._pending_password is None
