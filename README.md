# iLand

An Apple Dynamic Island-inspired desktop widget for Windows 10/11, built with WPF. It provides an animated capsule, clock/date, and hardware monitoring where supported. This is an independent visual homage, not Apple software or an exact copy of iOS/macOS behavior.

## Download Windows builds

After the GitHub Actions workflow completes successfully, open the repository's Actions tab, select the latest Build iLand run, and download iLand-win-x64 (64-bit Windows) or iLand-win-x86 (32-bit Windows) from the Artifacts section. Extract the ZIP and run iLand.exe.

Artifacts are downloadable from their workflow run and are not permanent release assets. Windows may show a SmartScreen warning because the application is unsigned. The program is a Windows desktop app, not a browser extension.

## Build locally

Requires the .NET 8 SDK on Windows.

```powershell
dotnet restore iLand.csproj
dotnet publish iLand.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PlatformTarget=x64 -o publish/win-x64
dotnet publish iLand.csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PlatformTarget=x86 -o publish/win-x86
