# Naval Deck Cannon

Flat-shaded, vertex-colored cannon matched to the approved V8 steamer palette.
Blender coordinates: Z up, muzzle toward -Y. Root is on the deck plane.
Approximate footprint: 1.22 wide by 2.18 long; height 1.28.

## Structure

- `Cannon_Root`: placement and whole-carriage recoil.
- `Elevation_Pivot`: local X elevation; default four degrees upward.
- `Barrel`: continuous profile, reinforcing rings and recessed bore.
- `Muzzle_Socket`: child of elevation pivot; local +Z points out of the bore.
- Four `Truck_Wheel` meshes: centered origins for local X wheel rotation.
- Carriage: shaped wooden cheeks, circular bearing saddles, iron caps,
  axles, cross timbers and an elevation wedge.

The FBX retains editable pieces and vertex colors. Assign a vertex-color-aware
material in Unity; no production prefab or scene has been changed. Review
renders use Blender Workbench, not the Unity shader. No iOS performance test
or runtime animation integration is included.

`ship-fit.png` and `deck-fit.png` show temporary placement on the actual V8
ship. These placements are presentation checks, not a final gameplay layout.
Mesh checks cover manifold edges and degenerate faces. Separate parts meet
at physical joints; exhaustive collision checks over animation are not included.
`export-verification.json` records an independent FBX reimport check.
