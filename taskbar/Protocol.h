// What Nilam.exe and the taskbar agent inside Explorer say to each other.
//
// The same names and numbers are written out in src\TaskbarStyle.cs, which is
// C# and cannot include this file. Change both together, and when the layout of
// NilamCommand or the meaning of an operation changes, raise the protocol
// number: it is part of the agent's window class name, so a Nilam that speaks
// one version never sends commands to an agent that speaks another.
#pragma once

#include <stdint.h>

#define NILAM_TASKBAR_PROTOCOL 1

// The agent is a message-only window created on Explorer's taskbar thread.
// Nilam finds it with FindWindowEx(HWND_MESSAGE, ...) and talks to it with
// WM_COPYDATA, which Windows copies across the process boundary - and between
// Nilam's 32-bit process and Explorer's 64-bit one.
#define NILAM_AGENT_CLASS L"NilamTaskbarAgent.1"
#define NILAM_AGENT_CLASS_PREFIX L"NilamTaskbarAgent."

// The loader creates this event and waits on it; the agent sets it once it has
// finished starting up, whether or not that worked.
#define NILAM_READY_EVENT L"Local\\NilamTaskbarReady"

// COPYDATASTRUCT.dwData of every command, so a stray WM_COPYDATA is ignored.
#define NILAM_COPYDATA_MAGIC 0x4E4C5442u    // "NLTB"

enum NilamOp : uint32_t
{
    // Style one taskbar: clear (with the colour and line choice) or back to normal.
    NilamOpSet = 1,
    // Put every taskbar back as Windows drew it.
    NilamOpRestoreAll = 2,
    // Sent by a newer agent to an older one: restore everything and stop.
    NilamOpRetire = 3,
};

enum NilamFlags : uint32_t
{
    NilamFlagClear = 1,         // replace the background with `color`
    NilamFlagHideLine = 2,      // and hide the one-pixel line along its top edge
};

#pragma pack(push, 4)
struct NilamCommand
{
    uint32_t op;
    uint32_t taskbar;           // Shell_TrayWnd or Shell_SecondaryTrayWnd. Window handles
                                // are 32 bits wide in every process, 32- or 64-bit.
    uint32_t flags;
    uint32_t color;             // 0xAARRGGBB; 0 is fully transparent
    uint32_t clientPid;         // when this process ends, every taskbar is restored
};
#pragma pack(pop)

// What SendMessage(WM_COPYDATA) returns for NilamOpSet.
#define NILAM_RESULT_UNKNOWN_TASKBAR 0      // no taskbar with that handle has been seen
#define NILAM_RESULT_APPLIED 1
#define NILAM_RESULT_NOT_READY 2            // the taskbar is known, its background not yet
