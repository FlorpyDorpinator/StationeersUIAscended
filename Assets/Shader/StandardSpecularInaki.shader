Shader "StandardSpecularInaki"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf StandardSpecular fullforwardshadows addshadow noambient
        #pragma target 3.0

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutputStandardSpecular o)
        {

            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);

            o.Albedo = c.rgb;
            o.Alpha = c.a;

            o.Specular = 0.1;
            o.Smoothness = 0.5;
            o.Emission = 0;

        }
        ENDCG
    }

    Fallback "Diffuse"
}