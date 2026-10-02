# Shelf Presentation Environment

`Market_Presentation.unity` is the photo-based demo scene. Its shelf faces are
eight lightweight photo panels arranged as four continuous rows. The existing
photos are temporary references only; no generated shelf artwork is included.

Use `Tools > Shelf Location > Build Presentation Market` to rebuild the scene
from `Market_01` while preserving the inference camera, bridge, client, HUD, and
the eight logical `KnownShelfRegion` IDs.

The camera is stationary. Runtime code does not translate or rotate `RobotRig`
or `Main Camera`. `ShelfInferenceClient` continues to capture from the mounted
camera on its normal timed inference loop.

See `Documentation/TEXTURE_REPLACEMENT_GUIDE.md` before replacing shelf images.
