@echo off
REM Batch script to run GLTF analysis in watch mode
echo ========================================
echo GLTF Analysis - Watch Mode
echo ========================================
echo.
echo This will continuously monitor and re-analyze the file every 5 seconds.
echo The window will stay open and show analysis progress.
echo Press Ctrl+C to stop.
echo.
echo ========================================
echo.

REM Run the analysis in watch mode
REM Note: The script will wait for the file to be created if it doesn't exist yet
python analyze_gltf.py --watch Live_Export\Result.gltf

REM If we get here, the script has exited (shouldn't happen in watch mode)
echo.
echo Analysis watch mode has stopped.
echo.
pause

