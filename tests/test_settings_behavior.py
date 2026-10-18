import pytest

from COMTool.settings_behavior import should_auto_hide_settings


@pytest.mark.parametrize(
    "inside,hover_seen,dx,popup_active,editing,expected",
    [
        (True, True, 10, False, False, False),   # still inside the panel
        (False, False, 10, False, False, False),  # never visited the panel
        (False, True, -5, False, False, False),   # moving left keeps it open
        (False, True, 0, False, False, False),    # vertical/stationary keeps it open
        (False, True, 5, False, False, True),     # moving right outside closes
        (False, True, 5, True, False, False),     # do not close over a popup
        (False, True, 5, False, True, False),     # do not close while editing
    ],
)
def test_should_auto_hide_settings(inside, hover_seen, dx, popup_active, editing, expected):
    assert should_auto_hide_settings(
        inside=inside,
        hover_seen=hover_seen,
        dx=dx,
        popup_active=popup_active,
        editing=editing,
    ) is expected
