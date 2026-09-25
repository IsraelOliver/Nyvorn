// V8 ambient reconstruction, shared by V8Receiver, V8Faces and V8AmbientResolve.
// Port of the former CPU V8AmbientField.Reconstruct, evaluated at the centre of each art pixel:
// a closed centre cell gives zero, closed cells get no weight, a diagonal cell is rejected when both
// orthogonal cells are closed (no bridge across a closed corner), weights renormalise, and the layer
// sky weight vetoes sky AFTER interpolation. Point sampling only: no hardware filtering crosses solids.
texture AmbientFieldTexture; // R sky exposure, G layer sky weight of the cell row, B open cell (transport cost > 0)
sampler AmbientFieldSampler = sampler_state { Texture = <AmbientFieldTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
texture AmbientLocalTexture; // RGB local fill divided by AmbientLocalScale
sampler AmbientLocalSampler = sampler_state { Texture = <AmbientLocalTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
float2 AmbientOrigin;      // world pixel of grid cell (0,0), minus the receiver's wrap copy offset
float2 AmbientCells;       // valid grid size in cells
float2 AmbientTextureSize; // texture size in cells (grow-only, >= AmbientCells)
float AmbientCellSize;
float AmbientLocalScale;
float3 AmbientSkyColor;
// Background wall only: exposure below this reads as zero and the rest is rescaled, giving the background its own
// visual range without touching the transport. 0 (every other receiver, the faces and the capture resolves) is a no-op.
float AmbientSkyCutoff;
// Composition prototype (diagnostic, 2026-09-23). 0 = shipped sum direct + sky + local. 1 = bounded, per channel:
// artificial T = D + A(1 - D), light L = S + T(1 - S). Each input is in [0, 1] (direct from an 8-bit target, local
// and sky saturated in AmbientAt), so L <= 1 and a receiver never exceeds its albedo.
float BoundedComposition;
float3 ComposeLight(float3 direct, float3 local, float3 sky)
{
    float3 artificial = direct + local * (1 - direct);
    float3 bounded = sky + artificial * (1 - sky);
    return lerp(direct + sky + local, bounded, BoundedComposition);
}

float4 AmbientFieldAt(float2 cell) { return tex2D(AmbientFieldSampler, (cell + 0.5) / AmbientTextureSize); }
float4 AmbientLocalAt(float2 cell) { return tex2D(AmbientLocalSampler, (cell + 0.5) / AmbientTextureSize); }

float AmbientInside(float2 cell)
{
    float2 inside = step(float2(0, 0), cell) * step(cell, AmbientCells - 1);
    return inside.x * inside.y;
}

// Layer sky weight of the art pixel's row (the former per-pixel alpha of the sky texture).
float AmbientLayerWeight(float2 worldPixel)
{
    float2 cell = floor((floor(worldPixel - AmbientOrigin) + 0.5) / AmbientCellSize);
    return AmbientFieldAt(cell).g * AmbientInside(cell);
}

void AmbientAt(float2 worldPixel, out float3 sky, out float3 local, out float layerWeight)
{
    float2 p = floor(worldPixel - AmbientOrigin) + 0.5;   // art pixel centre, grid-relative
    float2 cellF = p / AmbientCellSize;
    float2 c = floor(cellF);                              // centre cell
    float2 b = floor(cellF - 0.5);                        // bilinear 2x2 block origin
    float2 f = cellF - 0.5 - b;
    float2 k = c - b;                                     // centre position inside the block (0 or 1 per axis)
    float2 b10 = b + float2(1, 0);
    float2 b01 = b + float2(0, 1);
    float2 b11 = b + float2(1, 1);

    float4 fa = AmbientFieldAt(b);
    float4 fb = AmbientFieldAt(b10);
    float4 fc = AmbientFieldAt(b01);
    float4 fd = AmbientFieldAt(b11);
    float oa = fa.b * AmbientInside(b);
    float ob = fb.b * AmbientInside(b10);
    float oc = fc.b * AmbientInside(b01);
    float od = fd.b * AmbientInside(b11);

    float centre = lerp(lerp(oa, ob, k.x), lerp(oc, od, k.x), k.y);
    float sideX = lerp(lerp(oa, ob, 1 - k.x), lerp(oc, od, 1 - k.x), k.y);  // (other column, centre row)
    float sideY = lerp(lerp(oa, ob, k.x), lerp(oc, od, k.x), 1 - k.y);      // (centre column, other row)
    float diagonal = max(sideX, sideY);

    // The diagonal cell of the block is (1 - k.x, 1 - k.y).
    float wa = (1 - f.x) * (1 - f.y) * oa * lerp(1, diagonal, k.x * k.y);
    float wb = f.x * (1 - f.y) * ob * lerp(1, diagonal, (1 - k.x) * k.y);
    float wc = (1 - f.x) * f.y * oc * lerp(1, diagonal, k.x * (1 - k.y));
    float wd = f.x * f.y * od * lerp(1, diagonal, (1 - k.x) * (1 - k.y));
    float norm = centre / max(wa + wb + wc + wd, 0.000001);

    float exposure = (wa * fa.r + wb * fb.r + wc * fc.r + wd * fd.r) * norm;
    float3 fill = wa * AmbientLocalAt(b).rgb + wb * AmbientLocalAt(b10).rgb + wc * AmbientLocalAt(b01).rgb + wd * AmbientLocalAt(b11).rgb;
    local = saturate(fill * norm * AmbientLocalScale);

    layerWeight = lerp(lerp(fa.g, fb.g, k.x), lerp(fc.g, fd.g, k.x), k.y) * AmbientInside(c);
    float visible = saturate((min(exposure, layerWeight) - AmbientSkyCutoff) / max(1e-5, 1 - AmbientSkyCutoff));
    sky = saturate(AmbientSkyColor * visible);
}
