// UIA/HudEdgeFX - Tier B UGUI effect shader for Stationeers UI Ascended 0.9.0.
//
// Built-in RP (BiRP), gamma color space, UGUI. Skeleton copied from Unity's UI/Default so it
// composites identically inside a Canvas: same Tags, same stencil block driven by _Stencil*,
// same Blend/Cull/ZWrite, PLUS an authored `unity_GUIZTestMode` property so mode C's world-space
// canvas can ZTest-Always these materials (HudSystem sets it via material.SetInt; see
// HudSystem.cs ApplyWorldMaterials ~line 1470).
//
// The mesh IS the art: base color = vertex color (our PanelGraphic/PolylineGraphic bake color
// per-vertex). _MainTex is declared for UGUI compatibility and multiplied in (white default =>
// no-op, safe with the font system's _TextureSampleAdd).
//
// NO shader_feature / NO multi_compile for the EFFECT toggles: every effect is gated by a plain
// uniform float, so the bundle contains a SINGLE variant per keyword combination and variant
// stripping is a NON-ISSUE (no ShaderVariantCollection needed). Only the standard UGUI clip-rect
// keywords remain (multi_compile_local, which Unity never strips).
//
// Per-element strength rides uv0.x (Master-Plan §12.2). uv0.y is the legacy center/edge marker
// and is IGNORED here.
Shader "UIA/HudEdgeFX"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // --- stencil block, copied verbatim from UI/Default ---
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        // Authored ZTest (UI/Default leaves this as a Canvas-driven global; we make it a settable
        // property so mode C can force CompareFunction.Always). Default 4 = LEqual.
        [Enum(UnityEngine.Rendering.CompareFunction)] unity_GUIZTestMode ("ZTest", Float) = 4

        // --- effect uniforms (all plain floats; NO shader_feature -> zero stripping risk) ---
        // (a) shine sweep
        _ShineStrength ("Shine Strength", Float) = 0
        _ShineWidth ("Shine Width", Float) = 0.1
        _ShinePos ("Shine Pos", Float) = 0
        _ShineDir ("Shine Dir (screen)", Vector) = (1,0,0,0)
        // (b) iridescence
        _IridStrength ("Iridescence Strength", Float) = 0
        _IridScale ("Iridescence Scale", Float) = 6
        // (c) dissolve / boot reveal
        _DissolveAmt ("Dissolve Amount", Float) = 0
        _DissolveGlow ("Dissolve Frontier Glow", Float) = 1
        _DissolveScale ("Dissolve Noise Scale", Float) = 30
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 screenPos     : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            float  _ShineStrength;
            float  _ShineWidth;
            float  _ShinePos;
            float4 _ShineDir;
            float  _IridStrength;
            float  _IridScale;
            float  _DissolveAmt;
            float  _DissolveGlow;
            float  _DissolveScale;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            // --- Alan Zucconi's spectral_zucconi6: branchless approx of the visible spectrum. ---
            // http://www.alanzucconi.com/2017/07/15/improving-the-rainbow-2/
            float3 bump3y(float3 x, float3 yoffset)
            {
                float3 y = 1.0 - x * x;
                y = saturate(y - yoffset);
                return y;
            }
            float3 spectral_zucconi6(float w)
            {
                // w in [0,1]
                const float3 c1 = float3(3.54585104, 2.93225262, 2.41593945);
                const float3 x1 = float3(0.69549072, 0.49228336, 0.27699880);
                const float3 y1 = float3(0.02312639, 0.15225084, 0.52607955);
                const float3 c2 = float3(3.90307140, 3.21182957, 3.96587128);
                const float3 x2 = float3(0.11748627, 0.86755042, 0.66077860);
                const float3 y2 = float3(0.84897130, 0.88445281, 0.73949448);
                return bump3y(c1 * (w - x1), y1) + bump3y(c2 * (w - x2), y2);
            }

            // --- cheap procedural value noise from screen UV (no texture sample) ---
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // base = vertex color (the mesh is the art); multiply _MainTex for UGUI safety.
                half4 col = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                // screen-space UV (0..1) for the screen-anchored bands
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);

                // per-element strength = uv0.x (§12.2). uv0.y is the legacy centre/edge marker
                // (0 at the panel's fill centre -> 1 on the contour columns): interpolation
                // makes it a free RADIAL EDGE MASK — exactly what confines "glass optics" to
                // the rim. (Play-test fix: unmasked iridescence painted diagonal rainbow
                // stripes across entire panel fills — see the 2026-07-14 screenshots.)
                float strength = IN.texcoord.x;
                float edge = saturate(IN.texcoord.y);
                float rim = edge * edge * edge;    // cubic: quiet fill, bright contour

                // (a) SHINE sweep — additive band across the surface, modulated by own alpha
                // and per-element strength; mildly rim-weighted so the band reads as light
                // catching GLASS rather than a flat white wash.
                if (_ShineStrength > 0.0)
                {
                    float proj = dot(screenUV, _ShineDir.xy);
                    float band = 1.0 - smoothstep(0.0, _ShineWidth, abs(proj - _ShinePos));
                    col.rgb += col.a * _ShineStrength * strength * band * (0.35 + 0.65 * edge);
                }

                // (b) IRIDESCENCE — a slow spectral drift along the RIM only. The hue varies
                // gently across the screen (0.25x the old frequency: an accent, not stripes)
                // and the cubic rim mask keeps the fill clean.
                if (_IridStrength > 0.0)
                {
                    float w = frac((screenUV.x + screenUV.y) * (_IridScale * 0.25));
                    float3 irid = spectral_zucconi6(w);
                    col.rgb += irid * (_IridStrength * 0.4 * strength * rim * col.a);
                }

                // (c) DISSOLVE / boot reveal — clip by procedural noise + bright frontier glow.
                if (_DissolveAmt > 0.0)
                {
                    float n = valueNoise(screenUV * _DissolveScale);
                    clip(n - _DissolveAmt);
                    float frontier = step(n - _DissolveAmt, 0.06);
                    col.rgb += frontier * _DissolveGlow * col.a;
                }

                // (d) CHROMA: intentionally NOT here. Chromatic aberration needs a backdrop TEXTURE
                //     source, so it lives in HudGlass.shader (the frost/backdrop consumer) instead.

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
        ENDCG
        }
    }
}
