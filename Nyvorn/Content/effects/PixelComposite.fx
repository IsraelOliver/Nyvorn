#if OPENGL
    #define SV_POSITION POSITION
    #define sample2D(textureName, uv) tex2D(textureName##Sampler, uv)
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define sample2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
    #define sampler2D SamplerState
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 MatrixTransform;

// V7: light texels are stored as value / OverbrightScale (0..1). Multiplied back here so
// light can exceed 1.0 (torches "warm" the scene instead of only tinting it). Legacy passes 1.0.
float OverbrightScale = 1.0;
// V7: 0 = off, N = quantize the light to N steps (optional pixel-art look)
float PosterizeLevels = 0.0;

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

// P1C-3B: Light buffer texture for pixel-resolution lighting
// Cross-backend compatible multi-texture support
Texture2D LightBuffer;
sampler2D LightBufferSampler = sampler_state
{
    Texture = <LightBuffer>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

VertexShaderOutput MainVS(VertexShaderInput input)
{
    VertexShaderOutput output;
    output.Position = mul(input.Position, MatrixTransform);
    output.Color = input.Color;
    output.TextureCoordinates = input.TextureCoordinates;
    return output;
}

float4 MainPS(VertexShaderOutput input) : COLOR0
{
    // P1C-4: Final pixel composite - WorldColorRT × PixelLightBuffer
    float2 uv = input.TextureCoordinates;

    float4 world = sample2D(SpriteTexture, uv) * input.Color;
    float3 light = sample2D(LightBuffer, uv).rgb * OverbrightScale;
    float levels = max(PosterizeLevels, 1.0);
    float3 posterized = floor(light * levels + 0.5) / levels;
    light = (PosterizeLevels > 0.5) ? posterized : light;

    float4 result;
    result.rgb = world.rgb * light;
    result.a = world.a;

    return result;
}

technique SpriteDrawing
{
    pass SpriteDrawingPass
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
