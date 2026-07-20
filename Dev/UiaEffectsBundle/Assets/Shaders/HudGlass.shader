// UIA/HudGlass - Tier C frosted-glass consumer shader for Stationeers UI Ascended 0.9.0.
//
// Built-in RP (BiRP), gamma color space, UGUI. Same UI/Default skeleton as HudEdgeFX (stencil,
// blend, authored unity_GUIZTestMode for mode C). Instead of drawing effects it SAMPLES the
// shared blur RT that HudBackdrop produces, exposed as the GLOBAL texture _UiaBlurTex, using a
// screen-space UV derived from ComputeScreenPos (NOT uv0 — uv0.x is the per-element strength).
//
// Y-FLIP (Master-Plan §12.5, "the most likely first-try failure"): Built-in-RP captures are
// commonly Y-flipped vs screen UVs. The flip is RUNTIME-TOGGLEABLE via the uniform float
// _UiaBlurTexFlip (the C# side probes and sets it) — not baked — so we can correct it in-game.
//
// NO shader_feature (uniform-float toggles only) -> zero variant stripping risk. Per-element frost
// strength rides uv0.x (§12.2); uv0.y is the legacy marker, ignored.
Shader "UIA/HudGlass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        // See HudEdgeFX: probed by HudShaderStore so an old resident bundle degrades to the
        // static ripple bake instead of losing shape shimmer entirely.
        [HideInInspector] _UiaFlowAbiVersion ("UIA Flow ABI Version", Float) = 1

        // --- stencil block, copied verbatim from UI/Default ---
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Enum(UnityEngine.Rendering.CompareFunction)] unity_GUIZTestMode ("ZTest", Float) = 4

        // --- frost uniforms (plain floats/colors; NO shader_feature) ---
        _FrostStrength ("Frost Strength", Float) = 1
        _FrostTint ("Frost Tint", Color) = (1,1,1,1)
        _FrostDarken ("Frost Darken", Float) = 0.2
        _ChromaStrength ("Backdrop Chroma", Float) = 0
        _UiaBlurTexFlip ("Blur Tex Y Flip", Float) = 0

        // --- Tier B effects DUPLICATED here (play-test fix: frost REPLACED the edgefx
        // material, so enabling Tier C silently killed shine + iridescence — a frosted
        // panel must LAYER them over the glass instead) ---
        _ShineStrength ("Shine Strength", Float) = 0
        _ShineWidth ("Shine Width", Float) = 0.1
        _ShinePos ("Shine Pos", Float) = 0
        _ShineDir ("Shine Dir (screen)", Vector) = (1,0,0,0)
        _IridStrength ("Iridescence Strength", Float) = 0
        _IridScale ("Iridescence Scale", Float) = 6
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
                float4 texcoord : TEXCOORD0; // uv0: x=FxStrength, y=marker, z=ripple harmonic weight
                // Moving-ripple payload from PolygonPanelGraphic (see HudEdgeFX.shader) —
                // all-zero on every other mesh, making the flow term below inert.
                float4 uv1      : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float4 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 screenPos     : TEXCOORD2;
                float4 flow          : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            // Shared backdrop blur RT, set globally by HudBackdrop (Tier C).
            sampler2D _UiaBlurTex;
            float4 _UiaBlurTex_TexelSize;

            fixed4 _FrostTint;
            float  _FrostStrength;
            float  _FrostDarken;
            float  _ChromaStrength;
            float  _UiaBlurTexFlip;

            float  _ShineStrength;
            float  _ShineWidth;
            float  _ShinePos;
            float4 _ShineDir;
            float  _IridStrength;
            float  _IridScale;

            // spectral_zucconi6 (Alan Zucconi) — same helper as HudEdgeFX.
            float3 bump3y(float3 x, float3 yoffset)
            {
                float3 y = 1.0 - x * x;
                y = saturate(y - yoffset);
                return y;
            }
            float3 spectral_zucconi6(float w)
            {
                const float3 c1 = float3(3.54585104, 2.93225262, 2.41593945);
                const float3 x1 = float3(0.69549072, 0.49228336, 0.27699880);
                const float3 y1 = float3(0.02312639, 0.15225084, 0.52607955);
                const float3 c2 = float3(3.90307140, 3.21182957, 3.96587128);
                const float3 x2 = float3(0.11748627, 0.86755042, 0.66077860);
                const float3 y2 = float3(0.84897130, 0.88445281, 0.73949448);
                return bump3y(c1 * (w - x1), y1) + bump3y(c2 * (w - x2), y2);
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = float4(TRANSFORM_TEX(v.texcoord.xy, _MainTex), v.texcoord.zw);
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                OUT.color = v.color * _Color;
                OUT.flow = v.uv1;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 vertexCol = (tex2D(_MainTex, IN.texcoord.xy) + _TextureSampleAdd) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                vertexCol.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                // screen UV for the shared blur RT
                float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);

                // Runtime-toggleable Y-flip (§12.5). Only compiled where the platform flips.
                #if UNITY_UV_STARTS_AT_TOP
                if (_UiaBlurTexFlip > 0.5) uv.y = 1.0 - uv.y;
                #endif

                float strength = IN.texcoord.x;       // uv0.x per-element frost strength (§12.2)
                float frost = _FrostStrength * strength;

                float3 backdrop = tex2D(_UiaBlurTex, uv).rgb;

                // Optional cheap chroma: element-local dir isn't available, so use a small fixed
                // screen-space texel offset scaled by _ChromaStrength * strength (3 taps total).
                if (_ChromaStrength > 0.0)
                {
                    float2 off = _UiaBlurTex_TexelSize.xy * (_ChromaStrength * strength * 4.0);
                    float r = tex2D(_UiaBlurTex, uv + off).r;
                    float b = tex2D(_UiaBlurTex, uv - off).b;
                    backdrop.r = r;
                    backdrop.b = b;
                }

                backdrop = backdrop * _FrostTint.rgb * (1.0 - _FrostDarken);

                // COMPOSITE (play-test rewrite): frosted glass means the panel becomes (nearly)
                // OPAQUE — the blurred+tinted world REPLACES the see-through — and the panel's
                // own painting (fill gradient, sheen, border light: all vertex color) draws
                // OVER that glass exactly as it would over the real world. The old additive
                // 'backdrop + fill' at fill-alpha washed out the sheen AND let the raw
                // unblurred world bleed through the remaining alpha.
                half3 overGlass = backdrop * (1.0 - vertexCol.a) + vertexCol.rgb * vertexCol.a;
                half3 rgb = lerp(vertexCol.rgb, overGlass, frost);
                // Glass opacity RESPECTS the mesh's own coverage fade (play-test: forcing 0.97
                // across the whole mesh footprint razor-edged the frost at the AA fringe /
                // soft-edge skirt). vertexCol.a*3 saturates to full glass on the solid fill but
                // follows the fringe down to 0, so frost melts out with the panel edge.
                half  glassA = min(0.97, vertexCol.a * 3.0);
                half  a  = lerp(vertexCol.a, max(vertexCol.a, glassA), frost);
                half4 col = half4(rgb, a);

                // Tier B layers OVER the glass (same math as HudEdgeFX; uv0.y = rim mask).
                float edge = saturate(IN.texcoord.y);
                float rim = edge * edge * edge;
                if (_ShineStrength > 0.0)
                {
                    float proj = dot(uv, _ShineDir.xy);
                    float band = 1.0 - smoothstep(0.0, _ShineWidth, abs(proj - _ShinePos));
                    col.rgb += vertexCol.a * _ShineStrength * strength * band * (0.35 + 0.65 * edge);
                }
                if (_IridStrength > 0.0)
                {
                    float w = frac((uv.x + uv.y) * (_IridScale * 0.25));
                    col.rgb += spectral_zucconi6(w) * (_IridStrength * 0.4 * strength * rim * vertexCol.a);
                }

                // MOVING EDGE RIPPLE (freeform Shapes) — identical term to HudEdgeFX so a
                // frosted shape flows the same as an unfrosted one. uv1 all-zero = free skip.
                if (IN.flow.y > 0.0001 && IN.flow.w > 0.0001)
                {
                    float ft = IN.flow.x * (IN.flow.z * 0.0628) - _Time.y * IN.flow.w * 3.0;
                    // harm = uv0.z = 1 - RippleSmooth (matches HudEdgeFX and the static bake).
                    float harm = IN.texcoord.z;
                    float wave = 0.32 * sin(ft)
                        + harm * (0.24 * sin(ft * 2.417 + 1.7) + 0.14 * sin(ft * 5.089 + 4.2));
                    col.rgb *= clamp(1.0 + IN.flow.y * wave, 0.0, 2.0);
                    col.a = saturate(col.a * (1.0 + 0.5 * IN.flow.y * wave));
                }

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
        ENDCG
        }
    }
}
