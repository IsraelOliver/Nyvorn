#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 MatrixTransform;
float LightingIntensity = 1.0;
float SunlightIntensity = 0.5;

// s0 = SceneTexture (color + alpha)
// s1 = LightingMaskTexture (r = mask, 0.0 = no lighting, 1.0 = full lighting)
// s2 = LightingMapTexture (ambient BFS light, computed per frame)
// s3 = DirectionalSunlightMap (directional sunlight from sun position)
sampler SceneSampler : register(s0);
sampler MaskSampler : register(s1);
sampler LightSampler : register(s2);
sampler SunlightSampler : register(s3);

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
    // Sample textures
    float4 scene = tex2D(SceneSampler, input.TexCoord);
    float mask = tex2D(MaskSampler, input.TexCoord).r;
    float4 ambientLight = tex2D(LightSampler, input.TexCoord);
    float4 sunlight = tex2D(SunlightSampler, input.TexCoord);

    // Apply vertex color
    scene *= input.Color;

    // Combine ambient (BFS) and directional (sun) lighting
    // ambientLight: BFS-computed local + environment light
    // sunlight: directional light from sun position
    float4 combinedLight = ambientLight + (sunlight * SunlightIntensity);

    // Composite lighting using mask:
    // - mask = 0.0 (atmosphere): no lighting applied
    // - mask = 1.0 (world): full lighting applied
    float4 litScene = scene * combinedLight * LightingIntensity;
    float4 finalRgb = lerp(scene, litScene, mask);

    // Preserve original alpha
    finalRgb.a = scene.a;

    return finalRgb;
}

technique ComposeLighting
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
