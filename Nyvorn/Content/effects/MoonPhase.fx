#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 MatrixTransform;
float2 MoonPosition;
float MoonRadius;
float4 MoonColor;
float Opacity;

// Phase/terminator (MoonDisc technique).
float Phase01;       // 0/1 = new moon, 0.5 = full moon
float ResidualGlow;  // dark-side minimum brightness, 0..1 - never pure black

// Atmospheric bloom (separate additive pass - MoonBloom technique below), same idea as the sun's
// SunBloom in SunRays.fx: brightens the sky around the moon instead of occluding it.
float BloomRadius;
float BloomIntensity;

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
    float2 ScreenPosition : TEXCOORD1;
};

VertexOutput MainVS(VertexInput input)
{
    VertexOutput output;
    output.Position = mul(input.Position, MatrixTransform);
    output.Color = input.Color;
    output.TexCoord = input.TexCoord;
    output.ScreenPosition = input.Position.xy;
    return output;
}

float4 MoonDiscPS(VertexOutput input) : COLOR0
{
    float2 delta = input.ScreenPosition - MoonPosition;
    float dist = length(delta);
    float discMask = 1.0 - smoothstep(MoonRadius * 0.9, MoonRadius, dist);

    // Curved terminator via two same-radius circles (classic moon-phase trick) instead of a
    // straight clipped line: a "shadow" circle the same size as the moon, offset horizontally by
    // an amount driven by Phase01. Where the shadow circle does NOT reach is the lit region, which
    // naturally traces a lens-shaped curve as the offset changes.
    float fullness = 0.5 - (0.5 * cos(6.2831853 * Phase01));
    float shadowOffset = MoonRadius * 2.0 * fullness;
    float2 shadowCenter = MoonPosition + float2(shadowOffset, 0.0);
    float shadowDist = length(input.ScreenPosition - shadowCenter);
    float shadowMask = 1.0 - smoothstep(MoonRadius * 0.9, MoonRadius, shadowDist);

    float litAmount = saturate(1.0 - shadowMask);
    float brightness = lerp(ResidualGlow, 1.0, litAmount);

    float alpha = discMask * Opacity;
    float3 rgb = MoonColor.rgb * brightness;

    return float4(rgb * alpha, alpha) * input.Color.a;
}

float4 MoonBloomPS(VertexOutput input) : COLOR0
{
    float2 delta = input.ScreenPosition - MoonPosition;
    float dist = length(delta);

    float sigma = max(MoonRadius * BloomRadius, 1.0);
    float bloom = exp(-(dist * dist) / (2.0 * sigma * sigma));

    float strength = bloom * BloomIntensity * Opacity * input.Color.a;
    return float4(MoonColor.rgb, strength);
}

technique MoonDisc
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MoonDiscPS();
    }
}

technique MoonBloom
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MoonBloomPS();
    }
}
