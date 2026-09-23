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
// SpriteBatch binds the sprite to texture slot 0: SpriteSampler must stay the FIRST sampler declared in this effect.
texture SpriteTexture;
sampler SpriteSampler = sampler_state { Texture = <SpriteTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
texture LightTexture;
sampler LightSampler = sampler_state { Texture = <LightTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
#include "V8Ambient.fxh"
struct VSIn { float4 Position : POSITION0; float4 Color : COLOR0; float2 UV : TEXCOORD0; };
struct VSOut { float4 Position : SV_POSITION; float4 Color : COLOR0; float2 UV : TEXCOORD0; float2 LightUV : TEXCOORD1; float2 World : TEXCOORD2; };
VSOut VS(VSIn i) { VSOut o; o.Position = mul(i.Position, MatrixTransform); o.Color = i.Color; o.UV = i.UV; o.LightUV = (i.Position.xy - LightOrigin) / LightSize; o.World = i.Position.xy; return o; }
// Background, decorations, entities: direct + sky + local, the ambient reconstructed here from the tile grid.
float4 PS(VSOut i) : COLOR0 {
    float4 albedo = tex2D(SpriteSampler, i.UV) * i.Color;
    float3 sky, local;
    float layer;
    AmbientAt(i.World, sky, local, layer);
    float3 light = tex2D(LightSampler, i.LightUV).rgb + sky + local;
    return float4(albedo.rgb * light, albedo.a);
}
// Foreground terrain: the light texture is the face buffer, which already holds direct + sky + local.
float4 ForegroundPS(VSOut i) : COLOR0 {
    float4 albedo = tex2D(SpriteSampler, i.UV) * i.Color;
    return float4(albedo.rgb * tex2D(LightSampler, i.LightUV).rgb, albedo.a);
}
technique Receiver { pass P { VertexShader = compile VS_MODEL VS(); PixelShader = compile PS_MODEL PS(); } }
technique ForegroundReceiver { pass P { VertexShader = compile VS_MODEL VS(); PixelShader = compile PS_MODEL ForegroundPS(); } }
