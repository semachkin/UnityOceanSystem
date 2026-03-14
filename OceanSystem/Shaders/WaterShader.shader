Shader "Custom/WaterShader"
{
    Properties
    {
        _WaterColor ("Water Color", Color) = (0, 0.5, 0.7, 1)
        _DeepColor ("Deep Color", Color) = (0, 0.5, 0.7, 1)
        _DeepRange ("Deep Range", Range(1, 50)) = 50
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.5

        _NoiseTex ("Wave Noise", 2D) = "gray" {}
        _NoiseScale ("Noise Scale", Range(0, 1)) = 0.05

        _BaseColorTex ("Base Color", 2D) = "white" {}
        _NormalTex ("Normal Map", 2D) = "bump" {}
        _RoughnessTex ("Roughness", 2D) = "white" {}
        _NormalStrength ("Normal Strength", Range(0, 5)) = 1
        _TexScale ("Texture Scale", Range(0, 1)) = 1
        _BaseColorStrength ("Base Color Strength", Range(0, 1)) = 0.5

        _NormalNoiseTex ("Normal Noise", 2D) = "white" {}
        _NormalNoiseScale ("Normal Noise Scale", Range(0, 1)) = 0.05
        _NormalNoiseStrength ("Normal Noise Strength", Range(0, 1)) = 0.02
        _NormalNoiseSpeed ("Normal Noise Speed", Range(0, 10)) = 0.05

        _FogColor ("Fog Color", Color) = (1, 1, 1, 1)
        _Fog ("Fog", Range(0, 1)) = 0
        _FogMin ("Fog Min", Float) = 200
        _FogMax ("Fog Max", Float) = 500

        _SpectrumTex ("Spectrum Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { 
            "Queue"="Transparent" 
            "RenderType"="Transparent" 
        }
        LOD 200
        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _CameraDepthTexture;

            float4 _WaterColor;
            float4 _DeepColor;
            float _DeepRange;
            float _ReflectionStrength;

            sampler2D _NoiseTex;
            float _NoiseScale;

            sampler2D _BaseColorTex;
            sampler2D _NormalTex;
            sampler2D _RoughnessTex;
            float _NormalStrength;
            float _TexScale;
            float _BaseColorStrength;

            sampler2D _NormalNoiseTex;
            float _NormalNoiseScale;
            float _NormalNoiseSpeed;
            float _NormalNoiseStrength;

            float4 _FogColor;
            float _Fog;
            float _FogMin;
            float _FogMax;

            sampler2D _SpectrumTex;

            /*struct appdata_full {
                float4 vertex : POSITION;
                float4 tangent : TANGENT;
                float3 normal : NORMAL;
                float4 texcoord : TEXCOORD0;
                float4 texcoord1 : TEXCOORD1;
                float4 texcoord2 : TEXCOORD2;
                float4 texcoord3 : TEXCOORD3;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };*/

            struct v2f
            {
                float4 screenPos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD2;
            };

            v2f vert (appdata_full v)
            {
                float3 base = v.vertex.xyz;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                float3 p = base;

			    v.vertex.xyz = p;
                
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = worldPos;
                o.screenPos = ComputeScreenPos(o.pos);
                o.normal = normalize(UnityObjectToWorldNormal(v.normal));

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {                
                float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
                float waterDepth = LinearEyeDepth(i.screenPos.z / i.screenPos.w);

                float dist2 = length(_WorldSpaceCameraPos - i.worldPos);

                float diff = abs(sceneDepth - waterDepth);

                float2 uv = i.worldPos.xz * _TexScale;
                float4 baseTex = tex2D(_BaseColorTex, uv);
                float roughness = tex2D(_RoughnessTex, uv).r;

                float2 noiseUV = i.worldPos.xz * _NormalNoiseScale + _Time.y * _NormalNoiseSpeed;

                float2 noise = tex2D(_NormalNoiseTex, noiseUV).rg * 2 - 1;

                uv += noise * _NormalNoiseStrength;

                float3 nTex = UnpackNormal(tex2D(_NormalTex, uv));
                nTex.xy *= _NormalStrength;
                nTex = normalize(nTex);

                float4 baseColor = lerp(_WaterColor, _DeepColor, smoothstep(0, _DeepRange, diff)); 

                float3 worldNormal = normalize(
                    nTex.x * float3(1,0,0) +
                    nTex.y * float3(0,0,1) +
                    nTex.z * i.normal
                );

                float reflectionStrength = lerp(_ReflectionStrength, 0, roughness);

                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 reflDir = reflect(-viewDir, worldNormal);

                float3 skyColor = DecodeHDR(UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, reflDir), unity_SpecCube0_HDR);

                baseColor.rgb = lerp(baseColor, baseTex, _BaseColorStrength);
                baseColor.rgb = lerp(baseColor.rgb, skyColor, reflectionStrength);

                float4 col = lerp(baseColor, _FogColor, smoothstep(_FogMin, _FogMax, dist2) * _Fog);

                return col;
            }

            ENDCG
        }
    }
}