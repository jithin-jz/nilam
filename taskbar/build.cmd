@echo off
rem Builds the two 64-bit parts of the clear taskbar:
rem
rem   bin\taskbar\NilamTaskbar.dll         runs inside Explorer
rem   bin\taskbar\NilamTaskbarLoader.exe   puts it there
rem
rem Unlike the rest of Nilam these need Microsoft's C++ compiler - Visual Studio,
rem or the free Build Tools, with the "Desktop development with C++" workload.
rem People installing Nilam never run this: a release carries the two files
rem prebuilt, and the installer downloads and checks them.
rem
rem The same quoting rules as ..\build.cmd apply: every `set` is in the
rem set "NAME=value" form, and no path is expanded inside a parenthesised block.

setlocal
set "HERE=%~dp0"
set "OUT=%HERE%..\bin\taskbar"
set "OBJ=%HERE%..\bin\obj\taskbar"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"

if not exist "%VSWHERE%" goto :novs
if not exist "%OUT%" mkdir "%OUT%"
if not exist "%OBJ%" mkdir "%OBJ%"

rem vswhere's answer goes through a file: a `for /f` over a quoted command
rem whose path holds "(x86)" is exactly the parsing trap described above.
"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath > "%OBJ%\vs.txt"
set "VS="
set /p VS=<"%OBJ%\vs.txt"
if "%VS%"=="" goto :novs
set "VCVARS=%VS%\VC\Auxiliary\Build\vcvarsall.bat"
if not exist "%VCVARS%" goto :novs

rem vcvarsall looks for vswhere on PATH as well and complains when it is not
rem there; the complaint is harmless, so its output is discarded.
call "%VCVARS%" x64 >nul 2>&1
if errorlevel 1 goto :failed

rem cl.exe takes extra options from the CL environment variable.
rem /MT links the C runtime in statically. The DLL runs inside Explorer on
rem machines that may not have the Visual C++ runtime installed, and must not
rem depend on it.
set "CL=/nologo /std:c++20 /EHsc /O2 /MT /W4 /permissive- /utf-8 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX"

cl /LD "%HERE%NilamTaskbar.cpp" /Fo"%OBJ%/" /Fe"%OUT%\NilamTaskbar.dll" /link /DEF:"%HERE%NilamTaskbar.def" /IMPLIB:"%OBJ%\NilamTaskbar.lib" user32.lib ole32.lib oleaut32.lib windowsapp.lib
if errorlevel 1 goto :failed

cl "%HERE%Loader.cpp" /Fo"%OBJ%/" /Fe"%OUT%\NilamTaskbarLoader.exe" /link /SUBSYSTEM:WINDOWS user32.lib shell32.lib
if errorlevel 1 goto :failed

echo TASKBAR BUILD OK
exit /b 0

:failed
echo TASKBAR BUILD FAILED
exit /b 1

:novs
echo The Visual Studio C++ compiler was not found.
echo Install Visual Studio or the Build Tools with "Desktop development with C++"
echo to build the clear taskbar. The rest of Nilam builds without it.
exit /b 1
