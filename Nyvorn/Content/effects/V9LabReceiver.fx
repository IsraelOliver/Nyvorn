#if OPENGL
#define SV_POSITION POSITION
#define VS_MODEL vs_3_0
#define PS_MODEL ps_3_0
#else
#define VS_MODEL vs_4_0_level_9_1
#define PS_MODEL ps_4_0_level_9_1
#endif
float4x4 MatrixTransform;
float2 WorldSize;
float Response;
texture SpriteTexture;
sampler SpriteSampler = sampler_state { Texture = <SpriteTexture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
texture Irradiance;
sampler LightSampler = sampler_state { Texture = <Irradiance>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
struct Input { float4 Position : POSITION0; float4 Color : COLOR0; float2 UV : TEXCOORD0; };
struct Output { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; float2 World : TEXCOORD1; };
Output VS(Input i) { Output o; o.Position = mul(i.Position, MatrixTransform); o.UV = i.UV; o.World = i.Position.xy; return o; }
float3 Decode(float3 c) { return lerp(c / 12.92, pow((c + .055) / 1.055, 2.4), step(.04045, c)); }
float3 Encode(float3 c) { return lerp(c * 12.92, 1.055 * pow(max(c, 0), 1.0 / 2.4) - .055, step(.0031308, c)); }
float4 PS(Output i) : COLOR0
{
    float4 tex = tex2D(SpriteSampler, i.UV);
    float3 albedo = Decode(saturate(tex.rgb / max(tex.a, .00001)));
    float inside = step(0, i.World.x) * step(0, i.World.y) * (1-step(WorldSize.x, i.World.x)) * (1-step(WorldSize.y, i.World.y));
    float3 light = tex2D(LightSampler, i.World / WorldSize).rgb * Response * inside;
    float3 radiance = albedo * light;
    float3 mapped = radiance / (1 + max(radiance.r, max(radiance.g, radiance.b)));
    return float4(Encode(mapped) * tex.a, tex.a);
}
technique V9Receiver { pass P { VertexShader = compile VS_MODEL VS(); PixelShader = compile PS_MODEL PS(); } }
