# ProceduralCableSkin

![Demo](demo.gif)

Procedural cable / rope mesh generator for Unity.

This component builds a tubular mesh from ordered control points, supports linear and Catmull-Rom path interpolation, updates in Edit Mode and Play Mode, and can bake the result into a Mesh asset or Prefab.

## Features

- Generates a cable mesh from ordered point transforms
- Supports `Linear` and `CatmullRom` path modes
- Adjustable radius, side count, and UV tiling
- Optional double-sided geometry
- Auto-update in Edit Mode and Play Mode
- Custom inspector buttons for adding points
- Bake to Mesh asset
- Bake to Prefab

## How to use

1. Add `ProceduralCableSkin` to a GameObject with `MeshFilter` and `MeshRenderer`.
2. Assign ordered point transforms to the `points` array.
3. Tune:
   - `pathMode`
   - `stepsPerSegment`
   - `radius`
   - `sides`
   - `uvMultiply`
4. Use **Add Start** / **Add** in the inspector to create more points.
5. Use **Bake Mesh** to save a baked mesh asset.
6. Use **Bake Prefab** to save a baked prefab.

## Notes

- Editor-specific tools are wrapped in `#if UNITY_EDITOR`.
- The mesh is rebuilt automatically in Edit Mode and Play Mode if enabled.
- The generated mesh is assigned to the object's `MeshFilter`.

## File

- `ProceduralCableSkin.cs` — runtime component + custom editor inspector

## License

MIT
