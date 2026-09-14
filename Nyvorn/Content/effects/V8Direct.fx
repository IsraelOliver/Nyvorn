#if OPENGL
#define SV_POSITION POSITION
#define VS_MODEL vs_3_0
#define PS_MODEL ps_3_0
#else
#define VS_MODEL vs_4_0_level_9_1
#define PS_MODEL ps_4_0_level_9_1
#endif
float4x4 MatrixTransform;
float2 SourcePosition;
float Radius;
float3 Radiance;
struct VSIn { float4 Position : POSITION0; float4 Color : COLOR0; float2 UV : TEXCOORD0; };
struct VSOut { float4 Position : SV_POSITION; float2 World : TEXCOORD0; };
VSOut VS(VSIn i) { VSOut o; o.Position = mul(i.Position, MatrixTransform); o.World = i.Position.xy; return o; }
float4 PS(VSOut i) : COLOR0 {
    float t = saturate(1.0 - distance(i.World, SourcePosition) / Radius);
    return float4(Radiance * t * t, 0);
}
technique Direct { pass P { VertexShader = compile VS_MODEL VS(); PixelShader = compile PS_MODEL PS(); } }
