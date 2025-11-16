#!/usr/bin/env python3
"""
GLTF Analysis Script - Advanced Architectural Analysis
Performs comprehensive clash detection, spacing checks, clearance validation, and more.
"""

import json
import os
import shutil
import sys
import time
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Tuple, Optional
import math

# Color definitions for different issue types
# Format: [R, G, B, A] where values are 0.0-1.0
CLASH_COLOR = [1.0, 0.0, 0.0, 1.0]  # Red for clashes
WALL_SPACING_COLOR = [1.0, 0.5, 0.0, 1.0]  # Orange for wall spacing issues
DOOR_DISTANCE_COLOR = [1.0, 1.0, 0.0, 1.0]  # Yellow for door distance issues
CLEARANCE_COLOR = [0.0, 1.0, 1.0, 1.0]  # Cyan for clearance issues
ACCESSIBILITY_COLOR = [1.0, 0.0, 1.0, 1.0]  # Magenta for accessibility issues
STRUCTURAL_COLOR = [0.5, 0.0, 0.5, 1.0]  # Purple for structural issues
EGRESS_COLOR = [1.0, 0.75, 0.8, 1.0]  # Pink for egress issues
FIXTURE_PLACEMENT_COLOR = [0.0, 0.5, 1.0, 1.0]  # Blue for fixture placement issues
EQUIPMENT_CLEARANCE_COLOR = [0.0, 1.0, 0.5, 1.0]  # Green-cyan for equipment clearance

# Thresholds
WALL_SPACING_THRESHOLD = 8.0  # units
DOOR_DISTANCE_THRESHOLD = 6.0  # units
DOOR_CLEARANCE_THRESHOLD = 3.0  # Minimum clearance around doors
WINDOW_CLEARANCE_THRESHOLD = 2.0  # Minimum clearance around windows
ACCESSIBILITY_CLEARANCE_THRESHOLD = 3.5  # Wheelchair accessibility clearance
STRUCTURAL_CLEARANCE_THRESHOLD = 1.0  # Minimum clearance for structural elements
EGRESS_WIDTH_THRESHOLD = 3.0  # Minimum egress path width
FIXTURE_CLEARANCE_THRESHOLD = 1.5  # Minimum clearance around fixtures
EQUIPMENT_CLEARANCE_THRESHOLD = 2.0  # Minimum clearance around equipment


class BoundingBox:
    """Represents a 3D bounding box."""
    
    def __init__(self, min_point: List[float], max_point: List[float]):
        self.min = min_point
        self.max = max_point
        self.center = [
            (min_point[0] + max_point[0]) / 2,
            (min_point[1] + max_point[1]) / 2,
            (min_point[2] + max_point[2]) / 2
        ]
        self.size = [
            max_point[0] - min_point[0],
            max_point[1] - min_point[1],
            max_point[2] - min_point[2]
        ]
    
    def intersects(self, other: 'BoundingBox', min_penetration: float = 0.1) -> bool:
        """
        Check if this bounding box intersects with another.
        
        Args:
            other: The other bounding box to check
            min_penetration: Minimum overlap required to consider it a clash (default: 0.1 units)
                            This prevents edge/corner touching from being considered clashes
        """
        # First check if boxes overlap at all
        if (self.max[0] < other.min[0] or
            self.min[0] > other.max[0] or
            self.max[1] < other.min[1] or
            self.min[1] > other.max[1] or
            self.max[2] < other.min[2] or
            self.min[2] > other.max[2]):
            return False
        
        # Calculate overlap on each axis
        overlap_x = min(self.max[0], other.max[0]) - max(self.min[0], other.min[0])
        overlap_y = min(self.max[1], other.max[1]) - max(self.min[1], other.min[1])
        overlap_z = min(self.max[2], other.max[2]) - max(self.min[2], other.min[2])
        
        # Require minimum penetration on at least 2 axes (not just edge/corner touching)
        # This means they must actually penetrate each other's volume
        overlaps = [overlap_x, overlap_y, overlap_z]
        significant_overlaps = [o for o in overlaps if o >= min_penetration]
        
        # Need at least 2 axes with significant overlap for a real clash
        # (corner/edge touches will only have 1 axis with overlap)
        return len(significant_overlaps) >= 2
    
    def distance_to(self, other: 'BoundingBox') -> float:
        """Calculate the minimum distance between two bounding boxes."""
        # Calculate distance along each axis
        dx = max(0, max(self.min[0] - other.max[0], other.min[0] - self.max[0]))
        dy = max(0, max(self.min[1] - other.max[1], other.min[1] - self.max[1]))
        dz = max(0, max(self.min[2] - other.max[2], other.min[2] - self.max[2]))
        
        # If boxes overlap, return 0
        if dx == 0 and dy == 0 and dz == 0:
            return 0.0
        
        # Otherwise return Euclidean distance
        return math.sqrt(dx*dx + dy*dy + dz*dz)
    
    def get_orientation(self) -> str:
        """Determine if the box is primarily horizontal (wall) or vertical (door-like)."""
        # Calculate the dominant dimension
        sizes = [abs(s) for s in self.size]
        max_dim = max(sizes)
        max_idx = sizes.index(max_dim)
        
        # If Z (height) is dominant, it's likely a wall
        # If X or Y is dominant and Z is small, it might be a door
        if max_idx == 2:  # Z is dominant
            return "wall"
        elif self.size[2] < min(self.size[0], self.size[1]) * 0.5:
            return "door"
        else:
            return "wall"
    
    def get_primary_axis(self) -> int:
        """
        Get the primary (longest) axis of the bounding box.
        Returns: 0 for X, 1 for Y, 2 for Z
        """
        sizes = [abs(s) for s in self.size]
        return sizes.index(max(sizes))
    
    def are_perpendicular(self, other: 'BoundingBox') -> bool:
        """
        Check if two bounding boxes have perpendicular primary axes.
        This means their long directions are at right angles to each other.
        """
        axis1 = self.get_primary_axis()
        axis2 = other.get_primary_axis()
        # Perpendicular means different axes (0 != 1, 1 != 2, 0 != 2)
        return axis1 != axis2


def load_gltf(file_path: str) -> Dict:
    """Load a GLTF file."""
    with open(file_path, 'r', encoding='utf-8') as f:
        return json.load(f)


def save_gltf(gltf_data: Dict, file_path: str):
    """Save a GLTF file."""
    with open(file_path, 'w', encoding='utf-8') as f:
        json.dump(gltf_data, f, indent=2)


def save_glb(gltf_data: Dict, file_path: str, gltf_dir: str = ""):
    """
    Save a GLB file (binary format that embeds all buffers).
    
    Args:
        gltf_data: The GLTF JSON data
        file_path: Output file path
        gltf_dir: Directory where .bin files are located (for reading them)
    """
    import struct
    
    # Calculate the maximum byteOffset + byteLength for each buffer to determine actual needed size
    buffer_max_sizes = {}
    if 'bufferViews' in gltf_data:
        for buffer_view in gltf_data['bufferViews']:
            buffer_idx = buffer_view.get('buffer', 0)
            byte_offset = buffer_view.get('byteOffset', 0)
            byte_length = buffer_view.get('byteLength', 0)
            max_size = byte_offset + byte_length
            if buffer_idx not in buffer_max_sizes or max_size > buffer_max_sizes[buffer_idx]:
                buffer_max_sizes[buffer_idx] = max_size
    
    # Collect all binary data and track offsets
    binary_chunks = []
    buffer_offsets = []
    current_offset = 0
    
    # Update buffer references to point to binary chunk
    if 'buffers' in gltf_data:
        for i, buffer in enumerate(gltf_data['buffers']):
            buffer_offsets.append(current_offset)
            expected_size = buffer.get('byteLength', 0)
            # Use the maximum size needed by bufferViews if larger than byteLength
            actual_needed_size = buffer_max_sizes.get(i, expected_size)
            if actual_needed_size > expected_size:
                print(f"Buffer {i}: byteLength is {expected_size}, but bufferViews need {actual_needed_size} bytes")
            
            if 'uri' in buffer:
                # Read the .bin file (read the full file, or at least what's needed)
                bin_path = os.path.join(gltf_dir, buffer['uri'])
                if os.path.exists(bin_path):
                    file_size = os.path.getsize(bin_path)
                    with open(bin_path, 'rb') as f:
                        # Read the full file (it may contain more data than byteLength indicates)
                        bin_data = f.read()
                    
                    # Use the larger of: file size, expected size, or actual needed size
                    # Always use at least the actual_needed_size to ensure all bufferViews fit
                    required_size = max(actual_needed_size, expected_size, len(bin_data))
                    
                    # If file is smaller than required, try Result.bin as fallback for buffer 0
                    if len(bin_data) < required_size and i == 0:
                        result_bin = os.path.join(gltf_dir, 'Result.bin')
                        if os.path.exists(result_bin):
                            result_size = os.path.getsize(result_bin)
                            if result_size >= required_size:
                                print(f"Buffer {i} file '{buffer['uri']}' is too small ({len(bin_data)}), using Result.bin ({result_size} bytes) instead")
                                with open(result_bin, 'rb') as f:
                                    bin_data = f.read()
                                # Update required_size to match the larger file
                                required_size = max(actual_needed_size, expected_size, len(bin_data))
                    
                    # Ensure we have enough data - pad if necessary
                    if len(bin_data) < required_size:
                        print(f"Warning: Buffer {i} file is smaller ({len(bin_data)}) than needed ({required_size}), padding with zeros")
                        bin_data += b'\x00' * (required_size - len(bin_data))
                    # If file is larger than required, use the full file (don't truncate)
                    elif len(bin_data) > required_size:
                        required_size = len(bin_data)
                    
                    # Use exactly required_size bytes (pad or truncate if needed)
                    if len(bin_data) >= required_size:
                        binary_chunks.append(bin_data[:required_size])
                    else:
                        # This shouldn't happen after padding, but just in case
                        padded_data = bin_data + b'\x00' * (required_size - len(bin_data))
                        binary_chunks.append(padded_data)
                    
                    current_offset += required_size
                    # Remove URI, buffer will be in binary chunk
                    del buffer['uri']
                else:
                    # Try to find Result.bin as fallback for buffer 0
                    if i == 0:
                        result_bin = os.path.join(gltf_dir, 'Result.bin')
                        if os.path.exists(result_bin):
                            print(f"Buffer {i} file '{buffer['uri']}' not found, using Result.bin instead")
                            with open(result_bin, 'rb') as f:
                                bin_data = f.read()
                            required_size = max(actual_needed_size, expected_size, len(bin_data))
                            if len(bin_data) < required_size:
                                bin_data += b'\x00' * (required_size - len(bin_data))
                            binary_chunks.append(bin_data[:required_size] if len(bin_data) > required_size else bin_data)
                            current_offset += required_size
                            del buffer['uri']
                        else:
                            print(f"Warning: Buffer {i} references file '{buffer['uri']}' which doesn't exist, creating zero-filled buffer")
                            binary_chunks.append(b'\x00' * actual_needed_size)
                            current_offset += actual_needed_size
                    else:
                        # Create empty buffer if file not found
                        print(f"Warning: Buffer {i} references file '{buffer['uri']}' which doesn't exist, creating zero-filled buffer")
                        binary_chunks.append(b'\x00' * actual_needed_size)
                        current_offset += actual_needed_size
            else:
                # Buffer already embedded or empty - create buffer of needed size
                binary_chunks.append(b'\x00' * actual_needed_size)
                current_offset += actual_needed_size
    
    # Combine all binary chunks into a single buffer
    binary_data = b''.join(binary_chunks)
    
    # Update bufferViews to account for offsets - ALL bufferViews must reference buffer 0
    # In GLB, all buffers are combined into one, so we need to adjust bufferView byteOffsets
    if 'bufferViews' in gltf_data:
        for i, buffer_view in enumerate(gltf_data['bufferViews']):
            buffer_idx = buffer_view.get('buffer', 0)
            original_offset = buffer_view.get('byteOffset', 0)
            original_length = buffer_view.get('byteLength', 0)
            
            # Ensure we have offsets for this buffer index
            if buffer_idx < len(buffer_offsets):
                # Add the offset of this buffer to the bufferView's byteOffset
                new_offset = buffer_offsets[buffer_idx] + original_offset
            else:
                # If buffer index is out of range, assume it's buffer 0
                print(f"Warning: bufferView {i} references buffer {buffer_idx} which doesn't exist, assuming buffer 0")
                new_offset = original_offset
            
            # Validate that the bufferView doesn't exceed the combined buffer size
            if new_offset + original_length > len(binary_data):
                print(f"ERROR: bufferView {i} extends beyond buffer!")
                print(f"  Offset: {new_offset}, Length: {original_length}, Buffer size: {len(binary_data)}")
                print(f"  Required end position: {new_offset + original_length}")
                print(f"  Original buffer index: {buffer_idx}, Original offset: {original_offset}")
                # Clamp the length to fit within the buffer
                max_length = len(binary_data) - new_offset
                if max_length > 0:
                    print(f"  Clamping length from {original_length} to {max_length}")
                    original_length = max_length
                else:
                    print(f"  ERROR: Cannot fix bufferView {i} - offset is beyond buffer end!")
                    # Set to a safe value (empty bufferView at the end)
                    new_offset = len(binary_data)
                    original_length = 0
            
            # Update the bufferView
            buffer_view['byteOffset'] = new_offset
            buffer_view['byteLength'] = original_length
            # All bufferViews now reference buffer 0 (the combined buffer)
            buffer_view['buffer'] = 0
    
    # Update buffers array to have a single buffer
    if 'buffers' in gltf_data:
        total_size = len(binary_data)
        gltf_data['buffers'] = [{'byteLength': total_size}]
    
    # Pad binary data to 4-byte boundary
    binary_padding = (4 - (len(binary_data) % 4)) % 4
    binary_data += b'\x00' * binary_padding
    
    # Convert GLTF JSON to string
    json_str = json.dumps(gltf_data, separators=(',', ':'))  # Compact JSON
    json_bytes = json_str.encode('utf-8')
    
    # Pad JSON to 4-byte boundary
    json_padding = (4 - (len(json_bytes) % 4)) % 4
    json_bytes += b' ' * json_padding
    
    # GLB format constants
    GLB_MAGIC = 0x46546C67  # "glTF"
    GLB_VERSION = 2
    JSON_CHUNK_TYPE = 0x4E4F534A  # "JSON"
    BIN_CHUNK_TYPE = 0x004E4942   # "BIN\0"
    
    # Calculate file size
    # Header (12) + JSON chunk header (8) + JSON data + Binary chunk header (8) + Binary data
    file_size = 12 + 8 + len(json_bytes) + 8 + len(binary_data)
    
    # Write GLB file
    with open(file_path, 'wb') as f:
        # Write header
        f.write(struct.pack('<I', GLB_MAGIC))      # Magic
        f.write(struct.pack('<I', GLB_VERSION))    # Version
        f.write(struct.pack('<I', file_size))      # File size
        
        # Write JSON chunk
        f.write(struct.pack('<I', len(json_bytes)))  # JSON chunk length
        f.write(struct.pack('<I', JSON_CHUNK_TYPE))  # JSON chunk type
        f.write(json_bytes)                          # JSON data
        
        # Write binary chunk (if there's binary data)
        if len(binary_data) > 0:
            f.write(struct.pack('<I', len(binary_data)))  # Binary chunk length
            f.write(struct.pack('<I', BIN_CHUNK_TYPE))    # Binary chunk type
            f.write(binary_data)                          # Binary data


def get_bounding_box_from_accessor(accessor: Dict) -> Optional[BoundingBox]:
    """Extract bounding box from an accessor."""
    if 'min' in accessor and 'max' in accessor:
        return BoundingBox(accessor['min'], accessor['max'])
    return None


def get_node_bounding_box(node: Dict, gltf_data: Dict) -> Optional[BoundingBox]:
    """Get the bounding box for a node by traversing to its mesh."""
    if 'mesh' not in node:
        return None
    
    mesh_idx = node['mesh']
    if mesh_idx >= len(gltf_data.get('meshes', [])):
        return None
    
    mesh = gltf_data['meshes'][mesh_idx]
    if not mesh.get('primitives'):
        return None
    
    # Get the first primitive's position accessor
    primitive = mesh['primitives'][0]
    if 'attributes' not in primitive or 'POSITION' not in primitive['attributes']:
        return None
    
    pos_accessor_idx = primitive['attributes']['POSITION']
    if pos_accessor_idx >= len(gltf_data.get('accessors', [])):
        return None
    
    accessor = gltf_data['accessors'][pos_accessor_idx]
    return get_bounding_box_from_accessor(accessor)


def get_entity_category(name: str) -> str:
    """
    Determine the entity category from the node name.
    Returns: 'wall', 'door', 'window', 'column', 'beam', 'slab', 'stair', 
             'furniture', 'fixture', 'equipment', or 'unknown'
    """
    name_lower = name.lower()
    
    # Check in order of specificity (more specific first)
    if 'wall' in name_lower:
        return 'wall'
    elif 'door' in name_lower:
        return 'door'
    elif 'window' in name_lower:
        return 'window'
    elif 'column' in name_lower or 'pillar' in name_lower:
        return 'column'
    elif 'beam' in name_lower or 'girder' in name_lower:
        return 'beam'
    elif 'slab' in name_lower or 'floor' in name_lower or 'ceiling' in name_lower:
        return 'slab'
    elif 'stair' in name_lower or 'step' in name_lower or 'ramp' in name_lower:
        return 'stair'
    elif 'furniture' in name_lower or 'furnishing' in name_lower:
        return 'furniture'
    elif 'fixture' in name_lower or 'plumbing' in name_lower or 'sink' in name_lower or 'toilet' in name_lower:
        return 'fixture'
    elif 'equipment' in name_lower or 'hvac' in name_lower or 'electrical' in name_lower or 'mechanical' in name_lower:
        return 'equipment'
    else:
        return 'unknown'


def is_wall(name: str) -> bool:
    """Check if a node is a wall based on its name."""
    return get_entity_category(name) == 'wall'


def is_door(name: str) -> bool:
    """Check if a node is a door based on its name."""
    return get_entity_category(name) == 'door'


def is_window(name: str) -> bool:
    """Check if a node is a window based on its name."""
    return get_entity_category(name) == 'window'


def is_column(name: str) -> bool:
    """Check if a node is a column based on its name."""
    return get_entity_category(name) == 'column'


def is_beam(name: str) -> bool:
    """Check if a node is a beam based on its name."""
    return get_entity_category(name) == 'beam'


def is_slab(name: str) -> bool:
    """Check if a node is a slab/floor/ceiling based on its name."""
    return get_entity_category(name) == 'slab'


def is_stair(name: str) -> bool:
    """Check if a node is a stair/ramp based on its name."""
    return get_entity_category(name) == 'stair'


def is_furniture(name: str) -> bool:
    """Check if a node is furniture based on its name."""
    return get_entity_category(name) == 'furniture'


def is_fixture(name: str) -> bool:
    """Check if a node is a fixture based on its name."""
    return get_entity_category(name) == 'fixture'


def is_equipment(name: str) -> bool:
    """Check if a node is equipment based on its name."""
    return get_entity_category(name) == 'equipment'


def create_material(gltf_data: Dict, color: List[float], name: str) -> int:
    """Create a new material with the specified color and return its index."""
    if 'materials' not in gltf_data:
        gltf_data['materials'] = []
    
    material = {
        "name": name,
        "alphaMode": "OPAQUE",
        "doubleSided": True,
        "pbrMetallicRoughness": {
            "baseColorFactor": color,
            "metallicFactor": 0.0,
            "roughnessFactor": 1.0
        }
    }
    
    gltf_data['materials'].append(material)
    return len(gltf_data['materials']) - 1


def analyze_gltf(input_path: str, output_dir: str = "output") -> str:
    """
    Analyze a GLTF file and create a timestamped output with color-coded issues.
    
    Returns the path to the output file.
    """
    # Check if input file exists
    if not os.path.exists(input_path):
        raise FileNotFoundError(f"Input file not found: {input_path}")
    
    # Create output directory
    os.makedirs(output_dir, exist_ok=True)
    
    # Copy GLTF to temp location quickly and release the file handle immediately
    # Use copyfile instead of copy2 for faster operation (we don't need metadata)
    temp_path = os.path.join(output_dir, "temp_result.gltf")
    try:
        # Try to copy the file - if it's locked, we'll catch the error
        with open(input_path, 'rb') as src:
            with open(temp_path, 'wb') as dst:
                # Read and write in chunks for efficiency, but ensure we release handles quickly
                shutil.copyfileobj(src, dst, length=64*1024)  # 64KB chunks
    except (PermissionError, OSError) as e:
        # File might be locked - wait a moment and retry once
        import time
        time.sleep(0.1)
        try:
            with open(input_path, 'rb') as src:
                with open(temp_path, 'wb') as dst:
                    shutil.copyfileobj(src, dst, length=64*1024)
        except (PermissionError, OSError) as e2:
            raise FileNotFoundError(f"Cannot access file (may be locked by another process): {input_path}") from e2
    
    # Now read from the temp copy (not the original) - this ensures we don't block the original file
    gltf_dir = os.path.dirname(input_path)
    gltf_data = load_gltf(temp_path)
    
    # Copy all referenced .bin files quickly and release handles immediately
    if 'buffers' in gltf_data:
        for buffer in gltf_data['buffers']:
            if 'uri' in buffer:
                bin_file = os.path.join(gltf_dir, buffer['uri'])
                if os.path.exists(bin_file):
                    dest_bin = os.path.join(output_dir, buffer['uri'])
                    try:
                        # Copy quickly using file handles that are immediately released
                        with open(bin_file, 'rb') as src:
                            with open(dest_bin, 'wb') as dst:
                                shutil.copyfileobj(src, dst, length=64*1024)  # 64KB chunks
                    except (PermissionError, OSError) as e:
                        # If bin file is locked, skip it (we'll handle it in save_glb)
                        print(f"Warning: Could not copy {buffer['uri']} (may be locked): {e}")
    
    # Extract all nodes with their bounding boxes and entity categories
    nodes_data = []
    for i, node in enumerate(gltf_data.get('nodes', [])):
        bbox = get_node_bounding_box(node, gltf_data)
        if bbox:
            node_name = node.get('name', f'Node_{i}')
            nodes_data.append({
                'index': i,
                'node': node,
                'bbox': bbox,
                'name': node_name,
                'category': get_entity_category(node_name),
                'mesh_idx': node.get('mesh'),
                'issues': []
            })
    
    # Create materials for different issue types
    clash_material_idx = create_material(gltf_data, CLASH_COLOR, "Clash_Issue")
    wall_spacing_material_idx = create_material(gltf_data, WALL_SPACING_COLOR, "Wall_Spacing_Issue")
    door_distance_material_idx = create_material(gltf_data, DOOR_DISTANCE_COLOR, "Door_Distance_Issue")
    clearance_material_idx = create_material(gltf_data, CLEARANCE_COLOR, "Clearance_Issue")
    accessibility_material_idx = create_material(gltf_data, ACCESSIBILITY_COLOR, "Accessibility_Issue")
    structural_material_idx = create_material(gltf_data, STRUCTURAL_COLOR, "Structural_Issue")
    egress_material_idx = create_material(gltf_data, EGRESS_COLOR, "Egress_Issue")
    fixture_placement_material_idx = create_material(gltf_data, FIXTURE_PLACEMENT_COLOR, "Fixture_Placement_Issue")
    equipment_clearance_material_idx = create_material(gltf_data, EQUIPMENT_CLEARANCE_COLOR, "Equipment_Clearance_Issue")
    
    # 1. Clash Detection - Check for intersecting bounding boxes with actual penetration
    # Exclude wall-to-wall clashes (walls don't clash with themselves)
    # Requires significant overlap (high penetration threshold) to avoid false positives
    print("\n" + "="*60)
    print("Performing clash detection (requires significant volume penetration)...")
    print("  Note: Walls are excluded from clashing with other walls")
    print("  High overlap requirement: 2.0 units minimum penetration")
    print("="*60)
    clash_count = 0
    min_penetration = 2.0  # High minimum overlap in units to consider it a clash (was 0.1)
    
    for i, node_data1 in enumerate(nodes_data):
        for j, node_data2 in enumerate(nodes_data[i+1:], start=i+1):
            # Skip wall-to-wall clashes
            if node_data1['category'] == 'wall' and node_data2['category'] == 'wall':
                continue
            
            if node_data1['bbox'].intersects(node_data2['bbox'], min_penetration):
                # Calculate overlap details for reporting
                bbox1 = node_data1['bbox']
                bbox2 = node_data2['bbox']
                overlap_x = min(bbox1.max[0], bbox2.max[0]) - max(bbox1.min[0], bbox2.min[0])
                overlap_y = min(bbox1.max[1], bbox2.max[1]) - max(bbox1.min[1], bbox2.min[1])
                overlap_z = min(bbox1.max[2], bbox2.max[2]) - max(bbox1.min[2], bbox2.min[2])
                
                node_data1['issues'].append(('clash', j))
                node_data2['issues'].append(('clash', i))
                clash_count += 1
                print(f"  Clash detected: {node_data1['name']} ({node_data1['category']}) <-> {node_data2['name']} ({node_data2['category']}) "
                      f"(overlap: X={overlap_x:.2f}, Y={overlap_y:.2f}, Z={overlap_z:.2f})")
    
    # 2. Wall Spacing Check - Check for walls less than 8 units apart
    # Only flag walls that are close AND have perpendicular primary axes (long directions)
    print(f"\nChecking wall spacing (threshold: {WALL_SPACING_THRESHOLD} units)...")
    print("  Note: Only flags walls that are close AND have perpendicular long axes")
    wall_spacing_count = 0
    walls = [nd for nd in nodes_data if is_wall(nd['name'])]
    
    for i, wall1 in enumerate(walls):
        for wall2 in walls[i+1:]:
            distance = wall1['bbox'].distance_to(wall2['bbox'])
            # Only flag if walls are close AND have perpendicular primary axes
            if 0 < distance < WALL_SPACING_THRESHOLD:
                # Check if walls have perpendicular long directions
                if wall1['bbox'].are_perpendicular(wall2['bbox']):
                    axis1 = wall1['bbox'].get_primary_axis()
                    axis2 = wall2['bbox'].get_primary_axis()
                    axis_names = ['X', 'Y', 'Z']
                    wall1['issues'].append(('wall_spacing', wall2['index']))
                    wall2['issues'].append(('wall_spacing', wall1['index']))
                    wall_spacing_count += 1
                    print(f"  Wall spacing issue: {wall1['name']} <-> {wall2['name']} "
                          f"(distance: {distance:.2f}, axes: {axis_names[axis1]} vs {axis_names[axis2]})")
    
    # 3. Door Distance Check - Check doors more than 6 units from perpendicular walls
    print(f"\nChecking door distance from perpendicular walls (threshold: {DOOR_DISTANCE_THRESHOLD} units)...")
    door_distance_count = 0
    doors = [nd for nd in nodes_data if is_door(nd['name'])]
    
    for door in doors:
        door_bbox = door['bbox']
        door_center = door_bbox.center
        
        for wall in walls:
            wall_bbox = wall['bbox']
            
            # Check if wall is perpendicular to door
            # Simple heuristic: check if door is near the wall's plane
            # Calculate distance from door center to wall's bounding box
            distance = door_bbox.distance_to(wall_bbox)
            
            # If door is too far from wall, it's an issue
            if distance > DOOR_DISTANCE_THRESHOLD:
                # Check if they're roughly perpendicular (one is horizontal, one is vertical in 2D)
                door_orientation = door_bbox.get_orientation()
                wall_orientation = wall_bbox.get_orientation()
                
                if door_orientation != wall_orientation:
                    door['issues'].append(('door_distance', wall['index']))
                    door_distance_count += 1
                    print(f"  Door distance issue: {door['name']} is {distance:.2f} units from {wall['name']}")
    
    # 4. Door Clearance Check - Check minimum clearance around doors
    print(f"\nChecking door clearance (threshold: {DOOR_CLEARANCE_THRESHOLD} units)...")
    door_clearance_count = 0
    doors = [nd for nd in nodes_data if is_door(nd['name'])]
    all_other_entities = [nd for nd in nodes_data if not is_door(nd['name'])]
    
    for door in doors:
        for other in all_other_entities:
            if other['index'] == door['index']:
                continue
            distance = door['bbox'].distance_to(other['bbox'])
            if 0 < distance < DOOR_CLEARANCE_THRESHOLD:
                door['issues'].append(('clearance', other['index']))
                door_clearance_count += 1
                print(f"  Door clearance issue: {door['name']} has only {distance:.2f} units clearance from {other['name']}")
    
    # 5. Window Clearance Check - Check minimum clearance around windows
    print(f"\nChecking window clearance (threshold: {WINDOW_CLEARANCE_THRESHOLD} units)...")
    window_clearance_count = 0
    windows = [nd for nd in nodes_data if is_window(nd['name'])]
    all_other_entities = [nd for nd in nodes_data if not is_window(nd['name'])]
    
    for window in windows:
        for other in all_other_entities:
            if other['index'] == window['index']:
                continue
            distance = window['bbox'].distance_to(other['bbox'])
            if 0 < distance < WINDOW_CLEARANCE_THRESHOLD:
                window['issues'].append(('clearance', other['index']))
                window_clearance_count += 1
                print(f"  Window clearance issue: {window['name']} has only {distance:.2f} units clearance from {other['name']}")
    
    # 6. Accessibility Check - Check wheelchair accessibility clearances
    print(f"\nChecking accessibility clearances (threshold: {ACCESSIBILITY_CLEARANCE_THRESHOLD} units)...")
    accessibility_count = 0
    doors = [nd for nd in nodes_data if is_door(nd['name'])]
    stairs = [nd for nd in nodes_data if is_stair(nd['name'])]
    all_other_entities = [nd for nd in nodes_data if not is_door(nd['name']) and not is_stair(nd['name'])]
    
    # Check door accessibility
    for door in doors:
        for other in all_other_entities:
            if other['index'] == door['index']:
                continue
            distance = door['bbox'].distance_to(other['bbox'])
            if 0 < distance < ACCESSIBILITY_CLEARANCE_THRESHOLD:
                door['issues'].append(('accessibility', other['index']))
                accessibility_count += 1
                print(f"  Accessibility issue: {door['name']} has insufficient clearance ({distance:.2f} units) from {other['name']} for wheelchair access")
    
    # Check stair accessibility (ramps should have clear paths)
    for stair in stairs:
        for other in all_other_entities:
            if other['index'] == stair['index']:
                continue
            distance = stair['bbox'].distance_to(other['bbox'])
            if 0 < distance < ACCESSIBILITY_CLEARANCE_THRESHOLD:
                stair['issues'].append(('accessibility', other['index']))
                accessibility_count += 1
                print(f"  Accessibility issue: {stair['name']} has insufficient clearance ({distance:.2f} units) from {other['name']} for accessibility")
    
    # 7. Structural Check - Check columns/beams clashing with walls or insufficient clearance
    print(f"\nChecking structural element clearances (threshold: {STRUCTURAL_CLEARANCE_THRESHOLD} units)...")
    structural_count = 0
    columns = [nd for nd in nodes_data if is_column(nd['name'])]
    beams = [nd for nd in nodes_data if is_beam(nd['name'])]
    structural_elements = columns + beams
    walls = [nd for nd in nodes_data if is_wall(nd['name'])]
    
    for struct_elem in structural_elements:
        for wall in walls:
            distance = struct_elem['bbox'].distance_to(wall['bbox'])
            # Structural elements should have some clearance from walls (unless intentionally integrated)
            if 0 < distance < STRUCTURAL_CLEARANCE_THRESHOLD:
                struct_elem['issues'].append(('structural', wall['index']))
                structural_count += 1
                print(f"  Structural issue: {struct_elem['name']} has only {distance:.2f} units clearance from {wall['name']}")
    
    # 8. Egress Check - Check egress path widths (doors and corridors)
    print(f"\nChecking egress path widths (threshold: {EGRESS_WIDTH_THRESHOLD} units)...")
    egress_count = 0
    doors = [nd for nd in nodes_data if is_door(nd['name'])]
    
    for door in doors:
        # Check if door opening width is sufficient for egress
        door_width = min(door['bbox'].size[0], door['bbox'].size[1])
        if door_width < EGRESS_WIDTH_THRESHOLD:
            door['issues'].append(('egress', None))
            egress_count += 1
            print(f"  Egress issue: {door['name']} has insufficient width ({door_width:.2f} units) for egress requirements")
        
        # Check clearance on both sides of door for egress path
        for other in nodes_data:
            if other['index'] == door['index']:
                continue
            distance = door['bbox'].distance_to(other['bbox'])
            if 0 < distance < EGRESS_WIDTH_THRESHOLD:
                door['issues'].append(('egress', other['index']))
                egress_count += 1
                print(f"  Egress issue: {door['name']} has insufficient egress path width ({distance:.2f} units) due to {other['name']}")
    
    # 9. Fixture Placement Check - Check fixture clearances from walls and other fixtures
    print(f"\nChecking fixture placement clearances (threshold: {FIXTURE_CLEARANCE_THRESHOLD} units)...")
    fixture_placement_count = 0
    fixtures = [nd for nd in nodes_data if is_fixture(nd['name'])]
    walls = [nd for nd in nodes_data if is_wall(nd['name'])]
    all_other_entities = [nd for nd in nodes_data if not is_fixture(nd['name'])]
    
    for fixture in fixtures:
        # Check clearance from walls
        for wall in walls:
            distance = fixture['bbox'].distance_to(wall['bbox'])
            if 0 < distance < FIXTURE_CLEARANCE_THRESHOLD:
                fixture['issues'].append(('fixture_placement', wall['index']))
                fixture_placement_count += 1
                print(f"  Fixture placement issue: {fixture['name']} has only {distance:.2f} units clearance from {wall['name']}")
        
        # Check clearance from other fixtures
        for other_fixture in fixtures:
            if other_fixture['index'] == fixture['index']:
                continue
            distance = fixture['bbox'].distance_to(other_fixture['bbox'])
            if 0 < distance < FIXTURE_CLEARANCE_THRESHOLD:
                fixture['issues'].append(('fixture_placement', other_fixture['index']))
                fixture_placement_count += 1
                print(f"  Fixture placement issue: {fixture['name']} has only {distance:.2f} units clearance from {other_fixture['name']}")
    
    # 10. Equipment Clearance Check - Check equipment clearances for maintenance and operation
    print(f"\nChecking equipment clearances (threshold: {EQUIPMENT_CLEARANCE_THRESHOLD} units)...")
    equipment_clearance_count = 0
    equipment = [nd for nd in nodes_data if is_equipment(nd['name'])]
    all_other_entities = [nd for nd in nodes_data if not is_equipment(nd['name'])]
    
    for equip in equipment:
        for other in all_other_entities:
            if other['index'] == equip['index']:
                continue
            distance = equip['bbox'].distance_to(other['bbox'])
            if 0 < distance < EQUIPMENT_CLEARANCE_THRESHOLD:
                equip['issues'].append(('equipment_clearance', other['index']))
                equipment_clearance_count += 1
                print(f"  Equipment clearance issue: {equip['name']} has only {distance:.2f} units clearance from {other['name']} (insufficient for maintenance)")
    
    # Apply colors to meshes based on issues
    print(f"\nApplying colors to issues...")
    print(f"  Original materials count: {len(gltf_data.get('materials', []))}")
    colored_count = 0
    nodes_without_materials = 0
    
    for node_data in nodes_data:
        mesh_idx = node_data['mesh_idx']
        if mesh_idx is None or mesh_idx >= len(gltf_data.get('meshes', [])):
            continue
        
        mesh = gltf_data['meshes'][mesh_idx]
        
        # Ensure all primitives have a material set (default to 0 if not set)
        for primitive in mesh.get('primitives', []):
            if 'material' not in primitive:
                primitive['material'] = 0
                nodes_without_materials += 1
        
        # Only color nodes that have issues
        if not node_data['issues']:
            # Keep original material for nodes without issues
            continue
        
        # Determine which issue type takes priority (highest to lowest):
        # (mesh_idx and mesh are already validated above)
        # 1. Clash (red) - most critical
        # 2. Structural (purple) - safety critical
        # 3. Egress (pink) - safety critical
        # 4. Accessibility (magenta) - code compliance
        # 5. Clearance (cyan) - functional
        # 6. Equipment clearance (green-cyan) - maintenance
        # 7. Fixture placement (blue) - functional
        # 8. Door distance (yellow) - design guideline
        # 9. Wall spacing (orange) - design guideline
        issue_priority = None
        material_idx = None
        if any(issue[0] == 'clash' for issue in node_data['issues']):
            issue_priority = 'clash'
            material_idx = clash_material_idx
        elif any(issue[0] == 'structural' for issue in node_data['issues']):
            issue_priority = 'structural'
            material_idx = structural_material_idx
        elif any(issue[0] == 'egress' for issue in node_data['issues']):
            issue_priority = 'egress'
            material_idx = egress_material_idx
        elif any(issue[0] == 'accessibility' for issue in node_data['issues']):
            issue_priority = 'accessibility'
            material_idx = accessibility_material_idx
        elif any(issue[0] == 'clearance' for issue in node_data['issues']):
            issue_priority = 'clearance'
            material_idx = clearance_material_idx
        elif any(issue[0] == 'equipment_clearance' for issue in node_data['issues']):
            issue_priority = 'equipment_clearance'
            material_idx = equipment_clearance_material_idx
        elif any(issue[0] == 'fixture_placement' for issue in node_data['issues']):
            issue_priority = 'fixture_placement'
            material_idx = fixture_placement_material_idx
        elif any(issue[0] == 'door_distance' for issue in node_data['issues']):
            issue_priority = 'door_distance'
            material_idx = door_distance_material_idx
        elif any(issue[0] == 'wall_spacing' for issue in node_data['issues']):
            issue_priority = 'wall_spacing'
            material_idx = wall_spacing_material_idx
        
        if issue_priority and material_idx is not None:
            # Update material for all primitives in the mesh
            original_materials = []
            for primitive in mesh.get('primitives', []):
                # Track original material before changing
                original_materials.append(primitive.get('material', 0))
                primitive['material'] = material_idx
            colored_count += 1
            orig_mat_str = ', '.join(map(str, set(original_materials))) if original_materials else '0'
            print(f"  Applied {issue_priority} color (material {material_idx}) to {node_data['name']} (was material {orig_mat_str})")
    
    print(f"  Total nodes with issues colored: {colored_count} out of {len(nodes_data)} total nodes")
    print(f"  Final materials count: {len(gltf_data.get('materials', []))}")
    if nodes_without_materials > 0:
        print(f"  Fixed {nodes_without_materials} primitives that were missing material assignments")
    
    # Generate timestamped filename (as GLB)
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    output_filename = f"Result_analyzed_{timestamp}.glb"
    output_path = os.path.join(output_dir, output_filename)
    
    # Save the analyzed GLB (embeds all buffers, no separate .bin files needed)
    save_glb(gltf_data, output_path, output_dir)
    
    # Print summary
    print(f"\n{'='*60}")
    print(f"Analysis Complete!")
    print(f"{'='*60}")
    print(f"1. Clashes detected: {clash_count} (Red)")
    print(f"2. Wall spacing issues: {wall_spacing_count} (Orange)")
    print(f"3. Door distance issues: {door_distance_count} (Yellow)")
    print(f"4. Door clearance issues: {door_clearance_count} (Cyan)")
    print(f"5. Window clearance issues: {window_clearance_count} (Cyan)")
    print(f"6. Accessibility issues: {accessibility_count} (Magenta)")
    print(f"7. Structural issues: {structural_count} (Purple)")
    print(f"8. Egress issues: {egress_count} (Pink)")
    print(f"9. Fixture placement issues: {fixture_placement_count} (Blue)")
    print(f"10. Equipment clearance issues: {equipment_clearance_count} (Green-Cyan)")
    print(f"\nOutput saved to: {output_path}")
    print(f"{'='*60}\n")
    
    return output_path


def watch_and_analyze(input_path: str, interval: int = 5):
    """
    Continuously monitor the GLTF file and re-analyze it at regular intervals.
    
    Args:
        input_path: Path to the GLTF file to monitor
        interval: Time interval in seconds between checks (default: 5)
    """
    last_modified = 0
    iteration = 0
    file_exists = os.path.exists(input_path)
    
    print(f"Watching file: {input_path}")
    if not file_exists:
        print(f"  NOTE: File does not exist yet. Waiting for it to be created...")
    print(f"Recheck interval: {interval} seconds")
    print("Press Ctrl+C to stop\n")
    print("="*60)
    
    try:
        while True:
            try:
                iteration += 1
                
                # Check if file still exists
                if not os.path.exists(input_path):
                    print(f"\n[{datetime.now().strftime('%H:%M:%S')}] WARNING: File not found: {input_path}")
                    print(f"Waiting {interval} seconds before checking again...")
                    time.sleep(interval)
                    continue
                
                current_modified = os.path.getmtime(input_path)
                
                # Check if file has been modified or it's the first run
                if current_modified != last_modified or iteration == 1:
                    if iteration > 1:
                        print(f"\n{'='*60}")
                        print(f"File change detected! Re-analyzing...")
                        print(f"{'='*60}\n")
                    else:
                        print(f"Initial analysis (iteration {iteration})...\n")
                    
                    try:
                        output_path = analyze_gltf(input_path)
                        last_modified = current_modified
                        print(f"\n[{datetime.now().strftime('%H:%M:%S')}] Analysis complete. Next check in {interval} seconds...")
                    except Exception as e:
                        import traceback
                        print(f"\n[{datetime.now().strftime('%H:%M:%S')}] ERROR during analysis:")
                        print(f"  {str(e)}")
                        print(f"  Traceback: {traceback.format_exc()}")
                        print(f"  Will retry in {interval} seconds...")
                else:
                    print(f"[{datetime.now().strftime('%H:%M:%S')}] No changes detected. Next check in {interval} seconds...")
                
                # Wait for the specified interval
                time.sleep(interval)
                
            except Exception as e:
                # Catch any unexpected errors in the loop and continue
                import traceback
                print(f"\n[{datetime.now().strftime('%H:%M:%S')}] Unexpected error in watch loop:")
                print(f"  {str(e)}")
                print(f"  Traceback: {traceback.format_exc()}")
                print(f"  Continuing watch mode... Will retry in {interval} seconds...")
                time.sleep(interval)
                continue
            
    except KeyboardInterrupt:
        print(f"\n\n{'='*60}")
        print("Monitoring stopped by user.")
        print(f"{'='*60}")
    except Exception as e:
        # Final catch-all for any unhandled exceptions
        import traceback
        print(f"\n\n{'='*60}")
        print("FATAL ERROR: Watch mode has stopped due to an unexpected error:")
        print(f"  {str(e)}")
        print(f"  Traceback: {traceback.format_exc()}")
        print(f"{'='*60}")
        print("Press any key to exit...")
        input()


def main():
    """Main entry point."""
    watch_mode = False
    interval = 5
    input_path = "Live_Export/Result.gltf"
    
    # Parse command line arguments
    i = 1
    while i < len(sys.argv):
        arg = sys.argv[i]
        if arg == "--watch" or arg == "-w":
            watch_mode = True
            i += 1
        elif arg == "--interval" or arg == "-i":
            if i + 1 < len(sys.argv):
                try:
                    interval = int(sys.argv[i + 1])
                    if interval < 1:
                        print("Error: Interval must be at least 1 second")
                        sys.exit(1)
                except ValueError:
                    print(f"Error: Invalid interval value: {sys.argv[i + 1]}")
                    sys.exit(1)
                i += 2
            else:
                print("Error: --interval requires a value")
                sys.exit(1)
        elif arg == "--help" or arg == "-h":
            print("Usage: python analyze_gltf.py [options] [gltf_file]")
            print("\nOptions:")
            print("  --watch, -w              Run in watch mode (continuous monitoring)")
            print("  --interval, -i SECONDS   Set recheck interval in seconds (default: 5)")
            print("  --help, -h               Show this help message")
            print("\nExamples:")
            print("  python analyze_gltf.py Live_Export/Result.gltf")
            print("  python analyze_gltf.py --watch")
            print("  python analyze_gltf.py --watch --interval 60 Live_Export/Result.gltf")
            sys.exit(0)
        elif not arg.startswith("-"):
            input_path = arg
            i += 1
        else:
            print(f"Error: Unknown option: {arg}")
            print("Use --help for usage information")
            sys.exit(1)
    
    # In watch mode, allow starting even if file doesn't exist yet
    # In single-run mode, file must exist
    if not watch_mode and not os.path.exists(input_path):
        print(f"Error: File not found: {input_path}")
        print(f"  In watch mode, the system will wait for the file to be created.")
        print(f"  For single analysis, the file must exist.")
        sys.exit(1)
    
    if watch_mode:
        watch_and_analyze(input_path, interval)
    else:
        output_path = analyze_gltf(input_path)
        print(f"Analysis complete. Output: {output_path}")


if __name__ == "__main__":
    main()

