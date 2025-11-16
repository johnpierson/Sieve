# GLTF Analysis and Viewer System

A comprehensive system for analyzing GLTF files, detecting spatial issues, and visualizing results in a web-based 3D viewer.

## Overview

This system performs automated analysis on GLTF model files to detect:
1. **Clash Detection**: Identifies objects with intersecting bounding boxes
2. **Wall Spacing**: Checks for walls that are less than 8 units apart
3. **Door Distance**: Validates that doors are more than 6 units away from perpendicular walls

Issues are color-coded in the output GLTF file and can be viewed in a real-time web-based 3D viewer that automatically reloads when new analysis results are available.

## Features

- **Automated Analysis**: Python script analyzes GLTF files and detects spatial issues
- **Watch Mode**: Continuously monitor and re-analyze files every 30 seconds (configurable)
- **Color-Coded Results**: Different colors for different issue types:
  - 🔴 **Red**: Clash detection (intersecting objects)
  - 🟠 **Orange**: Wall spacing issues (walls < 8 units apart)
  - 🟡 **Yellow**: Door distance issues (doors > 6 units from perpendicular walls)
- **Timestamped Output**: Each analysis creates a timestamped copy of the result
- **Web-Based Viewer**: Interactive 3D viewer using Three.js
- **Auto-Reload**: Viewer automatically detects and loads new analysis results
- **Simple Web Server**: Built-in HTTP server for serving files

## Requirements

- Python 3.7 or higher
- Modern web browser with WebGL support (Chrome, Firefox, Edge, Safari)
- Internet connection (for loading Three.js from CDN)

## Installation

1. Clone or download this repository
2. Ensure Python 3.7+ is installed
3. No additional Python packages are required (uses only standard library)

## Usage

### Step 1: Analyze a GLTF File

#### Single Analysis (Run Once)

Run the analysis script on your GLTF file:

```bash
python analyze_gltf.py [path_to_gltf_file]
```

If no path is provided, it defaults to `Live_Export/Result.gltf`.

**Example:**
```bash
python analyze_gltf.py Live_Export/Result.gltf
```

The script will:
- Copy the GLTF file and associated .bin files to the `output` directory
- Perform all three types of analysis
- Apply color-coding to problematic elements
- Save a timestamped copy (e.g., `Result_analyzed_20240101_143022.gltf`)

#### Continuous Monitoring (Watch Mode)

To continuously monitor the GLTF file and re-analyze it every 30 seconds:

```bash
python analyze_gltf.py --watch [path_to_gltf_file]
```

**Options:**
- `--watch` or `-w`: Enable watch mode (continuous monitoring)
- `--interval SECONDS` or `-i SECONDS`: Set the recheck interval in seconds (default: 30)

**Examples:**
```bash
# Watch mode with default 30-second interval
python analyze_gltf.py --watch Live_Export/Result.gltf

# Watch mode with custom 60-second interval
python analyze_gltf.py --watch --interval 60 Live_Export/Result.gltf

# Short form
python analyze_gltf.py -w -i 15 Live_Export/Result.gltf
```

**Watch Mode Features:**
- Automatically detects when the source file is modified
- Re-runs analysis immediately when changes are detected
- Also rechecks at regular intervals (default: every 30 seconds)
- Runs continuously until stopped with Ctrl+C
- Creates a new timestamped output file each time analysis runs

**Note:** When running in watch mode, the script will:
1. Perform an initial analysis immediately
2. Monitor the file modification time
3. Re-analyze whenever the file changes OR every 30 seconds (whichever comes first)
4. Continue until you press Ctrl+C

### Step 2: Start the Web Server

Start the web server to serve the viewer and analysis results:

```bash
python server.py [port]
```

If no port is specified, it defaults to port 8000.

**Example:**
```bash
python server.py 8000
```

### Step 3: Open the Viewer

Open your web browser and navigate to:

```
http://localhost:8000/viewer.html
```

The viewer will automatically:
- Load the latest analyzed GLTF file
- Display the 3D model with color-coded issues
- Check for new files every 2 seconds and reload automatically

## Analysis Details

### 1. Clash Detection

**What it does:**
- Checks all objects in the GLTF file for intersecting bounding boxes
- Two objects are considered to clash if their bounding boxes overlap in 3D space

**Color:** Red (RGB: 1.0, 0.0, 0.0)

**When it triggers:**
- Any two objects have overlapping bounding boxes

### 2. Wall Spacing Check

**What it does:**
- Identifies all walls in the model (based on node names containing "wall")
- Calculates the distance between each pair of walls
- Flags walls that are less than 8 units apart

**Color:** Orange (RGB: 1.0, 0.5, 0.0)

**When it triggers:**
- Two walls are between 0 and 8 units apart (non-intersecting)

**Threshold:** 8.0 units (configurable in `analyze_gltf.py`)

### 3. Door Distance Check

**What it does:**
- Identifies all doors in the model (based on node names containing "door")
- For each door, checks the distance to all walls
- Flags doors that are more than 6 units away from perpendicular walls

**Color:** Yellow (RGB: 1.0, 1.0, 0.0)

**When it triggers:**
- A door is more than 6 units away from a wall that is perpendicular to it

**Threshold:** 6.0 units (configurable in `analyze_gltf.py`)

## File Structure

```
.
├── analyze_gltf.py          # Main analysis script
├── server.py                # Web server for viewer
├── viewer.html              # 3D viewer interface
├── README.md                # This file
├── Live_Export/             # Input directory (your GLTF files)
│   ├── Result.gltf
│   └── *.bin files
└── output/                  # Output directory (created automatically)
    ├── Result_analyzed_*.gltf
    └── *.bin files
```

## Configuration

You can customize the analysis parameters in `analyze_gltf.py`:

```python
# Thresholds
WALL_SPACING_THRESHOLD = 8.0  # units
DOOR_DISTANCE_THRESHOLD = 6.0  # units

# Color definitions
CLASH_COLOR = [1.0, 0.0, 0.0, 1.0]  # Red
WALL_SPACING_COLOR = [1.0, 0.5, 0.0, 1.0]  # Orange
DOOR_DISTANCE_COLOR = [1.0, 1.0, 0.0, 1.0]  # Yellow
```

## Viewer Controls

- **Mouse Drag**: Rotate the camera around the model
- **Mouse Wheel**: Zoom in/out
- **Right-Click + Drag**: Pan the view
- **Reload Model Button**: Manually reload the latest model
- **Reset Camera Button**: Reset camera to default position

## API Endpoints

The web server provides a simple API:

### GET `/api/latest-model`

Returns information about the most recently analyzed GLTF file.

**Response:**
```json
{
  "filename": "Result_analyzed_20240101_143022.gltf",
  "filetime": 1704115822.123,
  "path": "/output/Result_analyzed_20240101_143022.gltf"
}
```

## Troubleshooting

### Analysis Script Issues

**Problem:** "File not found" error
- **Solution:** Ensure the GLTF file path is correct and the file exists
- **Solution:** Make sure associated .bin files are in the same directory as the .gltf file

**Problem:** No issues detected
- **Solution:** This is normal if your model doesn't have any of the specified issues
- **Solution:** Check that node names contain "wall" or "door" keywords for those checks to work

### Viewer Issues

**Problem:** Model doesn't load
- **Solution:** Check browser console for errors
- **Solution:** Ensure the web server is running
- **Solution:** Verify that analyzed files exist in the `output` directory

**Problem:** Colors not showing
- **Solution:** The analysis script creates new materials with colors. Ensure the analysis ran successfully
- **Solution:** Check browser console for any loading errors

**Problem:** Auto-reload not working
- **Solution:** Check browser console for errors
- **Solution:** Ensure the web server is running and accessible
- **Solution:** Manually click the "Reload Model" button

### Server Issues

**Problem:** Port already in use
- **Solution:** Use a different port: `python server.py 8080`
- **Solution:** Close other applications using the port

**Problem:** Files not found (404 errors)
- **Solution:** Ensure the `output` directory exists and contains analyzed files
- **Solution:** Run the analysis script first to generate output files

## Advanced Usage

### Watch Mode

The watch mode is perfect for continuous monitoring when:
- The GLTF file is being updated by another application
- You want real-time analysis as changes are made
- You're working in a live export scenario

**Windows Batch File:**
```bash
run_watch.bat
```

This will start watch mode with the default settings.

### Batch Processing

To analyze multiple files:

```bash
for file in Live_Export/*.gltf; do
    python analyze_gltf.py "$file"
done
```

### Custom Output Directory

Modify the `analyze_gltf()` function call in `analyze_gltf.py`:

```python
output_path = analyze_gltf(input_path, output_dir="custom_output")
```

### Integration with Other Tools

The analysis script can be integrated into build pipelines or automated workflows. The script returns the path to the output file, making it easy to chain with other tools.

## Technical Details

### Bounding Box Calculation

The analysis uses bounding boxes from GLTF accessors. Each mesh's bounding box is calculated from its position accessor's min/max values.

### Distance Calculations

- **Wall spacing**: Minimum distance between two bounding boxes
- **Door distance**: Minimum distance from door bounding box to wall bounding box

### Material Assignment

When issues are detected, the script creates new materials with the appropriate colors and assigns them to the affected meshes. The priority order is:
1. Clash (highest priority)
2. Wall spacing
3. Door distance (lowest priority)

## License

This tool is provided as-is for analysis and visualization purposes.

## Support

For issues or questions:
1. Check the Troubleshooting section above
2. Review the code comments in the Python scripts
3. Check browser console for JavaScript errors

## Future Enhancements

Potential improvements:
- Support for GLB (binary GLTF) files
- More sophisticated clash detection (mesh-level instead of bounding box)
- Configurable thresholds via command-line arguments
- Export analysis reports (JSON, CSV)
- Additional analysis types (e.g., minimum room size, ceiling height)
- Web-based analysis interface
- Real-time analysis as files are updated

