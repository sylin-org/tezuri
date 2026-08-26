@echo off
rem Tezuri launcher: prepare what is missing, then open the desktop app.
rem Installs locked frontend dependencies and builds the bundle the first
rem time only; every later run starts straight away.
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

if not exist "src-tauri\ui\dist\index.html" (
    echo Building the interface bundle ^(first run^)...
    pushd src-tauri\ui
    call npm run build || goto :prepare-failed
    popd
)

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
