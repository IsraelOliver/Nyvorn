#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 MatrixTransform;
sampler TestSampler : register(s0);

struct VertexInput { float4 Position : POSITION0; float4 Color : COLOR0; float2 TexCoord : TEXCOORD0; };
struct VertexOutput { float4 Position : SV_Position; float4 Color : COLOR0; float2 TexCoord : TEXCOORD0; };

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
    float value = 0.0;
    [unroll(32)]
    for (int i = 0; i < 32; i++)
    {
        value += tex2D(TestSampler, float2((float)i/32.0, 0.5)).r / 32.0;
    }
    return float4(value, value, value, 1.0);
}

technique Main
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
