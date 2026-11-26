using System;

namespace Iciclecreek.Terminal;

[Flags]
public enum Win32ControlKeyState
{
	None = 0,
	RightAltPressed = 1,
	LeftAltPressed = 2,
	RightCtrlPressed = 4,
	LeftCtrlPressed = 8,
	ShiftPressed = 0x10,
	NumLockOn = 0x20,
	ScrollLockOn = 0x40,
	CapsLockOn = 0x80,
	EnhancedKey = 0x100
}
