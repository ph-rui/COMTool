"""Pure helpers for main-window UI behavior."""


def should_auto_hide_settings(inside, hover_seen, dx, popup_active=False, editing=False):
    """Return True when an open settings panel should collapse.

    The panel follows the pointer's horizontal direction: moving left (or
    staying still) keeps it open; moving right after leaving the panel closes
    it.  Do not close while a popup or an editing widget is active.
    """
    return bool(hover_seen and not inside and dx > 0 and not popup_active and not editing)
