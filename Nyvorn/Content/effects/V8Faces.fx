#if OPENGL
#define SV_POSITION POSITION
#define VS_MODEL vs_3_0
#define PS_MODEL ps_3_0
#else
#define VS_MODEL vs_4_0_level_9_1
#define PS_MODEL ps_4_0_level_9_1
#endif
float4x4 MatrixTransform;
texture DirectTexture;
sampler DirectSampler = sampler_state { Texture = <DirectTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
texture SkyTexture;
sampler SkySampler = sampler_state { Texture = <SkyTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
texture LocalTexture;
sampler LocalSampler = sampler_state { Texture = <LocalTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
float2 LightOrigin;
float2 LightSize;
struct VSIn { float4 Position : POSITION0; float4 Color : COLOR0; float2 UV : TEXCOORD0; };
struct VSOut { float4 Position : SV_POSITION; float4 Color : COLOR0; float2 UV : TEXCOORD0; float2 ReceiverUV : TEXCOORD1; };
VSOut VS(VSIn i) { VSOut o; o.Position = mul(i.Position, MatrixTransform); o.Color = i.Color; o.UV = i.UV; o.ReceiverUV = (i.Position.xy - LightOrigin) / LightSize; return o; }
float4 PS(VSOut i) : COLOR0 {
    // Alpha carries layer eligibility, not exposure. Veto the actual solid receiver too.
    float skyAllowed = tex2D(SkySampler, i.ReceiverUV).a > 0 ? 1 : 0;
    float3 light = tex2D(DirectSampler, i.UV).rgb + tex2D(LocalSampler, i.UV).rgb + skyAllowed * tex2D(SkySampler, i.UV).rgb;
    return float4(light * i.Color.r, 1);
}
technique Faces { pass P { VertexShader = compile VS_MODEL VS(); PixelShader = compile PS_MODEL PS(); } }
