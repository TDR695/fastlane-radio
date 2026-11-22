@echo off
rem Builds FastLaneRadio.exe with the C# compiler that comes with Windows 10/11 - nothing to install.
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set REFS=
for %%f in ("%WINDIR%\System32\WinMetadata\*.winmd") do call set REFS=%%REFS%% -r:"%%~f"

rem csc fails with "Cannot create temporary file" when TEMP is a very long path
set TMP=%~dp0build
set TEMP=%~dp0build
if not exist "%TMP%" mkdir "%TMP%"

"%FW%\csc.exe" -nologo -target:winexe -optimize+ -out:"%~dp0FastLaneRadio.exe" %REFS% ^
  -win32icon:"%~dp0FastLaneRadio.ico" -resource:"%~dp0FastLaneRadio.ico",FastLaneRadio.ico ^
  -r:"%FW%\System.Runtime.dll" -r:"%FW%\System.Runtime.WindowsRuntime.dll" ^
  -r:"%FW%\System.Runtime.InteropServices.WindowsRuntime.dll" -r:System.Web.Extensions.dll ^
  -r:"%FW%\WPF\PresentationFramework.dll" -r:"%FW%\WPF\PresentationCore.dll" ^
  -r:"%FW%\WPF\WindowsBase.dll" -r:"%FW%\System.Xaml.dll" -r:System.Windows.Forms.dll -r:System.Drawing.dll ^
  "%~dp0Radio.cs" "%~dp0RadioWindow.cs" "%~dp0RadioLogos.cs" "%~dp0RadioTray.cs" "%~dp0AssemblyInfo.cs"
