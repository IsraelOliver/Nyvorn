#if OPENGL
#define SV_POSITION POSITION
#define VS_MODEL vs_3_0
#define PS_MODEL ps_3_0
#else
#define VS_MODEL vs_4_0_level_9_1
#define PS_MODEL ps_4_0_level_9_1
#endif
#include "V8Ambient.fxh"
float4x4 MatrixTransform;
texture DirectTexture;
sampler DirectSampler = sampler_state { Texture = <DirectTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
float2 LightOrigin;
float2 LightSize;
// Vertices are world anchored (V8FaceChunks): TextureCoordinate is the world centre of the face's first
// external air pixel, so cached chunks stay valid while the camera and the light bounds move.
struct VSIn { float4 Position : POSITION0; float4 Color : COLOR0; float2 UV : TEXCOORD0; };
struct VSOut { float4 Position : SV_POSITION; float4 Color : COLOR0; float2 DirectUV : TEXCOORD0; float2 Sample : TEXCOORD1; float2 Receiver : TEXCOORD2; };
VSOut VS(VSIn i) {
    VSOut o;
    o.Position = mul(i.Position, MatrixTransform);
    o.Color = i.Color;
    o.DirectUV = (i.UV - LightOrigin) / LightSize;
    o.Sample = i.UV;
    o.Receiver = i.Position.xy;
    return o;
}
float4 PS(VSOut i) : COLOR0 {
    float3 sky, local;
    float sampleLayer;
    AmbientAt(i.Sample, sky, local, sampleLayer);
    // Layer eligibility of the actual solid receiver vetoes sky (formerly the sky texture alpha at the receiver).
    float skyAllowed = AmbientLayerWeight(i.Receiver) > 0 ? 1 : 0;
    // Color.r weights direct + local (quadratic over FaceWidth); Color.g weights sky (smooth band over SkyFaceDepth).
    float3 light = (tex2D(DirectSampler, i.DirectUV).rgb + local) * i.Color.r + skyAllowed * sky * i.Color.g;
    return float4(light, 1);
}
technique Faces { pass P { VertexShader = compile VS_MODEL VS(); PixelShader = compile PS_MODEL PS(); } }
