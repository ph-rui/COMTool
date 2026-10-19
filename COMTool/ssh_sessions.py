"""Persistence helpers for remembered SSH sessions.

The store keeps connection metadata plus an optional password token that has
already been encrypted by ``secure_store`` (Windows DPAPI).  Passwords are
never written as plaintext; the token is only decrypted in memory when a
session is opened.
"""

import copy
import time


SSH_SESSIONS_KEY = "sshSessions"
SSH_SESSION_PREFIX = "ssh:"
DEFAULT_SSH_PORT = 22


def normalize_host(host):
    """Return the comparison key for an SSH host."""
    return str(host or "").strip().casefold()


def normalize_user(user):
    return str(user or "").strip()


def normalize_ssh_port(port):
    try:
        value = int(port)
    except (TypeError, ValueError):
        value = DEFAULT_SSH_PORT
    if value <= 0 or value > 65535:
        return DEFAULT_SSH_PORT
    return value


def ssh_session_key(host, port, user):
    """Build the stable key used to keep one session per SSH endpoint."""
    return "{}@{}:{}".format(
        normalize_user(user),
        normalize_host(host),
        normalize_ssh_port(port),
    )


def ssh_display_name(session):
    """Primary label shown in the sidebar and as a tab title."""
    alias = str(session.get("alias") or "").strip()
    if alias:
        return alias
    return str(session.get("host") or "").strip()


def ssh_session_detail(session):
    """Secondary label shown under an SSH session row."""
    return "{}@{}:{}".format(
        session.get("user") or "",
        session.get("host") or "",
        session.get("port") or DEFAULT_SSH_PORT,
    )


class SshSessionStore:
    """List of remembered SSH sessions stored in the app config."""

    def __init__(self, config=None):
        self.sessions = []
        if config is not None:
            self.load(config)

    def load(self, config):
        try:
            raw = config[SSH_SESSIONS_KEY]
        except Exception:
            raw = []
        if not isinstance(raw, list):
            raw = []

        loaded = []
        by_key = {}
        for item in raw:
            cleaned = self._clean_item(item)
            if cleaned is None:
                continue
            key = cleaned["key"]
            if key in by_key:
                # Last entry wins, but keep the original position.
                loaded[by_key[key]] = cleaned
            else:
                by_key[key] = len(loaded)
                loaded.append(cleaned)
        self.sessions = loaded

    def save(self, config):
        config[SSH_SESSIONS_KEY] = self.to_list()

    def to_list(self):
        return copy.deepcopy(self.sessions)

    def _clean_item(self, item):
        if not isinstance(item, dict):
            return None
        host = str(item.get("host") or "").strip()
        if not host:
            return None
        port = normalize_ssh_port(item.get("port"))
        user = normalize_user(item.get("user"))
        alias = str(item.get("alias") or item.get("name") or "").strip()
        ssh_key = str(item.get("ssh_key") or "").strip()
        passwd_enc = str(item.get("passwd_enc") or "").strip()
        return {
            "key": ssh_session_key(host, port, user),
            "host": host,
            "port": port,
            "user": user,
            "alias": alias,
            "ssh_key": ssh_key,
            "passwd_enc": passwd_enc,
            "updated": item.get("updated", 0),
        }

    def get(self, key):
        key = str(key or "")
        for session in self.sessions:
            if session["key"] == key:
                return copy.deepcopy(session)
        return None

    def upsert(self, host, port, user, alias="", ssh_key="", passwd_enc=None):
        """Add or update a session.

        ``passwd_enc=None`` keeps the existing encrypted password, which is
        what normal metadata/title updates should do.  Pass an empty string to
        explicitly forget it, or a new token to replace it.
        """
        host = str(host or "").strip()
        if not host:
            return None
        port = normalize_ssh_port(port)
        user = normalize_user(user)
        alias = str(alias or "").strip()
        ssh_key = str(ssh_key or "").strip()
        key = ssh_session_key(host, port, user)
        existing_enc = ""
        existing_index = None
        for index, existing in enumerate(self.sessions):
            if existing["key"] == key:
                existing_enc = str(existing.get("passwd_enc") or "")
                existing_index = index
                break
        if passwd_enc is None:
            passwd_enc = existing_enc
        passwd_enc = str(passwd_enc or "").strip()
        session = {
            "key": key,
            "host": host,
            "port": port,
            "user": user,
            "alias": alias,
            "ssh_key": ssh_key,
            "passwd_enc": passwd_enc,
            "updated": int(time.time()),
        }
        if existing_index is not None:
            self.sessions[existing_index] = session
            return copy.deepcopy(session)
        self.sessions.append(session)
        return copy.deepcopy(session)

    def remove(self, key):
        key = str(key or "")
        for index, session in enumerate(self.sessions):
            if session["key"] == key:
                removed = self.sessions.pop(index)
                return removed["key"]
        return None

    def sorted_sessions(self):
        def sort_key(session):
            return (
                ssh_display_name(session).casefold(),
                session["key"],
            )

        return [copy.deepcopy(session) for session in sorted(self.sessions, key=sort_key)]

    def migrate_items(self, items):
        """Import SSH details from old terminal tabs.

        Only metadata is copied.  Empty SSH configs and entries without a host
        are ignored, so tabs that never used SSH do not create fake sessions.
        """
        if not isinstance(items, list):
            return 0
        before = len(self.sessions)
        seen = set()
        for item in items:
            if not isinstance(item, dict) or item.get("pluginId") != "terminal":
                continue
            config = item.get("config")
            if not isinstance(config, dict):
                continue
            conns = config.get("conns")
            if not isinstance(conns, dict):
                continue
            ssh = conns.get("ssh")
            if not isinstance(ssh, dict):
                continue
            # Every terminal tab gets a default SSH config from SSH.onInit().
            # Only migrate it when SSH was the tab's active connection.
            if conns.get("currConn") == "ssh":
                self._migrate_config(ssh, seen)
            saved = ssh.get("saved")
            if isinstance(saved, list):
                for entry in saved:
                    if isinstance(entry, dict):
                        self._migrate_config(entry, seen)
        return len(self.sessions) - before

    def _migrate_config(self, config, seen):
        host = str(config.get("host") or "").strip()
        if not host:
            return
        key = ssh_session_key(host, config.get("port"), config.get("user"))
        if key in seen:
            return
        seen.add(key)
        self.upsert(
            host,
            config.get("port"),
            config.get("user"),
            alias=config.get("alias") or config.get("name") or "",
            ssh_key=config.get("ssh_key") or "",
        )
