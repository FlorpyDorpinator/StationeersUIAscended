// UIA/HudBlur - Tier C dual-Kawase blur for the frosted-glass backdrop (Stationeers UI Ascended 0.9.0).
//
// NOT a UI shader. A plain image-effect (blit) shader with TWO passes for the dual-filter blur of
// Marius Bjorge (ARM, "Bandwidth-Efficient Rendering", SIGGRAPH 2015):
//   Pass 0 = downsample (5-tap: center weight 0.5 + 4 half-texel diagonal corners 0.125 each)
//   Pass 1 = upsample   (8-tap tent: axis taps 1/12, diagonal taps 2/12)
// HudBackdrop chains Pass 0 down a mip pyramid then Pass 1 back up. _Offset scales the sample
// spread per level. NO shader_feature -> single variant, no stripping concern.
Shader "UIA/HudBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Offset ("Sample Offset", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _Offset;

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

        // Pass 0: DOWNSAMPLE (center 0.5 + 4 diagonal corners 0.125 each)
        Pass
        {
            Name "Downsample"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDown
            fixed4 fragDown(v2f i) : SV_Target
            {
                float2 hp = _MainTex_TexelSize.xy * _Offset;   // half-pixel offset at this level
                fixed4 sum = tex2D(_MainTex, i.uv) * 4.0;       // center: 4/8 = 0.5
                sum += tex2D(_MainTex, i.uv + float2( hp.x,  hp.y));
                sum += tex2D(_MainTex, i.uv + float2(-hp.x,  hp.y));
                sum += tex2D(_MainTex, i.uv + float2( hp.x, -hp.y));
                sum += tex2D(_MainTex, i.uv + float2(-hp.x, -hp.y));
                return sum / 8.0;                               // each corner: 1/8 = 0.125
            }
            ENDCG
        }

        // Pass 1: UPSAMPLE (axis taps weight 1, diagonal taps weight 2, normalized by 12)
        Pass
        {
            Name "Upsample"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragUp
            fixed4 fragUp(v2f i) : SV_Target
            {
                float2 hp = _MainTex_TexelSize.xy * _Offset;
                fixed4 sum = tex2D(_MainTex, i.uv + float2(-hp.x * 2.0, 0.0));
                sum += tex2D(_MainTex, i.uv + float2(-hp.x,  hp.y)) * 2.0;
                sum += tex2D(_MainTex, i.uv + float2( 0.0,  hp.y * 2.0));
                sum += tex2D(_MainTex, i.uv + float2( hp.x,  hp.y)) * 2.0;
                sum += tex2D(_MainTex, i.uv + float2( hp.x * 2.0, 0.0));
                sum += tex2D(_MainTex, i.uv + float2( hp.x, -hp.y)) * 2.0;
                sum += tex2D(_MainTex, i.uv + float2( 0.0, -hp.y * 2.0));
                sum += tex2D(_MainTex, i.uv + float2(-hp.x, -hp.y)) * 2.0;
                return sum / 12.0;
            }
            ENDCG
        }
    }
}
