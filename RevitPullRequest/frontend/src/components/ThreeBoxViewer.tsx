import React, { useEffect, useRef } from "react";
import * as THREE from "three";
import type { BoundingBoxData } from "@/types/messages";

interface ThreeBoxViewerProps {
  recorded?: BoundingBoxData | null;
  current?: BoundingBoxData | null;
}

// Simple Three.js-based 3D viewer to show original (blue) vs current (red)
// bounding boxes in the same 3D space with orbit-style controls.
export default function ThreeBoxViewer({ recorded, current }: ThreeBoxViewerProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const container = containerRef.current;
    if (!container || (!recorded && !current)) return;

    const width = container.clientWidth || 320;
    const height = container.clientHeight || 180;

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0xf1f5f9); // slate-100

    const camera = new THREE.PerspectiveCamera(45, width / height, 0.1, 1000);
    camera.position.set(2, 2, 4);

    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(width, height);
    container.innerHTML = "";
    container.appendChild(renderer.domElement);

    // Simple orbit-style controls (manual implementation)
    let isDragging = false;
    let lastX = 0;
    let lastY = 0;
    let rotationY = 0;
    let rotationX = 0.5;
    let distance = 6;

    const target = new THREE.Vector3(0, 0, 0);

    const onMouseDown = (event: MouseEvent) => {
      if (event.button !== 1 && event.button !== 0) return;
      isDragging = true;
      lastX = event.clientX;
      lastY = event.clientY;
    };

    const onMouseMove = (event: MouseEvent) => {
      if (!isDragging) return;
      const dx = event.clientX - lastX;
      const dy = event.clientY - lastY;
      lastX = event.clientX;
      lastY = event.clientY;

      // Spin (orbit) around the bounding box center.
      // We intentionally do NOT modify `target` here so the pivot stays
      // locked to the computed bounding-box center instead of drifting.
      rotationY -= dx * 0.01;
      rotationX = Math.max(
        -Math.PI / 2 + 0.1,
        Math.min(Math.PI / 2 - 0.1, rotationX - dy * 0.01)
      );
    };

    const onMouseUp = () => {
      isDragging = false;
    };

    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      const factor = 1 - event.deltaY * 0.001;
      distance = Math.max(1.5, Math.min(20, distance * factor));
    };

    renderer.domElement.addEventListener("mousedown", onMouseDown);
    window.addEventListener("mousemove", onMouseMove);
    window.addEventListener("mouseup", onMouseUp);
    renderer.domElement.addEventListener("wheel", onWheel, { passive: false });

    // Build boxes from bounding data
    const boxes: { bbox: BoundingBoxData; color: THREE.Color; opacity: number; wireframe?: boolean }[] = [];
    if (recorded) {
      boxes.push({ bbox: recorded, color: new THREE.Color(0x2563eb), opacity: 0.25, wireframe: true }); // blue
    }
    if (current) {
      boxes.push({ bbox: current, color: new THREE.Color(0xef4444), opacity: 0.35 }); // red
    }

    // Map Revit coordinates (X, Y, Z-up) to Three.js coordinates with Z-up visual:
    // We transform points so that Revit Z becomes the vertical axis in the viewer.
    const toThree = (p: BoundingBoxData["min"]) =>
      new THREE.Vector3(p.x, p.z, p.y);

    // Compute union center in transformed (Three.js) space
    const transformedMins = boxes.map((b) => toThree(b.bbox.min));
    const transformedMaxs = boxes.map((b) => toThree(b.bbox.max));

    const allMinX = Math.min(...transformedMins.map((v) => v.x));
    const allMaxX = Math.max(...transformedMaxs.map((v) => v.x));
    const allMinY = Math.min(...transformedMins.map((v) => v.y));
    const allMaxY = Math.max(...transformedMaxs.map((v) => v.y));
    const allMinZ = Math.min(...transformedMins.map((v) => v.z));
    const allMaxZ = Math.max(...transformedMaxs.map((v) => v.z));

    target.set(
      (allMinX + allMaxX) / 2,
      (allMinY + allMaxY) / 2,
      (allMinZ + allMaxZ) / 2
    );

    const maxDim = Math.max(allMaxX - allMinX, allMaxY - allMinY, allMaxZ - allMinZ, 1);
    distance = Math.max(3, maxDim * 1.5);

    boxes.forEach(({ bbox, color, opacity, wireframe }) => {
      const min = toThree(bbox.min);
      const max = toThree(bbox.max);

      const dx = max.x - min.x || 0.1;
      const dy = max.y - min.y || 0.1;
      const dz = max.z - min.z || 0.1;
      const geometry = new THREE.BoxGeometry(dx, dy, dz);
      const material = new THREE.MeshBasicMaterial({
        color,
        opacity,
        transparent: true,
        wireframe: !!wireframe,
      });
      const mesh = new THREE.Mesh(geometry, material);
      mesh.position.set(
        (min.x + max.x) / 2,
        (min.y + max.y) / 2,
        (min.z + max.z) / 2
      );
      scene.add(mesh);
    });

    const light = new THREE.DirectionalLight(0xffffff, 0.8);
    light.position.set(5, 10, 7.5);
    scene.add(light);

    const animate = () => {
      const x = target.x + distance * Math.cos(rotationX) * Math.sin(rotationY);
      const y = target.y + distance * Math.sin(rotationX);
      const z = target.z + distance * Math.cos(rotationX) * Math.cos(rotationY);
      camera.position.set(x, y, z);
      camera.lookAt(target);

      renderer.render(scene, camera);
      requestAnimationFrame(animate);
    };
    animate();

    return () => {
      renderer.domElement.removeEventListener("mousedown", onMouseDown);
      window.removeEventListener("mousemove", onMouseMove);
      window.removeEventListener("mouseup", onMouseUp);
      renderer.domElement.removeEventListener("wheel", onWheel);
      renderer.dispose();
    };
  }, [recorded, current]);

  return (
    <div
      ref={containerRef}
      className="w-full h-40 border rounded bg-slate-100 dark:bg-slate-950 overflow-hidden"
    />
  );
}


