#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 MatrixTransform;
float4 WaterShallowColor;
float4 WaterDeepColor;
float4 WaterSurfaceColor;
float WaterDepthStrength;

sampler TextureSampler : register(s0);

struct VertexInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

VertexOutput MainVS(VertexInput input)
{
    VertexOutput output;
    output.Position = mul(input.Position, MatrixTransform);
    output.Color = input.Color;
    output.TexCoord = input.TexCoord;
    return output;
}

float4 MainPS(VertexOutput input) : COLOR0
{
    float4 mask = tex2D(TextureSampler, input.TexCoord) * input.Color;
    float alpha = saturate(mask.a);
    clip(alpha - 0.001);

    float surfaceHint = saturate((mask.r - 0.16) * 7.5);
    float localDepth = smoothstep(0.05, 1.0, input.TexCoord.y) * WaterDepthStrength;

    float3 bodyColor = lerp(WaterShallowColor.rgb, WaterDeepColor.rgb, saturate(localDepth));
    float3 color = lerp(bodyColor, WaterSurfaceColor.rgb, surfaceHint);

    float finalAlpha = saturate(alpha * (0.90 + surfaceHint * 0.16));
    return float4(color * finalAlpha, finalAlpha);
}

technique Water
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
