// UIA/HudBloom - Stage 2 HUD bloom for Stationeers UI Ascended (0.9.0).
//
// NOT a UI shader. A plain image-effect (blit) shader that lets bright HUD pixels light their
// neighbours: the HUD is rendered into a RenderTexture (all curvature modes route through one),
// this shader extracts the bright parts, HudBlur.shader blurs them down/up a pyramid, and Pass 1
// composites the glow ADDITIVELY back onto that same HUD RT. Every presentation (dome grid,
// curved-flat, forced-flat) then carries the bloom, warped consistently with the HUD.
//
//   Pass 0 "BrightPass"       - soft-knee threshold (Catlike Coding form), weighted by source alpha
//   Pass 1 "AdditiveComposite" - Blend One One, adds the blurred glow (and its coverage) back in
//
// Gamma project: threshold + additive in gamma read punchier; no sRGB juggling (matches HudBlur /
// HudBackdrop, which use RenderTextureReadWrite.Default). NO shader_feature -> single variant.
Shader "UIA/HudBloom"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _BloomThreshold ("Bright threshold", Float) = 0.55
        _BloomKnee ("Soft knee (fraction)", Float) = 0.5
        _BloomStrength ("Composite strength", Float) = 0.8
        // Defaults = identity so a C# build older than this shader (or vice versa) is unchanged.
        _BloomTint ("Glow tint", Color) = (1,1,1,1)
        _BloomSaturation ("Glow saturation", Float) = 1
        // Saturation SELECTIVITY of the bright-pass: +1 = only SATURATED pixels bloom (coloured
        // borders/accents — white text stays dark), -1 = only unsaturated (white-hot text/ice),
        // 0 (default) = off, output identical to the previous bundle.
        _BloomSatBias ("Saturation bias", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _BloomThreshold;
        float _BloomKnee;      // 0..1 fraction of threshold used as the soft knee width
        float _BloomStrength;
        fixed4 _BloomTint;     // multiplies the glow colour (white = the HUD's own hues)
        float _BloomSaturation; // 0 = white-hot monochrome glow, 1 = source hues, >1 oversaturated
        float _BloomSatBias;   // bright-pass selectivity: + = saturated pixels only, - = unsaturated only

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv     : TEXCOORD0;
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv  : TEXCOORD0;
        };

        v2f vert(appdata v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv;
            return o;
        }
        ENDCG

        // Pass 0: BRIGHT-PASS. Soft-knee threshold on max(r,g,b); the result is weighted by the
        // SOURCE ALPHA so empty (transparent) HUD-RT areas contribute no bloom. Output alpha is 1
        // (irrelevant downstream: the blur carries it uniformly and Pass 1 re-derives coverage).
        Pass
        {
            Name "BrightPass"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBright
            fixed4 fragBright(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                float brightness = max(col.r, max(col.g, col.b));

                // Catlike Coding soft knee: a smooth ramp of width [threshold-knee .. threshold+knee]
                // instead of a hard cutoff, so borders/text fade INTO bloom rather than popping.
                float knee = _BloomThreshold * _BloomKnee;
                float soft = clamp(brightness - _BloomThreshold + knee, 0.0, 2.0 * knee);
                soft = soft * soft / (4.0 * knee + 1e-5);
                float contribution = max(soft, brightness - _BloomThreshold) / max(brightness, 1e-5);

                contribution *= col.a; // transparent RT areas stay black

                // Saturation selectivity (uniform branch — single variant, free when 0):
                // sat = chroma/brightness. +bias multiplies by sat (white text -> 0, coloured
                // borders -> ~1) so only saturated pixels reach the bloom; -bias inverts.
                if (abs(_BloomSatBias) > 0.001)
                {
                    float mn = min(col.r, min(col.g, col.b));
                    float sat = (brightness - mn) / max(brightness, 1e-4);
                    float w = _BloomSatBias >= 0.0
                        ? lerp(1.0, sat, _BloomSatBias)
                        : lerp(1.0, 1.0 - sat, -_BloomSatBias);
                    contribution *= w;
                }

                return fixed4(col.rgb * contribution, 1.0);
            }
            ENDCG
        }

        // Pass 1: ADDITIVE COMPOSITE back onto the HUD RT. Blend One One adds both light and coverage.
        //
        // ALPHA DECISION: the HUD RT is presented with STRAIGHT-alpha blending (RawImage _rtFlat and
        // the dome grid both draw with UI/Default = SrcAlpha OneMinusSrcAlpha), so the RT's alpha IS
        // its on-screen coverage. Writing (rgb, 0) would leave the glow INVISIBLE over empty glass
        // (alpha 0 -> nothing composited). So we ALSO add alpha, derived from the glow's own
        // brightness (a = saturate(max3(rgb))): bright halos gain real coverage and light their
        // transparent neighbours; existing HUD pixels (alpha already high) just get brighter, their
        // alpha saturating at 1. This is a lerp-toward-glow-colour rather than a pure screen add, but
        // for a translucent glass HUD it reads as the accent bleeding onto the surrounding glass.
        Pass
        {
            Name "AdditiveComposite"
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragComposite
            fixed4 fragComposite(v2f i) : SV_Target
            {
                fixed3 glow = tex2D(_MainTex, i.uv).rgb * _BloomStrength;
                // Saturation about the glow's own luminance (0 = white-hot, >1 pushes hues;
                // clamp at 0 — oversaturation can drive a channel negative), then tint.
                float lum = dot(glow, fixed3(0.299, 0.587, 0.114));
                glow = max(lerp(lum.xxx, glow, _BloomSaturation), 0.0) * _BloomTint.rgb;
                float a = saturate(max(glow.r, max(glow.g, glow.b)));
                return fixed4(glow, a);
            }
            ENDCG
        }
    }
}
