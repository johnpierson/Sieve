@echo off
REM Run analysis and start server
echo GLTF Analysis and Viewer
echo.
echo Choose an option:
echo 1. Run analysis once, then start server
echo 2. Run analysis in watch mode (background), then start server
echo.
set /p choice="Enter choice (1 or 2): "

if "%choice%"=="1" goto option1
if "%choice%"=="2" goto option2
goto default

:option1
echo.
echo Running GLTF Analysis...
python analyze_gltf.py Live_Export\Result.gltf
echo.
echo Analysis complete! Starting web server...
echo The server will try port 8000, or use the next available port if 8000 is in use.
echo Press Ctrl+C to stop the server
echo.
python server.py 8000
goto end

:option2
echo.
echo Starting analysis in watch mode (background)...
echo A new window will open for the analysis watch process.
echo The window will stay open and show continuous analysis updates.
echo.
REM Start in a new window that stays open (cmd /k keeps window open)
start "GLTF Analysis Watch" cmd /k "python analyze_gltf.py --watch Live_Export\Result.gltf"
timeout /t 3 /nobreak >nul
echo.
echo Starting web server...
echo The server will try port 8000, or use the next available port if 8000 is in use.
echo The analysis will continue running in the background window.
echo Press Ctrl+C to stop the server
echo.
python server.py 8000
goto end

:default
echo Invalid choice. Running analysis once, then starting server...
echo.
python analyze_gltf.py Live_Export\Result.gltf
echo.
echo Starting web server...
echo The server will try port 8000, or use the next available port if 8000 is in use.
echo Press Ctrl+C to stop the server
echo.
python server.py 8000
goto end

:end

