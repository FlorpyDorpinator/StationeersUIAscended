Shader "Custom/Hologram" {
	Properties {
		[Header(Color)] _Color ("Color", Vector) = (1,0,0,1)
		_MainTex ("MainTexture", 2D) = "white" {}
		[Header(General)] _Brightness ("Brightness", Range(0.1, 6)) = 4
		_Alpha ("Alpha", Range(0, 1)) = 0.097
		_Direction ("Direction", Vector) = (0,1,0,0)
		[Header(Scanlines)] _ScanEnabled ("Scanlines Enabled", Range(0, 1)) = 1
		_ScanTiling ("Scan Tiling", Range(0.01, 1000)) = 160
		_ScanSpeed ("Scan Speed", Range(-2, 2)) = 0
		[Space(10)] [Header(Fresnel)] _FresnelColor ("Fresnel Color", Vector) = (1,1,1,1)
		_FresnelPower ("Fresnel Power", Range(0.1, 10)) = 1.45
		_Offset ("Offset", Float) = -300
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType"="Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;
			float4 _MainTex_ST;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Vertex_Stage_Output
			{
				float2 uv : TEXCOORD0;
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.uv = (input.uv.xy * _MainTex_ST.xy) + _MainTex_ST.zw;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			Texture2D<float4> _MainTex;
			SamplerState sampler_MainTex;
			float4 _Color;

			struct Fragment_Stage_Input
			{
				float2 uv : TEXCOORD0;
			};

			float4 frag(Fragment_Stage_Input input) : SV_TARGET
			{
				return _MainTex.Sample(sampler_MainTex, input.uv.xy) * _Color;
			}

			ENDHLSL
		}
	}
}