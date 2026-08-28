@echo off
rem Tezuri launcher: prepare what is missing, then open the desktop app.
rem Installs locked frontend dependencies on first use. Every launch wipes
rem the previous interface bundle and rebuilds it from source, so the app
rem never serves a stale, half-written, or half-deleted dist — a failed
rem build leaves nothing behind to be remembered forever.
setlocal
cd /d "%~dp0"

where node >nul 2>nul || goto :missing-node
where npm >nul 2>nul || goto :missing-node
where cargo >nul 2>nul || goto :missing-cargo

if not exist "src-tauri\ui\package.json" goto :incomplete-checkout

if not exist "src-tauri\ui\node_modules" (
    echo Installing locked frontend dependencies...
    pushd src-tauri\ui
    call npm ci --no-fund --no-audit || goto :prepare-failed
    popd
)

echo Building the interface bundle...
pushd src-tauri\ui
if exist dist rmdir /s /q dist
call npm run build || goto :prepare-failed
if not exist "dist\index.html" goto :prepare-failed
if not exist "dist\tezuri-editor.js" goto :prepare-failed
popd

echo Starting Tezuri...
cargo run --release -p tezuri-desktop
goto :eof

:prepare-failed
popd
echo.
echo Tezuri: preparing the interface failed. Fix the npm error above,
echo then run launch.bat again.
pause
goto :eof

:incomplete-checkout
echo.
echo Tezuri's frontend folder ^(src-tauri\ui^) is missing or incomplete.
echo This launcher must be run from the repository root.
pause
goto :eof

:missing-node
echo.
echo Tezuri needs Node.js ^(20+^) on PATH to prepare its interface.
echo Install it from https://nodejs.org and run launch.bat again.
pause
goto :eof

:missing-cargo
echo.
echo Tezuri needs Rust ^(https://rustup.rs^) on PATH.
echo Install the toolchain and run launch.bat again.
pause
goto :eof
