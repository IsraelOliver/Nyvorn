#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 MatrixTransform;
float Time;
float4 WaterDeepColor;
float4 WaterLightColor;
float4 WaterFoamColor;
float4 WaterCausticColor;
float CausticIntensity;
float SurfaceFoamIntensity;

sampler TextureSampler : register(s0);

texture CausticTexture;
texture CausticHighlightTexture;
texture CausticThickTexture;

sampler CausticSampler : register(s1) = sampler_state
{
    Texture = <CausticTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

sampler CausticHighlightSampler : register(s2) = sampler_state
{
    Texture = <CausticHighlightTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

sampler CausticThickSampler : register(s3) = sampler_state
{
    Texture = <CausticThickTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

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
    float2 WorldPosition : TEXCOORD1;
};

VertexOutput MainVS(VertexInput input)
{
    VertexOutput output;
    output.Position = mul(input.Position, MatrixTransform);
    output.Color = input.Color;
    output.TexCoord = input.TexCoord;
    output.WorldPosition = input.Position.xy;
    return output;
}

float Hash21(float2 position)
{
    position = frac(position * float2(123.34, 456.21));
    position += dot(position, position + 45.32);
    return frac(position.x * position.y);
}

float ValueNoise(float2 position)
{
    float2 cell = floor(position);
    float2 local = frac(position);
    local = local * local * (3.0 - 2.0 * local);

    float bottomLeft = Hash21(cell);
    float bottomRight = Hash21(cell + float2(1.0, 0.0));
    float topLeft = Hash21(cell + float2(0.0, 1.0));
    float topRight = Hash21(cell + float2(1.0, 1.0));

    float bottom = lerp(bottomLeft, bottomRight, local.x);
    float top = lerp(topLeft, topRight, local.x);
    return lerp(bottom, top, local.y);
}

float FineCaustics(float2 worldPosition)
{
    float2 noiseUv = worldPosition * 0.026;
    float noiseA = ValueNoise(noiseUv + float2(Time * 0.075, Time * 0.032));
    float noiseB = ValueNoise(noiseUv * 1.73 + float2(-Time * 0.052, Time * 0.061));
    float2 shift = float2(noiseA - 0.5, noiseB - 0.5) * 0.012;

    float2 uvA = worldPosition * 0.0032 + float2(Time * 0.010, Time * 0.004) + shift;
    float2 uvB = worldPosition.yx * 0.0027 + float2(-Time * 0.007, Time * 0.010) + float2(shift.y, -shift.x);
    float2 uvThick = worldPosition * 0.0020 + float2(Time * 0.003, -Time * 0.004) - shift * 0.55;

    float mainA = tex2D(CausticSampler, uvA).a;
    float mainB = tex2D(CausticSampler, uvB).a;
    float thick = tex2D(CausticThickSampler, uvThick).a;
    float breakup = saturate(ValueNoise(noiseUv * 2.4 + float2(Time * 0.036, -Time * 0.044)) * 1.42 - 0.18);

    float fineLines = mainA * 0.46 + mainA * mainB * 0.38;
    return saturate((fineLines + thick * 0.12) * (0.45 + breakup * 0.32));
}

float TextureSparkle(float2 worldPosition)
{
    float2 noiseUv = worldPosition * 0.020;
    float noiseA = ValueNoise(noiseUv + float2(-Time * 0.034, Time * 0.047));
    float noiseB = ValueNoise(noiseUv * 1.9 + float2(Time * 0.027, -Time * 0.031));
    float2 shift = float2(noiseA - 0.5, noiseB - 0.5) * 0.014;

    float2 uvA = worldPosition * 0.0030 + float2(Time * 0.015, -Time * 0.007) + shift;
    float2 uvB = worldPosition.yx * 0.0034 + float2(-Time * 0.011, Time * 0.009) + float2(-shift.y, shift.x);
    float highlightA = tex2D(CausticHighlightSampler, uvA).a;
    float highlightB = tex2D(CausticHighlightSampler, uvB).a;
    float blink = saturate(0.52 + sin(Time * 2.7 + noiseA * 6.2831) * 0.48);

    return saturate(max(highlightA, highlightB * 0.72) * blink);
}

float SurfaceRipple(float2 worldPosition)
{
    float longRipple = sin(worldPosition.x * 0.085 + Time * 1.45);
    float shortRipple = sin(worldPosition.x * 0.19 - Time * 2.20 + worldPosition.y * 0.05);
    return saturate(0.50 + longRipple * 0.30 + shortRipple * 0.18);
}

float4 MainPS(VertexOutput input) : COLOR0
{
    float4 mask = tex2D(TextureSampler, input.TexCoord) * input.Color;
    float alpha = saturate(mask.a);
    clip(alpha - 0.001);

    float2 worldPosition = input.WorldPosition;
    float caustics = FineCaustics(worldPosition) * CausticIntensity;
    float sparkles = TextureSparkle(worldPosition) * (0.28 + caustics * 0.38);

    float surfaceHint = saturate((mask.r - 0.16) * 7.5);
    float surfaceRipple = SurfaceRipple(worldPosition);
    float waveLineLimit = saturate(0.48 + (surfaceRipple - 0.5) * 0.34);
    float surfaceLine = surfaceHint * (1.0 - smoothstep(waveLineLimit, waveLineLimit + 0.26, input.TexCoord.y));
    float foam = surfaceLine * SurfaceFoamIntensity * (0.36 + surfaceRipple * 0.34);

    float depthGradient = saturate(0.20 + ValueNoise(worldPosition * 0.014 + float2(0.0, Time * 0.018)) * 0.18);
    float bodyLight = saturate(depthGradient + caustics * 0.18 + surfaceLine * 0.34 + surfaceHint * 0.06);
    float3 color = lerp(WaterDeepColor.rgb, WaterLightColor.rgb, bodyLight);
    color += WaterCausticColor.rgb * (caustics * 0.32 + sparkles * 0.42 + surfaceLine * 0.16);
    color = lerp(color, WaterFoamColor.rgb, saturate(foam));

    float finalAlpha = saturate(alpha * (0.92 + surfaceLine * 0.18));
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
