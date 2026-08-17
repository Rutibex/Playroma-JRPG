# Node Reference

## Sources

- **Constant Color** — solid RGBA field.
- **Constant Scalar** — uniform floating-point field.
- **Coordinates** — normalized X, Y, radial distance, and angle.
- **White Noise** — independent deterministic values.
- **Poisson Disc Points** — separated point mask with toroidal distance.
- **Image Input** — portable exemplar or authored source.

## Noise

- **Procedural Noise** supports value, Perlin-style gradient, cellular, fBm, turbulence, billow, ridged, and domain-warped modes. Cellular mode also exposes edge-distance and cell-ID fields.

## Patterns

- **Pattern Generator** supports checker, stripes, bricks, running bond, herringbone, planks, floor tiles, shingles, cobblestone, flagstone, hexagons, grates, riveted panels, circuits, cloth, dots, waves, dunes, sediment, cracks, and veins.

## Transform and colour

- **Transform / Warp** — wrapped translation, scale, rotation, mirroring, and scalar displacement.
- **Gradient Map** — scalar-to-palette conversion.
- **Blend** — normal, multiply, screen, overlay, soft/hard light, add, subtract, difference, darken, lighten, minimum, maximum, and masked opacity.
- **Color Adjustment** — brightness, contrast, saturation, hue, gamma, and posterization.
- **Shade From Height** — directional lighting derived from a height field.

## Filtering

- **Image Filter** — blur, directional blur, median, sharpen, emboss, Sobel, high pass, low pass, and local contrast.
- **Morphology** — dilate, erode, open, close, outline, bevel, slope, curvature, and ambient-occlusion approximation.
- **Threshold / Levels** — hard or smooth range remapping.

## Simulation

- **Reaction Diffusion** — Gray–Scott activator/inhibitor system.
- **Erosion / Weathering** — thermal relaxation plus wear and moisture channels.
- **Organic Growth** — cellular, aggregation, branching, crystal, fungal, and root growth.
- **Flow Field** — height-gradient or procedural curl-like motion fields.

## Synthesis

- **Seam Repair** — blended or mirrored edge reconciliation.
- **Image Quilting** — overlap-cost patch assembly.
- **PatchMatch Inpaint** — stochastic patch-based filling from known regions.

## Pixel art

- **Pixel Art Finish** — reduction, quantization, dithering, isolated-pixel cleanup, and alpha snapping.

## Output

- **Material Output** — BaseColor, Alpha, Height, Roughness, Emission, DecorationProbability, Wear, Moisture, FlowX, and FlowY.
