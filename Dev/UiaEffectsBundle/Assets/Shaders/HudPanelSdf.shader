// UIA/HudPanelSdf - analytic UGUI panel renderer for Stationeers UI Ascended.
//
// Built-in Render Pipeline / Unity 2022.3, target 3.0.  PanelGraphic sends a regular
// warp-friendly grid rather than appearance tessellation.  uv0 contains local position and
// half-size; uv1/uv2/uv3/normal/tangent contain a constant packed parameter block.  Keeping
// every appearance decision in this fragment shader means edge energy can animate without
// dirtying the Canvas mesh.
//
// Distance honesty:
//   * With no trapezoid inset and exponent 2, RoundedSuperBox is the exact signed distance to
//     the selected-radius rounded rectangle.
//   * Exponents above 2 retain the exact requested superellipse ZERO CONTOUR.  Outside starts
//     from the analytic p-norm surrogate with a first-order gradient-metric correction for band
//     width; deep inside, a compact C1 smooth-max removes the rounded-box medial-axis crease.
//     Neither corrected value is claimed to be a globally Euclidean distance.
//   * A trapezoid is evaluated by undoing its linearly varying side inset before the rounded-box
//     test.  Its side silhouette is exact; corner curvature remains approximate under that
//     shear.  A first-order Jacobian correction keeps authored border/glow widths approximately
//     constant on slanted sides, while derivative AA measures the final field on screen.
//   * "Gaussian halo" means an analytic Gaussian falloff as a function of SDF distance.  It is
//     deliberately not described as a Gaussian convolution/bloom.
Shader "UIA/HudPanelSdf"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Enum(UnityEngine.Rendering.CompareFunction)] unity_GUIZTestMode ("ZTest", Float) = 4

        // Shared frost controls.  Strength is retained for material-family ABI compatibility;
        // PanelGraphic's packed frost amount is already the final resolved per-panel value.
        _FrostStrength ("Frost Strength (legacy ABI)", Float) = 1
        _FrostTint ("Frost Tint", Color) = (1,1,1,1)
        _FrostDarken ("Frost Darken", Float) = 0.2
        _ChromaStrength ("Backdrop Chroma (legacy ABI)", Float) = 0

        // Shared animation geometry.  Packed shine/iridescence amounts are final per-panel values.
        _ShineStrength ("Shine Strength (legacy ABI)", Float) = 0
        _ShineWidth ("Shine Width", Float) = 0.1
        _ShinePos ("Shine Pos", Float) = 0
        _ShineDir ("Shine Dir (screen)", Vector) = (1,0,0,0)
        _IridStrength ("Iridescence Strength (legacy ABI)", Float) = 0
        _IridScale ("Iridescence Scale", Float) = 6

        _DissolveAmt ("Dissolve Amount", Float) = 0
        _DissolveGlow ("Dissolve Frontier Glow", Float) = 1
        _DissolveScale ("Dissolve Noise Scale", Float) = 30

        _EdgeLightDir ("Edge Light Direction", Vector) = (-0.447214,0.894427,0,0)
        _EdgeLightColor ("Edge Light Color", Color) = (1,1,1,1)
        _EdgeLightRim ("Opposing Edge Rim", Float) = 0.5
        _EdgeLightSharp ("Edge Light Sharpness", Float) = 3
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
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex    : POSITION;
                float4 color     : COLOR;
                float4 texcoord0 : TEXCOORD0; // local x/y, half width/height
                float4 texcoord1 : TEXCOORD1; // radii, insets, border width + flags
                float4 texcoord2 : TEXCOORD2; // border RGBA
                float4 texcoord3 : TEXCOORD3; // feather + packed base/border/glow style
                float3 normal    : NORMAL;    // packed halo/ripple/flow style
                float4 tangent   : TANGENT;   // packed optics/superellipse/edge fade
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float4 localHalf     : TEXCOORD0;
                float4 radiiInset    : TEXCOORD1;
                fixed4 border        : TEXCOORD2;
                float4 baseStyle     : TEXCOORD3;
                float4 edgeStyle     : TEXCOORD4;
                float4 optics        : TEXCOORD5;
                float4 worldPosition : TEXCOORD6;
                float4 screenPos     : TEXCOORD7;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex; // Required by UGUI's material contract; analytic panels do not sample it.
            fixed4 _Color;
            float4 _ClipRect;

            // HudBackdrop publishes all four levels globally.  They intentionally are not material
            // Properties: a material-local texture would shadow the live global capture.
            sampler2D _UiaBlurTex0;
            sampler2D _UiaBlurTex1;
            sampler2D _UiaBlurTex2;
            sampler2D _UiaBlurTex3;
            float4 _UiaBlurTex0_TexelSize;
            float4 _UiaBlurTex1_TexelSize;
            float4 _UiaBlurTex2_TexelSize;
            float4 _UiaBlurTex3_TexelSize;
            float _UiaBlurTexFlip; // global-only runtime orientation correction

            fixed4 _FrostTint;
            float _FrostStrength;  // compatibility property; packed amount is authoritative here
            float _FrostDarken;
            float _ChromaStrength; // compatibility property; packed amount is authoritative here
            float _ShineStrength;  // compatibility property; packed amount is authoritative here
            float _ShineWidth;
            float _ShinePos;
            float4 _ShineDir;
            float _IridStrength;   // compatibility property; packed amount is authoritative here
            float _IridScale;
            float _DissolveAmt;
            float _DissolveGlow;
            float _DissolveScale;
            float4 _EdgeLightDir;
            fixed4 _EdgeLightColor;
            float _EdgeLightRim;
            float _EdgeLightSharp;

            static const float UIA_PI2 = 6.28318530718;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                OUT.color = v.color * _Color;
                OUT.localHalf = v.texcoord0;
                OUT.radiiInset = v.texcoord1;
                OUT.border = v.texcoord2;
                OUT.baseStyle = v.texcoord3;
                OUT.edgeStyle = float4(v.normal, 0.0);
                OUT.optics = v.tangent;
                return OUT;
            }

            // Decode two 12-bit UNorm values packed into one exactly representable float integer.
            float2 Unpack01(float packedValue)
            {
                float bits = floor(packedValue + 0.5);
                float hi = floor(bits * (1.0 / 4096.0));
                float lo = bits - hi * 4096.0;
                return float2(lo, hi) * (1.0 / 4095.0);
            }

            float FlagBit(float flags, float bitValue)
            {
                float q = floor(flags / bitValue);
                return q - floor(q * 0.5) * 2.0;
            }

            float SuperNorm(float2 q, float exponent)
            {
                q = max(q, 0.0);
                // Exponent 2 is the default and by far the common path.  Avoid three general
                // pow() operations for an ordinary Euclidean length.
                if (abs(exponent - 2.0) < 0.001)
                    return sqrt(dot(q, q));
                return pow(pow(q.x, exponent) + pow(q.y, exponent), 1.0 / exponent);
            }

            // Compact-support C1 smooth maximum.  Unlike log-sum-exp it becomes the exact hard
            // max outside a band of width k, so it cannot perturb the contour or straight sides.
            float CompactSmoothMax(float a, float b, float k)
            {
                if (k <= 1e-5) return max(a, b);
                float h = max(k - abs(a - b), 0.0) / k;
                return max(a, b) + 0.25 * k * h * h;
            }

            // Exact Euclidean SDF for exponent 2.  For exponent >2 the zero contour remains exact;
            // outside magnitude is the documented p-norm surrogate and deep-inside magnitude is
            // the explicitly smoothed appearance field described below.
            float RoundedSuperBox(float2 p, float2 halfSize, float4 radii, float exponent,
                out float2 outwardNormal, out float fieldMetricScale)
            {
                // radii order is BL, BR, TR, TL
                float bottomRadius = p.x < 0.0 ? radii.x : radii.y;
                float topRadius = p.x < 0.0 ? radii.w : radii.z;
                float radius = p.y < 0.0 ? bottomRadius : topRadius;
                radius = clamp(radius, 0.0, min(halfSize.x, halfSize.y));

                float2 q = abs(p) - halfSize + radius;
                float2 outside = max(q, 0.0);
                float inside = min(max(q.x, q.y), 0.0);
                float superDistance = SuperNorm(outside, exponent);
                float distance = inside + superDistance - radius;

                float2 n;
                fieldMetricScale = 1.0;
                if (outside.x > 0.0 || outside.y > 0.0)
                {
                    // Direction of the p-norm gradient; its raw magnitude is retained separately
                    // as a first-order distance metric for authored bands.
                    if (abs(exponent - 2.0) < 0.001 || superDistance < 1e-4)
                    {
                        n = outside;
                    }
                    else
                    {
                        // Normalize q by its p-norm first.  This is algebraically identical to
                        // dividing q^(n-1) by norm(q)^(n-1), but stays well-conditioned for
                        // subpixel radii and saves a third general pow().
                        float2 rawGradient = pow(outside / superDistance, exponent - 1.0);
                        // p-norm fields are not Euclidean: their gradient is ~0.77 at an n=8
                        // diagonal.  Preserve the documented surrogate, but expose its local
                        // metric so authored border/glow widths do not become 30% thicker there.
                        fieldMetricScale = length(rawGradient);
                        n = rawGradient;
                    }
                    n *= rsqrt(max(dot(n, n), 1e-8));
                }
                else
                {
                    n = q.x > q.y ? float2(1.0, 0.0) : float2(0.0, 1.0);
                }

                // A true rounded-box SDF necessarily has a max() medial axis deep inside.  Wide
                // inner effects expose that derivative crease as an X.  For squircle exponents,
                // blend toward a compact smooth-max of the radius-INDEPENDENT base box field.
                // Using one panel-wide width is important: a width derived from the selected
                // corner radius jumps at quadrant boundaries when adjacent radii differ.
                float squircleBlend = saturate((exponent - 2.0) * (1.0 / 6.0));
                float meanRadius = dot(radii, float4(0.25, 0.25, 0.25, 0.25));
                float smoothWidth = meanRadius * squircleBlend * 0.75;
                if (smoothWidth > 0.001 && distance < 0.0)
                {
                    float2 baseQ = abs(p) - halfSize;
                    float smoothInside = CompactSmoothMax(baseQ.x, baseQ.y, smoothWidth);
                    // The zero-contour gate is C1 and exactly zero at d=0, so circular/squircle
                    // shoulders remain untouched.  Deep inside it removes the max() crease.
                    float interiorBlend = smoothstep(0.0, smoothWidth, -distance);
                    distance = min(0.0, lerp(distance, smoothInside, interiorBlend));

                    float gradientX = saturate(0.5
                        + 0.5 * (baseQ.x - baseQ.y) / smoothWidth);
                    float2 smoothGradient = float2(gradientX, 1.0 - gradientX);
                    smoothGradient *= rsqrt(max(dot(smoothGradient, smoothGradient), 1e-8));
                    n = lerp(n, smoothGradient, interiorBlend);
                    n *= rsqrt(max(dot(n, n), 1e-8));
                    fieldMetricScale = lerp(fieldMetricScale, 1.0, interiorBlend);
                }

                float2 signP = float2(p.x < 0.0 ? -1.0 : 1.0,
                                      p.y < 0.0 ? -1.0 : 1.0);
                outwardNormal = n * signP;
                return distance;
            }

            float PanelDistance(float2 p, float2 halfSize, float4 radii,
                float topInset, float bottomInset, float exponent, out float2 outwardNormal)
            {
                // Clamp only pathological profiles that would invert the trapezoid through its centre.
                float maxInset = max(0.0, halfSize.x - 0.001);
                topInset = clamp(topInset, 0.0, maxInset);
                bottomInset = clamp(bottomInset, 0.0, maxInset);

                float y01 = saturate((p.y + halfSize.y) / max(2.0 * halfSize.y, 1e-5));
                float inset = lerp(bottomInset, topInset, y01);
                float sideSign = p.x < 0.0 ? -1.0 : 1.0;
                float2 shearedP = float2(sideSign * (abs(p.x) + inset), p.y);

                float2 shearedNormal;
                float fieldMetricScale;
                float distance = RoundedSuperBox(shearedP, halfSize, radii, exponent,
                    shearedNormal, fieldMetricScale);

                // Chain the rounded-box gradient through x' = x + sign(x)*inset(y).
                // Outside the top/bottom interval the saturate has zero derivative.
                float slope = 0.0;
                if (p.y > -halfSize.y && p.y < halfSize.y)
                    slope = (topInset - bottomInset) / max(2.0 * halfSize.y, 1e-5);
                float2 transformedGradient = float2(shearedNormal.x,
                    shearedNormal.y + sideSign * slope * shearedNormal.x);
                float jacobianScale = sqrt(max(dot(transformedGradient, transformedGradient), 1e-8));
                outwardNormal = transformedGradient / jacobianScale;

                // The inverse shear's raw field measures horizontal displacement on a slanted
                // side.  Dividing by the local Jacobian-gradient magnitude is the first-order
                // Euclidean correction: straight-side border/glow bands retain their authored
                // thickness.  Curved trapezoid corners remain the documented approximation.
                return distance / max(jacobianScale * fieldMetricScale, 1e-5);
            }

            float SmoothEdgeLight(float2 normal, float2 p, float halfWidth)
            {
                float2 lightDir = _EdgeLightDir.xy;
                lightDir *= rsqrt(max(dot(lightDir, lightDir), 1e-8));
                float lightDot = dot(normal, lightDir);
                float sharpness = max(1.0, _EdgeLightSharp);
                float across = saturate((p.x + halfWidth) / max(2.0 * halfWidth, 1e-5));
                return pow(saturate(lightDot), sharpness) * (1.0 - 0.55 * across)
                    + max(0.0, _EdgeLightRim) * pow(saturate(-lightDot), sharpness)
                    * (0.25 + 0.75 * across);
            }

            float RippledEdgeLight(float smoothLight, float2 localPosition, float amount,
                float frequency, float smoothing, float flowSpeed)
            {
                if (amount <= 0.004) return smoothLight;
                // Continuous 2D travelling waves avoid the unavoidable cut in a naively wrapped
                // perimeter scalar.  Frequency retains the legacy meaning of cycles per ~100
                // local pixels, while three directions keep the motion organic.  Because phase
                // is a function of position (not a branch-selected edge), rounded and trapezoid
                // corners cannot crack as the energy flows through them.
                float2 wavePosition = localPosition
                    * (max(0.05, frequency) * (UIA_PI2 / 100.0));
                float timePhase = _Time.y * flowSpeed * UIA_PI2;
                float harmonics = 1.0 - saturate(smoothing);
                float baseWave = sin(dot(wavePosition, float2(0.819232, 0.573462))
                    - timePhase);
                float detailA = sin(dot(wavePosition * 2.417, float2(-0.360596, 0.932722))
                    - timePhase * 0.83 + 1.7);
                float detailB = sin(dot(wavePosition * 5.089, float2(0.979398, -0.201938))
                    - timePhase * 1.19 + 4.2);
                float ripple = 1.0 + amount * (0.32 * baseWave
                    + harmonics * (0.24 * detailA + 0.14 * detailB));
                return clamp(smoothLight * ripple, 0.0, 2.0);
            }

            float EnabledSideMask(float2 normal, float flags)
            {
                float4 weights = float4(saturate(normal.y), saturate(normal.x),
                    saturate(-normal.y), saturate(-normal.x)); // top, right, bottom, left
                float4 enabled = float4(FlagBit(flags, 1.0), FlagBit(flags, 2.0),
                    FlagBit(flags, 4.0), FlagBit(flags, 8.0));
                float weightSum = weights.x + weights.y + weights.z + weights.w;
                return dot(weights, enabled) / max(weightSum, 1e-4);
            }

            float AxisEdgeFade(float coordinate, float extent, float fraction)
            {
                if (fraction <= 0.0001) return 1.0;
                float band = max(fraction * extent * 2.0, 1e-4);
                return saturate((extent - abs(coordinate)) / band);
            }

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

            float3 bump3y(float3 x, float3 yoffset)
            {
                return saturate(1.0 - x * x - yoffset);
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

            float3 SampleBlurDepth(float2 uv, float depth)
            {
                float level = saturate(depth) * 3.0;
                // Explicit initialization placates Unity's D3D11 shader compiler when this
                // helper is inlined through dynamic sampler branches; every branch overwrites it.
                float3 sampled = float3(0.0, 0.0, 0.0);
                if (level < 1.0)
                    sampled = lerp(tex2D(_UiaBlurTex0, uv).rgb,
                        tex2D(_UiaBlurTex1, uv).rgb, level);
                else if (level < 2.0)
                    sampled = lerp(tex2D(_UiaBlurTex1, uv).rgb,
                        tex2D(_UiaBlurTex2, uv).rgb, level - 1.0);
                else
                    sampled = lerp(tex2D(_UiaBlurTex2, uv).rgb,
                        tex2D(_UiaBlurTex3, uv).rgb, level - 2.0);
                return sampled;
            }

            float2 BlurTexelAtDepth(float depth)
            {
                float level = saturate(depth) * 3.0;
                float2 texel;
                if (level < 1.0)
                    texel = lerp(abs(_UiaBlurTex0_TexelSize.xy),
                        abs(_UiaBlurTex1_TexelSize.xy), level);
                else if (level < 2.0)
                    texel = lerp(abs(_UiaBlurTex1_TexelSize.xy),
                        abs(_UiaBlurTex2_TexelSize.xy), level - 1.0);
                else
                    texel = lerp(abs(_UiaBlurTex2_TexelSize.xy),
                        abs(_UiaBlurTex3_TexelSize.xy), level - 2.0);
                return max(texel, 1.0 / max(_ScreenParams.xy, float2(1.0, 1.0)));
            }

            void CompositeOver(float3 topRgb, float topAlpha, inout float3 underRgb,
                inout float underAlpha)
            {
                topAlpha = saturate(topAlpha);
                float remainingUnder = underAlpha * (1.0 - topAlpha);
                float outAlpha = topAlpha + remainingUnder;
                underRgb = (topRgb * topAlpha + underRgb * remainingUnder)
                    / max(outAlpha, 1e-5);
                underAlpha = outAlpha;
            }

            float CheapHaloFalloff(float x, float diffuse)
            {
                x = saturate(x);
                float smoothX = x * x * (3.0 - 2.0 * x);
                return pow(max(0.0, 1.0 - smoothX), lerp(2.0, 0.65, saturate(diffuse)));
            }

            float GaussianDistanceFalloff(float x)
            {
                // exp2(-6.643856) ~= 0.01 at the authored width; the smooth cutoff gives the
                // finite grid skirt a zero-slope landing.  This is distance falloff, not blur.
                x = saturate(x);
                return exp2(-6.643856 * x * x) * (1.0 - smoothstep(0.90, 1.0, x));
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.localHalf.xy;
                float2 halfSize = max(IN.localHalf.zw, 0.001);

                float2 radiusBLBR = Unpack01(IN.radiiInset.x) * 1024.0;
                float2 radiusTRTL = Unpack01(IN.radiiInset.y) * 1024.0;
                float4 radii = float4(radiusBLBR.x, radiusBLBR.y,
                    radiusTRTL.x, radiusTRTL.y);
                float2 insets = Unpack01(IN.radiiInset.z) * 1024.0; // top, bottom

                float flags = floor(IN.radiiInset.w * (1.0 / 16.0) + 1e-4);
                float borderWidth = max(0.0, IN.radiiInset.w - flags * 16.0);

                float feather = max(0.0, IN.baseStyle.x);
                float2 sheenSpec = Unpack01(IN.baseStyle.y);
                float2 borderFadeSoft = Unpack01(IN.baseStyle.z);
                float borderFade = borderFadeSoft.x;
                float softEdge = borderFadeSoft.y * 48.0;
                float2 glowAmounts = Unpack01(IN.baseStyle.w) * 2.0;

                float2 glowStyle = Unpack01(IN.edgeStyle.x);
                float glowWidth = glowStyle.x * 160.0;
                float glowDiffuse = glowStyle.y;
                float2 rippleStyle = Unpack01(IN.edgeStyle.y);
                float rippleAmount = rippleStyle.x * 2.5;
                float rippleFrequency = rippleStyle.y * 8.0;
                float2 flowStyle = Unpack01(IN.edgeStyle.z);
                float rippleSmooth = flowStyle.x;
                float flowSpeed = flowStyle.y * 4.0;

                float2 frostStyle = Unpack01(IN.optics.x);
                float2 chromaSuper = Unpack01(IN.optics.y);
                float2 shineIrid = Unpack01(IN.optics.z);
                float2 edgeFade = Unpack01(IN.optics.w) * 0.5;
                float frostAmount = frostStyle.x;
                float frostDepth = frostStyle.y;
                float chromaAmount = chromaSuper.x;
                float exponent = 2.0 + chromaSuper.y * 6.0;
                float shineAmount = shineIrid.x * 2.0;
                float iridAmount = shineIrid.y;

                float2 outwardNormal;
                float distance = PanelDistance(p, halfSize, radii, insets.x, insets.y,
                    exponent, outwardNormal);

                // Derivative coverage follows the actual post-warp field.  Feather/soft-edge are
                // authored local-pixel widths; the footprint prevents subpixel collapse under scaling.
                // The L2 screen-space derivative avoids fwidth's L1 diagonal bias (circles otherwise
                // become fractionally heavier around 45-degree arcs under the visor warp).
                float2 distanceDerivatives = float2(ddx(distance), ddy(distance));
                float derivativeAA = max(0.5 * length(distanceDerivatives), 0.001);
                // ddy is raster-space down on the D3D UI target; convert back to UI screen
                // coordinates (x right, y up) before comparing to the authored light direction.
                float2 screenOutwardNormal = float2(distanceDerivatives.x, -distanceDerivatives.y);
                screenOutwardNormal *= rsqrt(max(dot(screenOutwardNormal, screenOutwardNormal), 1e-8));

                // `_Color` is CanvasRenderer's tint.  The packed border must receive it just as
                // legacy border vertices did; IN.color already contains it for the fill.
                float4 fillColor = IN.color;
                float4 borderColor = IN.border * _Color;
                float3 accentBorderRgb = borderColor.rgb;
                float hasBorder = step(0.05, borderWidth) * step(0.004, IN.border.a);

                // Preserve PanelGraphic's authored band order instead of stacking every effect
                // at d=0: fill -> inward feather -> border -> outward feather + SoftEdge -> halo.
                // derivativeAA is a lower bound, never an extra band, so downscaled panels remain
                // anti-aliased without silently shortening the user's Feather/SoftEdge widths.
                float innerFeatherWidth = max(derivativeAA, feather);
                float authoredOuterWidth = max(0.0, feather + softEdge);
                float outerFadeWidth = max(derivativeAA, authoredOuterWidth);
                float fillMask = hasBorder > 0.5
                    ? 1.0 - smoothstep(-innerFeatherWidth, 0.0, distance)
                    : 1.0 - smoothstep(0.0, outerFadeWidth, distance);

                float contourInside = 1.0 - smoothstep(-derivativeAA, derivativeAA, distance);
                float borderInner = smoothstep(-innerFeatherWidth, 0.0, distance);
                float borderOuter = 1.0 - smoothstep(borderWidth,
                    borderWidth + outerFadeWidth, distance);
                float borderMask = borderInner * borderOuter * hasBorder;
                float baseOuterDistance = hasBorder > 0.5 ? borderWidth : 0.0;
                float haloStartDistance = baseOuterDistance + authoredOuterWidth;

                float smoothLight = SmoothEdgeLight(screenOutwardNormal, p, halfSize.x);
                float edgeLight = RippledEdgeLight(smoothLight, p, rippleAmount,
                    rippleFrequency, rippleSmooth, flowSpeed);
                float sideMask = EnabledSideMask(outwardNormal, flags);

                // Fill sheen mirrors PanelGraphic.FillAt: a quiet quadratic whitening toward top.
                float vertical = saturate((p.y + halfSize.y) / max(2.0 * halfSize.y, 1e-5));
                float sheenWeight = sheenSpec.x * 0.30 * vertical * vertical;
                fillColor.rgb = lerp(fillColor.rgb, float3(1.0, 1.0, 1.0), sheenWeight);
                fillColor.a = min(1.0, fillColor.a * (1.0 + sheenWeight));
                float3 accentFillRgb = fillColor.rgb;
                float fillSourceAlpha = fillColor.a;
                float fillLayerAlpha = fillSourceAlpha * fillMask;

                // Border light, spec tint, fade and side selection all happen per fragment, so a
                // moving ripple never rebuilds the Canvas.
                float specWeight = saturate(edgeLight * sheenSpec.y * 1.6);
                borderColor.rgb = lerp(borderColor.rgb, _EdgeLightColor.rgb, specWeight);
                borderColor.a += (1.0 - borderColor.a) * specWeight * 0.85;
                borderColor.a *= lerp(1.0, saturate(edgeLight * 2.2), borderFade);
                borderColor.a *= sideMask;

                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                float2 blurUV = screenUV;
                // The SDF gradient is the right refraction direction at the rim, but every true
                // distance field has a medial axis inside.  Using that gradient across the whole
                // frost fill would turn its direction discontinuity into a chromatic X.  Fade to
                // the legacy shader's quiet fixed diagonal away from the contour.
                float chromaRimDepth = max(4.0, min(halfSize.x, halfSize.y) * 0.25);
                float chromaRim = 1.0 - smoothstep(0.0, chromaRimDepth,
                    max(0.0, -distance));
                float2 chromaDirection = lerp(float2(0.707107, 0.707107),
                    screenOutwardNormal, chromaRim);
                #if UNITY_UV_STARTS_AT_TOP
                if (_UiaBlurTexFlip > 0.5)
                {
                    blurUV.y = 1.0 - blurUV.y;
                    chromaDirection.y = -chromaDirection.y;
                }
                #endif

                // Packed strengths are already resolved (global-follow vs local override) by C#.
                // Do not multiply by the legacy family-strength uniforms a second time.
                if (frostAmount > 0.001)
                {
                    float3 backdrop = SampleBlurDepth(blurUV, frostDepth);
                    if (chromaAmount > 0.001)
                    {
                        float2 chromaOffset = chromaDirection * BlurTexelAtDepth(frostDepth)
                            * (chromaAmount * 4.0);
                        backdrop.r = SampleBlurDepth(blurUV + chromaOffset, frostDepth).r;
                        backdrop.b = SampleBlurDepth(blurUV - chromaOffset, frostDepth).b;
                    }
                    backdrop *= _FrostTint.rgb * (1.0 - saturate(_FrostDarken));

                    // Coverage belongs inside the frost composite, exactly as it did when
                    // HudGlass received the fallback mesh's already-faded vertex alpha.  Using
                    // solid-fill alpha here and multiplying coverage later colours the fringe
                    // with too much foreground paint and saturates glass opacity too early.
                    float ownAlpha = saturate(fillLayerAlpha);
                    float3 overGlass = backdrop * (1.0 - ownAlpha)
                        + fillColor.rgb * ownAlpha;
                    fillColor.rgb = lerp(fillColor.rgb, overGlass, saturate(frostAmount));
                    float glassAlpha = min(0.97, ownAlpha * 3.0);
                    fillLayerAlpha = lerp(ownAlpha, max(ownAlpha, glassAlpha),
                        saturate(frostAmount));
                }

                // Halo colour stays accent-led rather than spec-white.  Light/ripple shapes its
                // energy; GlowDiffuse flattens the direction while lowering the peak.
                float3 haloRgb = lerp(accentFillRgb, accentBorderRgb, 0.75);
                float haloSourceAlpha = max(0.5, max(fillSourceAlpha, borderColor.a));
                float rippleShare = 0.35 * (1.0 - glowDiffuse);
                float haloLight = min(1.2, lerp(smoothLight, edgeLight, rippleShare));
                float shapeFloor = 0.08 + 0.27 * glowDiffuse;
                float shapedLight = 0.55 * haloSourceAlpha
                    * (shapeFloor + (1.0 - shapeFloor) * haloLight)
                    * (1.0 - 0.35 * glowDiffuse) * sideMask;

                float3 composedRgb = haloRgb;
                float composedAlpha = 0.0;
                if (glowAmounts.x > 0.004 && glowWidth > 0.05)
                {
                    float haloX = saturate(max(0.0, distance - haloStartDistance) / glowWidth);
                    float cheapFalloff = CheapHaloFalloff(haloX, glowDiffuse);
                    float gaussianFalloff = GaussianDistanceFalloff(haloX);
                    float haloFalloff = lerp(cheapFalloff, gaussianFalloff,
                        FlagBit(flags, 16.0));
                    // Let the halo rise underneath the outer base fade, reaching full energy at
                    // its endpoint.  Its own width begins there, matching the legacy stop order.
                    float outsideGate = smoothstep(baseOuterDistance - derivativeAA,
                        max(baseOuterDistance + derivativeAA, haloStartDistance), distance);
                    composedAlpha = min(0.9, glowAmounts.x * shapedLight)
                        * haloFalloff * outsideGate;
                }

                CompositeOver(fillColor.rgb, fillLayerAlpha,
                    composedRgb, composedAlpha);

                if (glowAmounts.y > 0.004 && glowWidth > 0.05)
                {
                    float innerWidth = min(glowWidth,
                        max(1.0, min(halfSize.x, halfSize.y) * 0.72));
                    float innerFalloff = 1.0 - smoothstep(0.0, innerWidth,
                        max(0.0, -distance));
                    float innerAlpha = min(0.9, glowAmounts.y * shapedLight)
                        * innerFalloff * contourInside;
                    CompositeOver(haloRgb, innerAlpha, composedRgb, composedAlpha);
                }

                CompositeOver(borderColor.rgb, borderColor.a * borderMask,
                    composedRgb, composedAlpha);

                fixed4 col = fixed4(composedRgb, saturate(composedAlpha));

                // Fragment-space end fade includes the complete authored skirt, so border, frost
                // and halo dissolve together.  This replaces the old vertex ramp in SDF mode.
                float skirt = baseOuterDistance + authoredOuterWidth;
                if (glowAmounts.x > 0.004)
                    skirt += glowWidth;
                skirt = max(2.0, skirt);
                float fade = AxisEdgeFade(p.x, halfSize.x + skirt, edgeFade.x)
                    * AxisEdgeFade(p.y, halfSize.y + skirt, edgeFade.y);
                fade = fade * fade * (3.0 - 2.0 * fade);
                col.a *= fade;

                // SDF-derived rim: independent of tessellation and stable under the visor warp.
                float rimWidth = max(2.0, borderWidth + feather + 2.0);
                float rim = 1.0 - smoothstep(0.0, rimWidth, abs(distance));
                if (shineAmount > 0.001)
                {
                    float projection = dot(screenUV, _ShineDir.xy);
                    float shineBand = 1.0 - smoothstep(0.0, max(_ShineWidth, 1e-4),
                        abs(projection - _ShinePos));
                    col.rgb += col.a * shineAmount * shineBand * (0.35 + 0.65 * rim);
                }
                if (iridAmount > 0.001)
                {
                    float wavelength = frac((screenUV.x + screenUV.y)
                        * (_IridScale * 0.25));
                    col.rgb += spectral_zucconi6(wavelength)
                        * (iridAmount * 0.4 * rim * rim * rim * col.a);
                }

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                if (FlagBit(flags, 32.0) > 0.5 && _DissolveAmt > 0.0)
                {
                    float noise = valueNoise(screenUV * max(_DissolveScale, 0.001));
                    float thresholdDelta = noise - saturate(_DissolveAmt);
                    clip(thresholdDelta);
                    float frontier = 1.0 - smoothstep(0.0, 0.06, thresholdDelta);
                    col.rgb += frontier * _DissolveGlow * col.a;
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
