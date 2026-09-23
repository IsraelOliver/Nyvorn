#if OPENGL
#define SV_POSITION POSITION
#define VS_MODEL vs_3_0
#define PS_MODEL ps_3_0
#else
#define VS_MODEL vs_4_0_level_9_1
#define PS_MODEL ps_4_0_level_9_1
#endif
#include "V8Ambient.fxh"
// Captures/validation only: writes the per-art-pixel sky (alpha = layer weight) and local buffers the renderer used
// to upload from the CPU, using the same reconstruction as the receivers and faces.
float4x4 MatrixTransform;
struct VSIn { float4 Position : POSITION0; float4 Color : COLOR0; float2 UV : TEXCOORD0; };
struct VSOut { float4 Position : SV_POSITION; float2 World : TEXCOORD0; };
VSOut VS(VSIn i) { VSOut o; o.Position = mul(i.Position, MatrixTransform); o.World = i.Position.xy; return o; }
float4 SkyPS(VSOut i) : COLOR0 {
    float3 sky, local;
    float layer;
    AmbientAt(i.World, sky, local, layer);
    return float4(sky, layer);
}
float4 LocalPS(VSOut i) : COLOR0 {
    float3 sky, local;
    float layer;
    AmbientAt(i.World, sky, local, layer);
    return float4(local, 1);
}
technique Sky { pass P { VertexShader = compile VS_MODEL VS(); PixelShader = compile PS_MODEL SkyPS(); } }
technique Local { pass P { VertexShader = compile VS_MODEL VS(); PixelShader = compile PS_MODEL LocalPS(); } }
