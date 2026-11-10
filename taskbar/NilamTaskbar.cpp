// NilamTaskbar.dll - the part of Nilam that runs inside Explorer.
//
// Windows 11 draws its taskbar with XAML, inside Explorer, and nothing outside
// that process can change how it looks. So this DLL is loaded into Explorer
// (see Loader.cpp) and uses XAML Diagnostics - the interface Visual Studio's
// Live Visual Tree is built on - to be told about every element in Explorer's
// XAML trees. Of those it keeps three per taskbar:
//
//   TaskbarFrame       the root of one taskbar's content
//   BackgroundFill     a Rectangle under it that paints the taskbar background
//   BackgroundStroke   another that paints the one-pixel line along the top
//
// Nilam.exe decides when a taskbar should be clear and sends a command (see
// Protocol.h) to a message-only window this DLL creates on the taskbar's own
// thread. Clearing swaps the two rectangles' brushes for transparent ones;
// "normal" puts back the exact brushes Windows had set. If Nilam exits or
// crashes, every taskbar is put back without being asked.
//
// Everything that touches XAML runs on the taskbar thread, which is the only
// thread allowed to. The state below is therefore only read or written there,
// and needs no lock.

#include <windows.h>
#include <unknwn.h>     // must come before any C++/WinRT header: it lets winrt::implements host classic COM interfaces
#include <ocidl.h>
#include <xamlOM.h>
#include <windows.ui.xaml.hosting.desktopwindowxamlsource.h>

// windows.h defines GetCurrentTime as a macro, which breaks a XAML method of
// the same name in the C++/WinRT headers.
#undef GetCurrentTime

#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Hosting.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>

#include <cstdarg>
#include <cstdio>
#include <cwchar>
#include <map>
#include <set>
#include <string_view>
#include <thread>

#include "Protocol.h"

namespace wf = winrt::Windows::Foundation;
namespace wux = winrt::Windows::UI::Xaml;

// {74B7498D-2AC5-4FE9-B369-8813F5F0A5D8} - the class XAML Diagnostics creates
// from this DLL. It identifies Nilam's agent and must not be reused elsewhere.
static constexpr CLSID CLSID_NilamTap =
    { 0x74b7498d, 0x2ac5, 0x4fe9, { 0xb3, 0x69, 0x88, 0x13, 0xf5, 0xf0, 0xa5, 0xd8 } };

// Messages the agent window posts to itself.
static constexpr UINT WM_NILAM_REAPPLY = WM_APP + 1;
static constexpr UINT WM_NILAM_CLIENT_GONE = WM_APP + 2;
static constexpr UINT WM_NILAM_DESTROY = WM_APP + 3;

using InitializeXamlDiagnosticsExFn = HRESULT(WINAPI*)(LPCWSTR endPointName, DWORD pid,
    LPCWSTR wszDllXamlDiagnostics, LPCWSTR wszTAPDllName, CLSID tapClsid, LPCWSTR wszInitializationData);

static HMODULE g_module;

// ---------------------------------------------------------------------- trace
// A short log, for finding out why the taskbar did not change on someone's
// machine. Only rare events are written - start-up, taskbars found, failures -
// never anything per window or per command. Kept under 64 KB.
static void Trace(const wchar_t* format, ...)
{
    wchar_t text[600];
    va_list args;
    va_start(args, format);
    vswprintf_s(text, format, args);
    va_end(args);

    SYSTEMTIME now;
    GetLocalTime(&now);
    wchar_t line[700];
    swprintf_s(line, L"%04u-%02u-%02u %02u:%02u:%02u  [%lu] %s\r\n", now.wYear, now.wMonth, now.wDay,
               now.wHour, now.wMinute, now.wSecond, GetCurrentProcessId(), text);

    wchar_t path[MAX_PATH];
    DWORD n = GetTempPathW(MAX_PATH, path);
    if (n == 0 || n > MAX_PATH - 40) return;
    wcscat_s(path, L"Nilam");
    CreateDirectoryW(path, nullptr);
    wcscat_s(path, L"\\taskbar-agent.log");

    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                              OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    LARGE_INTEGER size{};
    if (GetFileSizeEx(file, &size) && size.QuadPart > 64 * 1024)
    {
        CloseHandle(file);
        file = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                           CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
    }
    char utf8[2100];
    int bytes = WideCharToMultiByte(CP_UTF8, 0, line, -1, utf8, sizeof utf8, nullptr, nullptr);
    if (bytes > 1)
    {
        DWORD written;
        WriteFile(file, utf8, static_cast<DWORD>(bytes - 1), &written, nullptr);
    }
    CloseHandle(file);
}

static void SignalReady()
{
    HANDLE ready = OpenEventW(EVENT_MODIFY_STATE, FALSE, NILAM_READY_EVENT);
    if (ready)
    {
        SetEvent(ready);
        CloseHandle(ready);
    }
}

// ---------------------------------------------------------------------- state
struct StyledShape
{
    wux::Shapes::Shape element{ nullptr };
    InstanceHandle handle = 0;
    wux::Media::Brush original{ nullptr };      // what Windows set; put back on "normal"
    int64_t token = 0;                          // the Fill change callback
    bool overridden = false;                    // our brush is on it now
    uint32_t color = 0;                         // ...and this is its colour
};

struct Taskbar
{
    wux::UIElement frame{ nullptr };
    HWND window = nullptr;                      // Shell_TrayWnd or Shell_SecondaryTrayWnd, once known
    StyledShape fill;                           // BackgroundFill
    StyledShape line;                           // BackgroundStroke
    uint32_t flags = 0;                         // what Nilam last asked for
    uint32_t color = 0;
};

// The containers and COM pointers live on the heap and are never freed. This
// DLL stays loaded until Explorer exits, and destructors run then would release
// XAML objects while XAML itself is being torn down.
static winrt::com_ptr<IXamlDiagnostics>& g_diagnostics = *new winrt::com_ptr<IXamlDiagnostics>();
static DWORD g_taskbarThread;
static HWND g_agent;
static bool g_retired;
static bool g_applying;                         // set while we change a Fill ourselves
static std::map<InstanceHandle, Taskbar>& g_taskbars = *new std::map<InstanceHandle, Taskbar>();   // by TaskbarFrame handle
static std::set<InstanceHandle>& g_sources = *new std::set<InstanceHandle>();                      // every DesktopWindowXamlSource seen

static DWORD g_clientPid;
static HANDLE g_clientProcess;
static HANDLE g_clientWait;

template <typename T>
static T FromHandle(InstanceHandle handle)
{
    wf::IInspectable object{ nullptr };
    winrt::check_hresult(g_diagnostics->GetIInspectableFromHandle(
        handle, reinterpret_cast<::IInspectable**>(winrt::put_abi(object))));
    return object.as<T>();
}

// ---------------------------------------------------------------- appearance
static void SetShape(StyledShape& shape, bool styled, uint32_t argb)
{
    if (!shape.element) return;
    if (styled)
    {
        if (shape.overridden && shape.color == argb) return;
        wux::Media::SolidColorBrush brush;
        brush.Color(winrt::Windows::UI::Color{ static_cast<uint8_t>(argb >> 24), static_cast<uint8_t>(argb >> 16),
                                               static_cast<uint8_t>(argb >> 8), static_cast<uint8_t>(argb) });
        shape.element.Fill(brush);
        shape.overridden = true;
        shape.color = argb;
    }
    else if (shape.overridden)
    {
        shape.element.Fill(shape.original);
        shape.overridden = false;
    }
}

static void Apply(Taskbar& taskbar)
{
    bool clear = (taskbar.flags & NilamFlagClear) != 0;
    bool hideLine = clear && (taskbar.flags & NilamFlagHideLine) != 0;
    g_applying = true;
    try
    {
        SetShape(taskbar.fill, clear, taskbar.color);
        SetShape(taskbar.line, hideLine, 0);
    }
    catch (...)
    {
        Trace(L"could not restyle a taskbar: 0x%08X", static_cast<unsigned>(winrt::to_hresult()));
    }
    g_applying = false;
}

static void ApplyAll()
{
    for (auto& entry : g_taskbars) Apply(entry.second);
}

static void RestoreAll()
{
    for (auto& entry : g_taskbars)
    {
        entry.second.flags = 0;
        Apply(entry.second);
    }
}

// Windows sometimes sets a new brush itself - switching between light and dark,
// changing the accent colour. That brush is the new "normal", and if the
// taskbar is meant to be clear, our brush goes back on top of it. Re-applying
// is posted rather than done here: this runs inside the property change.
static void OnFillChanged(InstanceHandle frame, bool isFill)
{
    if (g_applying || g_retired) return;
    auto it = g_taskbars.find(frame);
    if (it == g_taskbars.end()) return;
    StyledShape& shape = isFill ? it->second.fill : it->second.line;
    if (!shape.element) return;
    try
    {
        shape.original = shape.element.Fill();
    }
    catch (...)
    {
        return;
    }
    bool wanted = isFill ? (it->second.flags & NilamFlagClear) != 0
                         : (it->second.flags & (NilamFlagClear | NilamFlagHideLine)) == (NilamFlagClear | NilamFlagHideLine);
    shape.overridden = false;
    if (wanted && g_agent) PostMessageW(g_agent, WM_NILAM_REAPPLY, 0, 0);
}

static void Detach(StyledShape& shape, bool restore)
{
    if (!shape.element) return;
    try
    {
        if (restore && shape.overridden)
        {
            g_applying = true;
            shape.element.Fill(shape.original);
            g_applying = false;
        }
        if (shape.token) shape.element.UnregisterPropertyChangedCallback(wux::Shapes::Shape::FillProperty(), shape.token);
    }
    catch (...)
    {
        g_applying = false;
    }
    shape = StyledShape{};
}

// ------------------------------------------------------------ the visual tree
// A taskbar's content is hosted in a DesktopWindowXamlSource, and that source's
// window handle is what tells us which taskbar - which monitor - it is. The
// frame and the source can arrive in either order, so matching is tried
// whenever either one appears, and again when a command arrives.
static void MatchTaskbars()
{
    for (auto& entry : g_taskbars)
    {
        Taskbar& taskbar = entry.second;
        if (taskbar.window || !taskbar.frame) continue;

        wux::UIElement content{ nullptr };
        try
        {
            wux::XamlRoot root = taskbar.frame.XamlRoot();
            if (!root) continue;
            content = root.Content();
        }
        catch (...)
        {
            continue;
        }
        if (!content) continue;

        for (InstanceHandle sourceHandle : g_sources)
        {
            try
            {
                auto source = FromHandle<wux::Hosting::DesktopWindowXamlSource>(sourceHandle);
                if (source.Content() != content) continue;
                HWND island = nullptr;
                winrt::check_hresult(source.as<IDesktopWindowXamlSourceNative>()->get_WindowHandle(&island));
                taskbar.window = GetAncestor(island, GA_ROOT);
                wchar_t cls[64] = L"";
                GetClassNameW(taskbar.window, cls, 64);
                Trace(L"taskbar %s 0x%08X found", cls, static_cast<unsigned>(reinterpret_cast<uintptr_t>(taskbar.window)));
                Apply(taskbar);
                break;
            }
            catch (...)
            {
                // A source on another thread, or one being torn down.
            }
        }
    }
}

static wux::FrameworkElement FindTaskbarFrame(wux::DependencyObject from)
{
    for (wux::DependencyObject at = from; at; at = wux::Media::VisualTreeHelper::GetParent(at))
    {
        auto element = at.try_as<wux::FrameworkElement>();
        if (element && element.Name() == L"TaskbarFrame") return element;
    }
    return nullptr;
}

static void AttachShape(InstanceHandle parent, InstanceHandle handle, bool isFill)
{
    wux::FrameworkElement frame = FindTaskbarFrame(FromHandle<wux::DependencyObject>(parent));
    if (!frame) return;     // a rectangle of the same name somewhere else in Explorer

    InstanceHandle frameHandle = 0;
    winrt::check_hresult(g_diagnostics->GetHandleFromIInspectable(
        static_cast<::IInspectable*>(winrt::get_abi(frame)), &frameHandle));

    Taskbar& taskbar = g_taskbars[frameHandle];
    StyledShape& shape = isFill ? taskbar.fill : taskbar.line;
    Detach(shape, true);

    shape.element = FromHandle<wux::Shapes::Shape>(handle);
    shape.handle = handle;
    shape.original = shape.element.Fill();
    shape.token = shape.element.RegisterPropertyChangedCallback(wux::Shapes::Shape::FillProperty(),
        [frameHandle, isFill](wux::DependencyObject const&, wux::DependencyProperty const&)
        {
            OnFillChanged(frameHandle, isFill);
        });

    if (!taskbar.frame)
    {
        taskbar.frame = frame;
        MatchTaskbars();
    }
    Apply(taskbar);
}

static void OnRemove(InstanceHandle handle)
{
    if (g_sources.erase(handle)) return;

    auto frame = g_taskbars.find(handle);
    if (frame != g_taskbars.end())
    {
        Detach(frame->second.fill, false);
        Detach(frame->second.line, false);
        g_taskbars.erase(frame);
        return;
    }
    for (auto& entry : g_taskbars)
    {
        // Put the brush back before letting go: if the same element is added
        // again, the brush it has then is taken as Windows' own.
        if (entry.second.fill.handle == handle) { Detach(entry.second.fill, true); return; }
        if (entry.second.line.handle == handle) { Detach(entry.second.line, true); return; }
    }
}

// ------------------------------------------------------------- client watch
// Nilam's own process. When it ends - exit, crash, Task Manager - every
// taskbar goes back to normal, so a clear taskbar is never left behind.
static void CALLBACK ClientGone(void* context, BOOLEAN)
{
    HWND agent = g_agent;
    if (agent) PostMessageW(agent, WM_NILAM_CLIENT_GONE, reinterpret_cast<WPARAM>(context), 0);
}

static void StopWatchingClient()
{
    if (g_clientWait)
    {
        UnregisterWaitEx(g_clientWait, nullptr);
        g_clientWait = nullptr;
    }
    if (g_clientProcess)
    {
        CloseHandle(g_clientProcess);
        g_clientProcess = nullptr;
    }
    g_clientPid = 0;
}

static void WatchClient(DWORD pid)
{
    if (pid == 0 || pid == g_clientPid) return;
    StopWatchingClient();
    g_clientProcess = OpenProcess(SYNCHRONIZE, FALSE, pid);
    if (!g_clientProcess) return;
    g_clientPid = pid;
    if (!RegisterWaitForSingleObject(&g_clientWait, g_clientProcess, ClientGone,
                                     reinterpret_cast<void*>(static_cast<uintptr_t>(pid)), INFINITE, WT_EXECUTEONLYONCE))
    {
        g_clientWait = nullptr;
    }
}

// ----------------------------------------------------------------- the agent
static void Retire();

static LRESULT Handle(const NilamCommand& command)
{
    switch (command.op)
    {
    case NilamOpSet:
    {
        WatchClient(command.clientPid);
        HWND window = reinterpret_cast<HWND>(static_cast<uintptr_t>(command.taskbar));
        for (int pass = 0; pass < 2; ++pass)
        {
            for (auto& entry : g_taskbars)
            {
                Taskbar& taskbar = entry.second;
                if (taskbar.window != window) continue;
                taskbar.flags = command.flags;
                taskbar.color = command.color;
                Apply(taskbar);
                return taskbar.fill.element ? NILAM_RESULT_APPLIED : NILAM_RESULT_NOT_READY;
            }
            MatchTaskbars();
        }
        return NILAM_RESULT_UNKNOWN_TASKBAR;
    }
    case NilamOpRestoreAll:
        RestoreAll();
        return 1;
    case NilamOpRetire:
        Retire();
        return 1;
    }
    return 0;
}

static LRESULT CALLBACK AgentProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    try
    {
        switch (message)
        {
        case WM_COPYDATA:
        {
            auto data = reinterpret_cast<const COPYDATASTRUCT*>(lParam);
            if (!data || data->dwData != NILAM_COPYDATA_MAGIC || data->cbData != sizeof(NilamCommand) || !data->lpData) return 0;
            NilamCommand command;
            memcpy(&command, data->lpData, sizeof command);
            return Handle(command);
        }
        case WM_NILAM_REAPPLY:
            ApplyAll();
            return 0;
        case WM_NILAM_CLIENT_GONE:
            if (static_cast<DWORD>(wParam) == g_clientPid)
            {
                Trace(L"Nilam (process %lu) has gone; taskbars restored", g_clientPid);
                RestoreAll();
                StopWatchingClient();
            }
            return 0;
        case WM_NILAM_DESTROY:
            DestroyWindow(hwnd);
            return 0;
        case WM_DESTROY:
            if (hwnd == g_agent) g_agent = nullptr;
            return 0;
        }
    }
    catch (...)
    {
        Trace(L"agent message 0x%04X failed: 0x%08X", message, static_cast<unsigned>(winrt::to_hresult()));
        return 0;
    }
    return DefWindowProcW(hwnd, message, wParam, lParam);
}

// An agent left by an older Nilam - Explorer keeps a DLL loaded until it
// restarts - is told to stand down, so two agents never style the same taskbar.
static void RetireOthers()
{
    HWND other = nullptr;
    while ((other = FindWindowExW(HWND_MESSAGE, other, nullptr, nullptr)) != nullptr)
    {
        if (other == g_agent) continue;
        DWORD pid = 0;
        GetWindowThreadProcessId(other, &pid);
        if (pid != GetCurrentProcessId()) continue;
        wchar_t cls[64] = L"";
        GetClassNameW(other, cls, 64);
        if (wcsncmp(cls, NILAM_AGENT_CLASS_PREFIX, wcslen(NILAM_AGENT_CLASS_PREFIX)) != 0) continue;
        if (reinterpret_cast<HMODULE>(GetWindowLongPtrW(other, GWLP_HINSTANCE)) == g_module) continue;

        NilamCommand retire{};
        retire.op = NilamOpRetire;
        COPYDATASTRUCT data{ NILAM_COPYDATA_MAGIC, sizeof retire, &retire };
        DWORD_PTR result = 0;
        SendMessageTimeoutW(other, WM_COPYDATA, 0, reinterpret_cast<LPARAM>(&data), SMTO_ABORTIFHUNG, 2000, &result);
        Trace(L"retired an older agent (%s)", cls);
    }
}

static void EnsureAgent()
{
    if (g_agent || g_retired) return;

    WNDCLASSEXW wc{ sizeof wc };
    wc.lpfnWndProc = AgentProc;
    wc.hInstance = g_module;
    wc.lpszClassName = NILAM_AGENT_CLASS;
    RegisterClassExW(&wc);  // fails harmlessly if already registered
    g_agent = CreateWindowExW(0, NILAM_AGENT_CLASS, L"Nilam taskbar agent", 0, 0, 0, 0, 0,
                              HWND_MESSAGE, nullptr, g_module, nullptr);
    if (!g_agent)
    {
        Trace(L"could not create the agent window: %lu", GetLastError());
        return;
    }
    RetireOthers();
}

// ----------------------------------------------------------------- COM: the watcher
struct ElementStrings
{
    // XAML Diagnostics hands over ownership of these.
    explicit ElementStrings(const VisualElement& e)
        : type(e.Type), name(e.Name), file(e.SrcInfo.FileName), hash(e.SrcInfo.Hash) {}
    ~ElementStrings()
    {
        SysFreeString(type);
        SysFreeString(name);
        SysFreeString(file);
        SysFreeString(hash);
    }
    ElementStrings(const ElementStrings&) = delete;
    ElementStrings& operator=(const ElementStrings&) = delete;
    BSTR type, name, file, hash;
};

struct Watcher : winrt::implements<Watcher, IVisualTreeServiceCallback2, winrt::non_agile>
{
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation, VisualElement element,
                                                 VisualMutationType mutation) noexcept override
    {
        ElementStrings strings(element);
        if (g_retired || GetCurrentThreadId() != g_taskbarThread) return S_OK;
        try
        {
            EnsureAgent();
            if (mutation == Add)
            {
                std::wstring_view type = strings.type ? strings.type : L"";
                std::wstring_view name = strings.name ? strings.name : L"";
                if (type == L"Windows.UI.Xaml.Hosting.DesktopWindowXamlSource")
                {
                    g_sources.insert(element.Handle);
                    MatchTaskbars();
                }
                else if (type == L"Taskbar.TaskbarFrame")
                {
                    Taskbar& taskbar = g_taskbars[element.Handle];
                    if (!taskbar.frame) taskbar.frame = FromHandle<wux::UIElement>(element.Handle);
                    MatchTaskbars();
                }
                else if (type == L"Windows.UI.Xaml.Shapes.Rectangle" &&
                         (name == L"BackgroundFill" || name == L"BackgroundStroke"))
                {
                    AttachShape(relation.Parent, element.Handle, name == L"BackgroundFill");
                }
            }
            else if (mutation == Remove)
            {
                OnRemove(element.Handle);
            }
        }
        catch (...)
        {
            Trace(L"visual tree change failed: 0x%08X", static_cast<unsigned>(winrt::to_hresult()));
        }
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) noexcept override
    {
        return S_OK;
    }
};

static winrt::com_ptr<Watcher>& g_watcher = *new winrt::com_ptr<Watcher>();

static void Retire()
{
    if (g_retired) return;
    RestoreAll();
    g_retired = true;
    for (auto& entry : g_taskbars)
    {
        Detach(entry.second.fill, false);
        Detach(entry.second.line, false);
    }
    g_taskbars.clear();
    g_sources.clear();
    StopWatchingClient();
    if (g_watcher && g_diagnostics)
    {
        // Off the taskbar thread, as the advise was: these calls wait on it.
        std::thread([watcher = g_watcher, diagnostics = g_diagnostics]
        {
            try { diagnostics.as<IVisualTreeService3>()->UnadviseVisualTreeChange(watcher.get()); }
            catch (...) {}
        }).detach();
    }
    if (g_agent) PostMessageW(g_agent, WM_NILAM_DESTROY, 0, 0);
    Trace(L"retired");
}

// --------------------------------------------------------------- COM: the TAP
// XAML Diagnostics creates this object and hands it the diagnostics interface
// through SetSite. From then on it reports the visual tree to the watcher.
struct Tap : winrt::implements<Tap, IObjectWithSite, winrt::non_agile>
{
    winrt::com_ptr<IUnknown> site;

    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* unknown) noexcept override
    {
        try
        {
            site.copy_from(unknown);
            if (!site || g_watcher) return S_OK;

            g_diagnostics = site.as<IXamlDiagnostics>();
            g_watcher = winrt::make_self<Watcher>();

            // AdviseVisualTreeChange walks the whole existing tree on the XAML
            // thread before it returns, so calling it from that thread would
            // wait on itself.
            std::thread([watcher = g_watcher, diagnostics = g_diagnostics]
            {
                HRESULT hr = E_FAIL;
                try { hr = diagnostics.as<IVisualTreeService3>()->AdviseVisualTreeChange(watcher.get()); }
                catch (...) { hr = winrt::to_hresult(); }
                Trace(L"watching the visual tree: 0x%08X", static_cast<unsigned>(hr));
                SignalReady();
            }).detach();
            return S_OK;
        }
        catch (...)
        {
            return winrt::to_hresult();
        }
    }

    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid, void** out) noexcept override
    {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (!site) return E_FAIL;
        return site->QueryInterface(iid, out);
    }
};

struct Factory : winrt::implements<Factory, IClassFactory, winrt::non_agile>
{
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** out) noexcept override
    {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        try
        {
            winrt::make_self<Tap>().as(iid, out);
            return S_OK;
        }
        catch (...)
        {
            return winrt::to_hresult();
        }
    }

    HRESULT STDMETHODCALLTYPE LockServer(BOOL) noexcept override
    {
        return S_OK;
    }
};

// ------------------------------------------------------------------ exports
STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** out)
{
    if (!out) return E_POINTER;
    *out = nullptr;
    if (clsid != CLSID_NilamTap) return CLASS_E_CLASSNOTAVAILABLE;
    try
    {
        winrt::make_self<Factory>().as(iid, out);
        return S_OK;
    }
    catch (...)
    {
        return winrt::to_hresult();
    }
}

// Never: Explorer keeps this DLL until it restarts. Unloading it while XAML
// still holds the watcher would leave Explorer calling into freed code.
STDAPI DllCanUnloadNow()
{
    return S_FALSE;
}

// ------------------------------------------------------------------ start-up
// 0: not started, or the last attempt failed. 1: starting. 2: started.
static volatile LONG g_installState;

static DWORD WINAPI Install(void*);

// The loader installs this as a WH_CALLWNDPROC hook on the taskbar thread, so
// that Windows loads the DLL into Explorer - and runs this there. Starting up
// from here rather than from DllMain is what lets a later attempt try again:
// a DLL that is already loaded is not loaded a second time, but every new hook
// runs this once more.
extern "C" LRESULT CALLBACK NilamTaskbarHook(int code, WPARAM wParam, LPARAM lParam)
{
    LONG state = InterlockedCompareExchange(&g_installState, 1, 0);
    if (state == 0)
    {
        HANDLE thread = CreateThread(nullptr, 0, Install, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
        else InterlockedExchange(&g_installState, 0);
    }
    else if (state == 2)
    {
        // Already running here; if the loader is waiting, there is nothing new
        // to wait for.
        SignalReady();
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

static DWORD WINAPI Install(void*)
{
    // Stay loaded after the loader removes its hook.
    HMODULE pinned = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                       reinterpret_cast<LPCWSTR>(&Install), &pinned);

    // Ends an attempt that did not get going; a later hook may try again.
    auto fail = []
    {
        InterlockedExchange(&g_installState, 0);
        SignalReady();
        return 1ul;
    };

    HWND tray = FindWindowW(L"Shell_TrayWnd", nullptr);
    DWORD pid = 0;
    g_taskbarThread = tray ? GetWindowThreadProcessId(tray, &pid) : 0;
    if (!tray || pid != GetCurrentProcessId())
    {
        Trace(L"loaded into an Explorer that does not own the taskbar; doing nothing");
        return fail();
    }

    wchar_t path[MAX_PATH];
    DWORD length = GetModuleFileNameW(g_module, path, MAX_PATH);
    HMODULE xaml = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    auto initialize = xaml ? reinterpret_cast<InitializeXamlDiagnosticsExFn>(
                                 GetProcAddress(xaml, "InitializeXamlDiagnosticsEx")) : nullptr;
    if (length == 0 || length >= MAX_PATH || !initialize)
    {
        Trace(L"XAML Diagnostics is not available here (error %lu)", GetLastError());
        return fail();
    }

    // Another tool may already hold the first connection name, and in the
    // first seconds after Explorer starts XAML Diagnostics answers "not found",
    // so keep trying for half a minute.
    HRESULT hr = E_FAIL;
    for (int attempt = 1; attempt <= 60; ++attempt)
    {
        wchar_t endpoint[40];
        swprintf_s(endpoint, L"VisualDiagConnection%d", attempt);
        // XAML Diagnostics can be set up only once per thread, so each attempt gets its own.
        std::thread([&]
        {
            hr = initialize(endpoint, GetCurrentProcessId(), nullptr, path, CLSID_NilamTap, nullptr);
        }).join();
        if (SUCCEEDED(hr))
        {
            InterlockedExchange(&g_installState, 2);
            Trace(L"agent %S started on %s", __DATE__, endpoint);
            return 0;
        }
        Sleep(500);
    }
    Trace(L"XAML Diagnostics would not start: 0x%08X", static_cast<unsigned>(hr));
    return fail();
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        // Nothing starts here. The loader loads this DLL as well, to find the
        // hook procedure, and only the hook - which runs on Explorer's taskbar
        // thread and nowhere else - sets the agent going.
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}
