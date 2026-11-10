// NilamTaskbarLoader.exe - puts NilamTaskbar.dll into Explorer, then exits.
//
//     NilamTaskbarLoader.exe "<full path of NilamTaskbar.dll>"
//
// Nilam.exe is a 32-bit process and Explorer a 64-bit one, and a process can
// only hook another of its own kind - so this small 64-bit program does it for
// Nilam and is gone again within a few seconds.
//
// It installs a WH_CALLWNDPROC hook on the thread that owns the taskbar and
// sends that thread one harmless message. Windows loads the hook's DLL into
// Explorer to run it; the DLL pins itself there, starts its agent (see
// NilamTaskbar.cpp) and sets NILAM_READY_EVENT. The hook is then removed.
//
// Exit codes, which Nilam writes to its log:
//   0  the agent is running in Explorer (or already was)
//   1  the DLL loaded but the agent did not start - see %TEMP%\Nilam\taskbar-agent.log
//   2  wrong arguments
//   3  no taskbar - Explorer is not running
//   4  the DLL could not be loaded here
//   5  the hook could not be installed
//   6  Explorer did not answer within 40 seconds

#include <windows.h>
#include <shellapi.h>

#include "Protocol.h"

static bool AgentRunning()
{
    return FindWindowExW(HWND_MESSAGE, nullptr, NILAM_AGENT_CLASS, nullptr) != nullptr;
}

static int Run(const wchar_t* dllPath)
{
    if (AgentRunning()) return 0;

    HWND tray = FindWindowW(L"Shell_TrayWnd", nullptr);
    DWORD thread = tray ? GetWindowThreadProcessId(tray, nullptr) : 0;
    if (!thread) return 3;

    HANDLE ready = CreateEventW(nullptr, TRUE, FALSE, NILAM_READY_EVENT);
    if (!ready) return 4;
    ResetEvent(ready);

    HMODULE dll = LoadLibraryExW(dllPath, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    HOOKPROC proc = dll ? reinterpret_cast<HOOKPROC>(GetProcAddress(dll, "NilamTaskbarHook")) : nullptr;
    if (!proc)
    {
        CloseHandle(ready);
        return 4;
    }

    HHOOK hook = SetWindowsHookExW(WH_CALLWNDPROC, proc, dll, thread);
    if (!hook)
    {
        CloseHandle(ready);
        return 5;
    }
    // A sent message passes through the hook, which is what makes Windows load
    // the DLL there. SendNotifyMessage returns at once rather than waiting on
    // Explorer.
    SendNotifyMessageW(tray, WM_NULL, 0, 0);
    DWORD waited = WaitForSingleObject(ready, 40000);
    UnhookWindowsHookEx(hook);
    CloseHandle(ready);

    // The agent window is created on the taskbar thread, which may be a moment
    // behind the ready signal.
    for (int i = 0; i < 50; ++i)
    {
        if (AgentRunning()) return 0;
        Sleep(100);
    }
    return waited == WAIT_TIMEOUT ? 6 : 1;
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    int argc = 0;
    wchar_t** argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    int code = (argv && argc == 2) ? Run(argv[1]) : 2;
    if (argv) LocalFree(argv);
    return code;
}
