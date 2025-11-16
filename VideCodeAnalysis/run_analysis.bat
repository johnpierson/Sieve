@echo off
REM Batch script to run GLTF analysis
echo Running GLTF Analysis...
echo.
echo Choose an option:
echo 1. Run analysis once
echo 2. Run in watch mode (recheck every 30 seconds)
echo.
set /p choice="Enter choice (1 or 2): "

if "%choice%"=="1" (
    python analyze_gltf.py Live_Export\Result.gltf
    echo.
    echo Analysis complete! Check the output directory for results.
    pause
) else if "%choice%"=="2" (
    python analyze_gltf.py --watch Live_Export\Result.gltf
) else (
    echo Invalid choice. Running analysis once...
    python analyze_gltf.py Live_Export\Result.gltf
    echo.
    echo Analysis complete! Check the output directory for results.
    pause
)

