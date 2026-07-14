#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 MatrixTransform;
float2 SunPosition;
float SunRadius;
float4 SunColor;
float RayIntensity;
float GlowIntensity;
float Seed;
float Time;             // skyState.VisualTimeSeconds - drives ray rotation/shimmer over time

// Ray shape/irregularity - tunable from C# without touching the math below.
float RayReach;         // how many SunRadius units the rays extend before fading out
float RayNoiseAmount;   // 0 = perfectly even rays, higher = more length/strength variation per angle
float RayRotationSpeed; // radians/second the whole ray field slowly turns
float RayShimmerSpeed;  // how fast the per-angle noise drifts, making rays writhe/flicker
float CoreIntensity;    // fraction of the disc radius that stays pure blown-out white
float Warmth;           // GetSunWarmth(skyState) - drives white-to-warm interpolation
float4 WarmTipColor;    // skyState.HorizonColor - what ray tips/disc rim lerp toward

// Atmospheric bloom - drawn in a separate additive pass (SunBloom technique below) so it adds
// light onto the sky instead of occluding it the way the alpha-blended core/rays do.
float BloomRadius;      // gaussian sigma, in SunRadius units - much larger than the ray reach
float BloomIntensity;   // additive strength at the sun's center

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

float4 MainPS(VertexOutput input) : COLOR0
{
    float2 delta = input.ScreenPosition - SunPosition;
    float dist = length(delta);
    float angle = atan2(delta.y, delta.x);

    // Core: blown-out pure white at the center, only saturating toward SunColor/WarmTipColor at
    // the rim - reads as an overexposed light source instead of one flat-colored disc.
    float coreMask = 1.0 - smoothstep(SunRadius * 0.82, SunRadius, dist);
    float centerHeat = 1.0 - smoothstep(0.0, SunRadius * max(CoreIntensity, 0.05), dist);
    float3 rimColor = lerp(SunColor.rgb, WarmTipColor.rgb, Warmth * 0.6);
    float3 coreColor = lerp(rimColor, float3(1.0, 1.0, 1.0), centerHeat);

    // Small edge-softening halo right around the disc, distinct from the big atmospheric bloom
    // pass - keeps the disc's edge from looking hard-cut against the ray field.
    float haloFalloff = saturate(1.0 - (dist / (SunRadius * 4.2)));
    float halo = pow(haloFalloff, 2.0);

    // Layered ray pattern: 3 sine waves at different frequencies AND phases (not just scaled by
    // Seed), each further modulated by slow angle-dependent noise so reach and strength vary per
    // direction - some rays read long and faint, others short and bright, instead of a uniform fan.
    // The whole field slowly rotates (RayRotationSpeed) and the noise phase drifts over time
    // (RayShimmerSpeed), so rays gently turn AND writhe/flicker instead of sitting frozen.
    float rotatedAngle = angle + (Time * RayRotationSpeed);
    float shimmer = Time * RayShimmerSpeed;

    float reachNoise = 0.5 + (0.5 * sin((rotatedAngle * 5.0) + (Seed * 3.1) + (shimmer * 0.8)));
    float perAngleReach = RayReach * lerp(1.0 - RayNoiseAmount, 1.0 + RayNoiseAmount, reachNoise);

    float ampNoiseA = 0.5 + (0.5 * sin((rotatedAngle * 7.0) + (Seed * 2.2) + 0.7 + (shimmer * 1.3)));
    float ampNoiseB = 0.5 + (0.5 * sin((rotatedAngle * 13.0) + (Seed * 4.6) + 2.1 + (shimmer * 0.5)));

    float wide = pow(saturate(sin((rotatedAngle * 3.0) + Seed) * 0.5 + 0.5), 5.0)
        * lerp(1.0 - RayNoiseAmount, 1.0 + RayNoiseAmount, ampNoiseA);
    float mid = pow(saturate(sin((rotatedAngle * 9.0) + (Seed * 1.7) + 1.4) * 0.5 + 0.5), 9.0)
        * lerp(1.0 - RayNoiseAmount, 1.0 + RayNoiseAmount, ampNoiseB);
    float fine = pow(saturate(sin((rotatedAngle * 31.0) + (Seed * 2.9) + 3.6) * 0.5 + 0.5), 13.0)
        * lerp(1.0 - RayNoiseAmount, 1.0 + RayNoiseAmount, reachNoise);
    float rayPattern = saturate((wide * 0.6) + (mid * 0.32) + (fine * 0.22));

    float safeReach = max(perAngleReach, 1.0);
    float rayFalloff = pow(saturate(1.0 - (dist / (SunRadius * safeReach))), 1.35);
    float rayProximity01 = saturate(dist / (SunRadius * safeReach));

    // Rays fade IN starting just past the disc's own edge instead of having full strength right at
    // dist=0 - without this gate, the sharp per-angle ray peaks bleed into the coreMask's
    // transition band and make the disc's silhouette look notched/non-circular instead of round.
    float rayStartGate = smoothstep(SunRadius * 0.95, SunRadius * 1.6, dist);

    // Ponta quente: each ray lerps from white near the disc to the horizon's warm color at its
    // far tip, scaled by how much "golden hour" warmth is currently in the sky.
    float3 rayColor = lerp(float3(1.0, 1.0, 1.0), WarmTipColor.rgb, rayProximity01 * Warmth);
    float rayStrength = rayPattern * rayFalloff * RayIntensity * rayStartGate;

    float alpha = saturate(coreMask + (halo * GlowIntensity * 0.6) + rayStrength);
    float3 rgb = (coreColor * coreMask) + (SunColor.rgb * halo * GlowIntensity * 0.6) + (rayColor * rayStrength);

    return float4(rgb, alpha) * input.Color.a;
}

// Wide, soft gaussian glow with a much larger radius than the rays - meant to brighten/desaturate
// the sky around the sun toward warm-white instead of sitting as a hard-edged sticker on top of
// it. Drawn with BlendState.Additive from C#, so this pass only ever ADDS light to the sky.
float4 BloomPS(VertexOutput input) : COLOR0
{
    float2 delta = input.ScreenPosition - SunPosition;
    float dist = length(delta);

    float sigma = max(SunRadius * BloomRadius, 1.0);
    float bloom = exp(-(dist * dist) / (2.0 * sigma * sigma));

    float3 bloomColor = lerp(float3(1.0, 1.0, 1.0), WarmTipColor.rgb, 0.4 * Warmth);
    float strength = bloom * BloomIntensity * input.Color.a;

    return float4(bloomColor, strength);
}

technique SunRays
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

technique SunBloom
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL BloomPS();
    }
}
