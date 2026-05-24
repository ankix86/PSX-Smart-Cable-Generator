# PSX Smart Cable Generator

A small **Unity Editor** helper for making low-poly cable meshes — the kind that fit a PS1/PSX look. You place anchors in the scene, tweak sliders, preview the shape, then save a mesh asset. At **play time** it’s just a normal `MeshFilter` + `MeshRenderer`; nothing keeps regenerating in the background.

---

## What it does

- Builds a **tube** or flat **ribbon** along a path between scene transforms.
- Smooths the path with **Catmull–Rom** splines and adds **sag** so cables hang naturally.
- Optional **PSX-style** touches: low side count, vertex wobble, grid snap.
- Optional **raycasts** to nudge around obstacles or snap toward a floor layer.
- **Presets** (Ethernet, power cable, thick industrial, hanging wire) to get close fast, then tweak by hand.

Mesh generation is meant for **edit mode only**. You bake the result into a `.asset` file and use it like any other static mesh.

---

## Quick start

1. Copy this folder into your Unity project (for example `Assets/PSXCable/`).
2. Add a **custom editor** if your copy doesn’t include it yet — the runtime component expects editor-side preview/save (referenced as `PSCableEditor` in code comments).
3. Create an empty GameObject and add **`PSCableGenerator`**.
4. Set up the path (see workflow below).
5. Adjust shape/mesh/PSX settings in the Inspector.
6. Use the editor buttons to **preview** and **save** the mesh under `Assets/_Generated/PSXCables/` (folder is configurable).

Assign a material on the `MeshRenderer` like any other mesh object.

---

## Workflow (simple)

Think of it in three steps: **path → shape → bake**.

### 1. Define the path

**Option A — anchor chain (recommended)**  
Add transforms to **Path Anchors** in order: point 1 → 2 → 3 → …  
The cable visits each one in sequence. With two or more valid anchors, this mode is used automatically.

**Option B — start and end (legacy)**  
Set **Cable Start** and **Cable End**, and optionally **Manual World Knots** for extra bends.

Turn on **Add Auto Mid Knot** if you want a automatic dip between each pair of points (nice for slack). **Curve Tension** controls how straight vs droopy that mid point is.

### 2. Shape the look

- Pick a **Preset** for a starting point, or leave **Custom**.
- **Sag Strength** / **Sag Asymmetry** — how much it droops and whether one side hangs lower.
- **Path Resolution** — more samples = smoother curve, more vertices.
- **Ribbon Mode** — flat strip instead of a round tube.
- **Cable Thickness** — diameter (tube) or width (ribbon).
- **Radial Segments** — keep low (3–6) for a chunky PSX silhouette.
- **Random Seed** / **Randomize Strength** — small variations when you want cables not to look cloned.

Optional environment helpers:

- **Avoid Obstacles** — raycasts and inserts detour points (editor-time physics).
- **Snap To Floor** — raycasts down to a floor layer.

### 3. Preview and save

With **Auto Preview In Editor** on, changing values can refresh the mesh in the Inspector (handled by the custom editor).

When you’re happy:

- Set **Mesh Asset Name** and **Save Folder Relative**.
- **Unique Name Per Save** avoids overwriting old assets (adds a suffix each time).
- Save — you get a mesh asset you can reuse, duplicate, or reference on prefabs.

---

## Presets

| Preset | Rough idea |
|--------|------------|
| **Ethernet** | Thin round cable, moderate sag |
| **Power Cable** | Medium thickness, a bit more sag |
| **Industrial Thick** | Heavy cable, fewer segments feel |
| **Hanging Wire** | Very thin **ribbon**, more hang |
| **Custom** | Your own values; presets don’t override after you’ve tuned |

Presets only set starting numbers on the component; you can still change anything afterward.

---

## Project layout

| File | Role |
|------|------|
| `PSCableGenerator.cs` | Main component: anchors, settings, path + mesh build API |
| `PSCablePathUtility.cs` | Spline, sag, obstacle detours, floor snap |
| `PSCableMeshBuilder.cs` | Tube / ribbon mesh along the path |
| `PSCablePreset.cs` | Preset enum and default values |

---

## Runtime behavior

After you save a mesh and assign it to the `MeshFilter`, the cable behaves like static geometry — **no per-frame cable math**. That’s intentional for PSX-style scenes where you want cheap draw calls, not simulated ropes.

---

## Requirements

- Unity (project tested conceptually as an editor workflow tool; use a version your team already ships on).
- Colliders / layers in the scene if you use obstacle avoidance or floor snap (editor raycasts only).

---

## Tips

- Parent empty objects along walls, poles, or plugs — drag them into **Path Anchors** instead of typing world positions.
- Start from a preset, then switch to **Custom** mentally once you’re one or two tweaks away.
- Low **Radial Segments** + **Psx Vertex Wobble** + small **Vertex Snap Grid** sell the retro look more than high poly smooth tubes.

---

## License

Add your license here if you publish the repo (MIT, etc.).
