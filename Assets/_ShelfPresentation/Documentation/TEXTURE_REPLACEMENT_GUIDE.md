# Connected shelf-strip texture replacement

The presentation scene contains four rows. Each row is 4.4 m long and 2.2 m
high, split into two exact 2.2 m by 2.2 m logical/photo panels. The panels meet
at `z = 0` and include a 4 mm visual overlap to prevent sub-pixel cracks without
placing the front faces on competing planes.

## Material slots

| Row | First panel | Second panel |
| --- | --- | --- |
| `A_Left_Row` | `M_A_Left_01` / `A-L-01` | `M_A_Left_02` / `A-L-02` |
| `A_Right_Row` | `M_A_Right_01` / `A-R-01` | `M_A_Right_02` / `A-R-02` |
| `B_Left_Row` | `M_B_Left_01` / `B-L-01` | `M_B_Left_02` / `B-L-02` |
| `B_Right_Row` | `M_B_Right_01` / `B-R-01` | `M_B_Right_02` / `B-R-02` |

Create each connected row as one 4096 x 2048 source strip, then split it exactly
at the center into two 2048 x 2048 images. Keep horizon lines, shelf boards,
product scale, exposure, and color continuous across the split. Do not add a
border or padding at the shared edge.

Place final images in `Assets/_ShelfPresentation/Textures/`. In Unity, use:

- Texture Type: Default
- sRGB (Color Texture): enabled
- Alpha Source: None unless the image really contains transparency
- Wrap Mode: Clamp
- Filter Mode: Bilinear
- Max Size: 2048 per panel
- Compression: High Quality, or None for controlled computer-vision captures
- Generate Mip Maps: enabled for normal presentation; disable only when a fixed
  camera test proves mip filtering is reducing inference clarity

Assign each image to the Base Map of its named material under `Materials/`.
Shelf materials use an unlit URP shader so lighting does not recolor the photos.
Do not resize or move the panel objects when swapping textures; their geometry
is aligned with the corresponding `KnownShelfRegion` world corners.

After replacement, run `Tools > Shelf Location > Validate Presentation Scene`,
inspect the Game view, and confirm the center seam is continuous from the
stationary capture camera.
