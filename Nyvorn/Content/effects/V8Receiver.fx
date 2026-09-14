#if OPENGL
#define SV_POSITION POSITION
#define VS_MODEL vs_3_0
#define PS_MODEL ps_3_0
#else
#define VS_MODEL vs_4_0_level_9_1
#define PS_MODEL ps_4_0_level_9_1
#endif
float4x4 MatrixTransform;
float2 LightOrigin;
float2 LightSize;
texture SpriteTexture;
sampler SpriteSampler = sampler_state { Texture = <SpriteTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
texture LightTexture;
sampler LightSampler = sampler_state { Texture = <LightTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
texture SkyTexture;
sampler SkySampler = sampler_state { Texture = <SkyTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
texture LocalTexture;
sampler LocalSampler = sampler_state { Texture = <LocalTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
float AmbientScale;
struct VSIn { float4 Position : POSITION0; float4 Color : COLOR0; float2 UV : TEXCOORD0; };
struct VSOut { float4 Position : SV_POSITION; float4 Color : COLOR0; float2 UV : TEXCOORD0; float2 LightUV : TEXCOORD1; };
VSOut VS(VSIn i) { VSOut o; o.Position = mul(i.Position, MatrixTransform); o.Color = i.Color; o.UV = i.UV; o.LightUV = (i.Position.xy - LightOrigin) / LightSize; return o; }
float4 PS(VSOut i) : COLOR0 {
    float4 albedo = tex2D(SpriteSampler, i.UV) * i.Color;
    float3 light = tex2D(LightSampler, i.LightUV).rgb + AmbientScale *
        (tex2D(SkySampler, i.LightUV).rgb + tex2D(LocalSampler, i.LightUV).rgb);
    return float4(albedo.rgb * light, albedo.a);
}
technique Receiver { pass P { VertexShader = compile VS_MODEL VS(); PixelShader = compile PS_MODEL PS(); } }
