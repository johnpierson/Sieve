#!/usr/bin/env python3
"""
Simple web server for serving the GLTF viewer and analysis results.
"""

import os
import json
import time
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs
from pathlib import Path
import glob


class GLTFViewerHandler(BaseHTTPRequestHandler):
    """HTTP request handler for the GLTF viewer."""
    
    def do_GET(self):
        """Handle GET requests."""
        from urllib.parse import unquote
        parsed_path = urlparse(self.path)
        path = unquote(parsed_path.path)  # Decode URL-encoded paths
        
        # API endpoint for getting the latest model
        if path == '/api/latest-model':
            self.handle_latest_model()
            return
        
        # Serve static files
        if path == '/' or path == '/viewer.html':
            self.serve_file('viewer.html', 'text/html')
        elif path.startswith('/output/'):
            # Serve files from output directory
            file_path = path[1:]  # Remove leading /
            self.serve_file(file_path, self.get_content_type(file_path))
        else:
            self.send_error(404, "File not found")
    
    def handle_latest_model(self):
        """Handle API request for latest model file."""
        output_dir = 'output'
        
        if not os.path.exists(output_dir):
            self.send_json_response({'error': 'Output directory not found'})
            return
        
        # Find all analyzed files (prioritize GLB over GLTF)
        glb_pattern = os.path.join(output_dir, 'Result_analyzed_*.glb')
        gltf_pattern = os.path.join(output_dir, 'Result_analyzed_*.gltf')
        
        glb_files = glob.glob(glb_pattern)
        gltf_files = glob.glob(gltf_pattern)
        
        # Prioritize GLB files - if any exist, only use those
        if glb_files:
            files = glb_files
        elif gltf_files:
            files = gltf_files
        else:
            self.send_json_response({'error': 'No analyzed files found'})
            return
        
        # Get the most recently modified file
        latest_file = max(files, key=os.path.getmtime)
        filename = os.path.basename(latest_file)
        filetime = os.path.getmtime(latest_file)
        
        self.send_json_response({
            'filename': filename,
            'filetime': filetime,
            'path': f'/output/{filename}'
        })
    
    def serve_file(self, file_path, content_type):
        """Serve a file with the specified content type."""
        # Handle URL-encoded paths (spaces, special characters)
        from urllib.parse import unquote
        file_path = unquote(file_path)
        
        if not os.path.exists(file_path):
            self.send_error(404, f"File not found: {file_path}")
            return
        
        try:
            with open(file_path, 'rb') as f:
                content = f.read()
            
            # Get file modification time for cache control
            file_mtime = os.path.getmtime(file_path)
            
            self.send_response(200)
            self.send_header('Content-Type', content_type)
            self.send_header('Content-Length', str(len(content)))
            self.send_header('Access-Control-Allow-Origin', '*')
            # Add cache control headers to prevent stale file caching
            self.send_header('Cache-Control', 'no-cache, no-store, must-revalidate')
            self.send_header('Pragma', 'no-cache')
            self.send_header('Expires', '0')
            # Add ETag based on file modification time
            self.send_header('ETag', f'"{int(file_mtime)}"')
            self.end_headers()
            self.wfile.write(content)
        except Exception as e:
            self.send_error(500, f"Error serving file: {str(e)}")
    
    def send_json_response(self, data):
        """Send a JSON response."""
        json_data = json.dumps(data).encode('utf-8')
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(json_data)))
        self.send_header('Access-Control-Allow-Origin', '*')
        self.end_headers()
        self.wfile.write(json_data)
    
    def get_content_type(self, file_path):
        """Determine content type based on file extension."""
        ext = os.path.splitext(file_path)[1].lower()
        content_types = {
            '.gltf': 'model/gltf+json',
            '.glb': 'model/gltf-binary',
            '.bin': 'application/octet-stream',
            '.json': 'application/json',
            '.html': 'text/html',
            '.css': 'text/css',
            '.js': 'application/javascript',
            '.png': 'image/png',
            '.jpg': 'image/jpeg',
            '.jpeg': 'image/jpeg',
        }
        return content_types.get(ext, 'application/octet-stream')
    
    def log_message(self, format, *args):
        """Override to customize log format."""
        print(f"[{time.strftime('%Y-%m-%d %H:%M:%S')}] {format % args}")


def run_server(port=8000, max_attempts=10):
    """Run the web server, trying alternative ports if the requested port is unavailable."""
    for attempt in range(max_attempts):
        try:
            server_address = ('', port)
            httpd = HTTPServer(server_address, GLTFViewerHandler)
            
            print(f"GLTF Viewer Server starting on http://localhost:{port}")
            print(f"Open http://localhost:{port}/viewer.html in your browser")
            print("Press Ctrl+C to stop the server")
            
            try:
                httpd.serve_forever()
            except KeyboardInterrupt:
                print("\nShutting down server...")
                httpd.shutdown()
            return
        except OSError as e:
            # Check for port permission/availability errors
            is_port_error = (
                (hasattr(e, 'winerror') and e.winerror == 10013) or
                "Address already in use" in str(e) or
                "Permission denied" in str(e) or
                e.errno == 98 or  # Linux: Address already in use
                e.errno == 48     # macOS: Address already in use
            )
            
            if is_port_error:
                if attempt < max_attempts - 1:
                    print(f"Port {port} is unavailable. Trying port {port + 1}...")
                    port += 1
                else:
                    print(f"\nError: Could not find an available port after {max_attempts} attempts.")
                    print("Please close other applications using these ports or try a different port range.")
                    raise
            else:
                raise


if __name__ == '__main__':
    import sys
    
    port = 8000
    if len(sys.argv) > 1:
        try:
            port = int(sys.argv[1])
            if port < 1 or port > 65535:
                print(f"Error: Port must be between 1 and 65535")
                sys.exit(1)
        except ValueError:
            print(f"Invalid port number: {sys.argv[1]}")
            print("Usage: python server.py [port]")
            sys.exit(1)
    
    try:
        run_server(port)
    except Exception as e:
        print(f"\nFailed to start server: {e}")
        print("\nTroubleshooting tips:")
        print("1. Check if another application is using the port")
        print("2. Try running as administrator")
        print("3. Check Windows Firewall settings")
        print("4. Try a different port: python server.py 8080")
        sys.exit(1)

