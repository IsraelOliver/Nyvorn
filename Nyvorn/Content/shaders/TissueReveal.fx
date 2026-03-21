#if OPENGL
	#define SV_POSITION POSITION
	#define PS_SHADERMODEL ps_3_0
#else
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float Time;
float RevealStrength;
float2 ScreenSize;
float2 FocusScreenPosition;
float RevealRadiusPixels;
float WaveProgress;
float LayerMode;
float LayerOpacity;

Texture2D SpriteTexture;

sampler2D SpriteTextureSampler = sampler_state
{
	Texture = <SpriteTexture>;
};

float4 DeepColor = float4(0.11f, 0.27f, 0.36f, 1.0f);
float4 BaseColor = float4(0.31f, 0.88f, 0.92f, 1.0f);
float4 PulseColor = float4(0.90f, 0.98f, 1.0f, 1.0f);

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color : COLOR0;
	float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR0
{
	float2 uv = input.TextureCoordinates;
	float a = tex2D(SpriteTextureSampler, uv).a;
	if (a <= 0.001f || RevealStrength <= 0.001f)
		return float4(0, 0, 0, 0);

	float2 texel = 1.0f / ScreenSize;
	float sampleRight = tex2D(SpriteTextureSampler, uv + float2(texel.x, 0.0f)).a;
	float sampleLeft = tex2D(SpriteTextureSampler, uv - float2(texel.x, 0.0f)).a;
	float sampleUp = tex2D(SpriteTextureSampler, uv - float2(0.0f, texel.y)).a;
	float sampleDown = tex2D(SpriteTextureSampler, uv + float2(0.0f, texel.y)).a;
	float halo = max(max(sampleRight, sampleLeft), max(sampleUp, sampleDown));
	float nodeCore = saturate((a - 0.72f) * 3.4f);

	float2 screenPosition = uv * ScreenSize;
	float distanceToFocus = distance(screenPosition, FocusScreenPosition);
	float radialNormalized = distanceToFocus / max(RevealRadiusPixels, 1.0f);
	float radialFade = 1.0f - smoothstep(0.18f, 1.0f, radialNormalized);
	float waveRadius = max(RevealRadiusPixels, 1.0f) * saturate(WaveProgress);
	float waveFeather = 20.0f;
	float frontWidth = 16.0f;
	float trailWidth = 46.0f;
	float revealedByWave = 1.0f - smoothstep(waveRadius - waveFeather, waveRadius + waveFeather, distanceToFocus);
	float frontOuter = 1.0f - smoothstep(waveRadius, waveRadius + frontWidth, distanceToFocus);
	float frontInner = 1.0f - smoothstep(waveRadius - frontWidth, waveRadius, distanceToFocus);
	float waveFront = saturate(frontOuter - frontInner);
	float trailOuter = 1.0f - smoothstep(waveRadius - frontWidth, waveRadius + 4.0f, distanceToFocus);
	float trailInner = 1.0f - smoothstep(waveRadius - (trailWidth + frontWidth), waveRadius - 6.0f, distanceToFocus);
	float waveTrail = saturate(trailOuter - trailInner) * (1.0f - waveFront);

	float pulse = 0.80f + (0.20f * sin(Time * 4.0f + uv.x * 18.0f));
	float waveVisibility = max(revealedByWave, waveFront * 0.95f);
	float visibility = waveVisibility * radialFade;
	float frontBoost = 1.0f + (waveFront * 2.4f);
	float trailBoost = 1.0f + (waveTrail * 0.85f);
	float coreIntensity = a * RevealStrength * pulse * visibility * frontBoost * trailBoost * max(LayerOpacity, 0.001f);
	float haloIntensity = halo * RevealStrength * 0.38f * visibility * (1.0f + waveFront * 1.2f + waveTrail * 0.6f) * max(LayerOpacity, 0.001f);

	float3 coreColor = float3(0.25f, 0.95f, 1.0f) * coreIntensity * lerp(1.1f, 3.0f, radialFade);
	float3 haloColor = float3(0.08f, 0.45f, 0.55f) * haloIntensity * 1.8f;
	float3 nodeColor = PulseColor.rgb * nodeCore * RevealStrength * visibility * (1.0f + waveFront * 1.6f) * lerp(0.45f, 1.25f, radialFade);
	float3 trailColor = DeepColor.rgb * waveTrail * RevealStrength * radialFade * 0.95f;
	float3 frontHaloColor = BaseColor.rgb * waveFront * RevealStrength * radialFade * 1.25f;
	float3 frontCoreColor = PulseColor.rgb * waveFront * RevealStrength * radialFade * 2.20f;
	float3 color = haloColor + coreColor + nodeColor + trailColor + frontHaloColor + frontCoreColor;
	return float4(color, 1.0f);
}

technique SpriteDrawing
{
    pass P0
    {
		PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
