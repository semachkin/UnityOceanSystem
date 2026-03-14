Shader "Custom/GerstnerShader"
{
    Properties
    {
        _WaterColor ("Water Color", Color) = (0, 0.5, 0.7, 1)
        _DeepColor ("Deep Color", Color) = (0, 0.5, 0.7, 1)
        _FoamColor ("Foam Color", Color) = (1,1,1,1)
        _DeepRange ("Deep Range", Range(1, 50)) = 50
        _FoamRange ("Foam Range", Range(0.001, 1)) = 0.1
        _TrailSpeed ("Trail Speed", Float) = 1
        _TrailRange ("Foam Trail Range", Range(0.5, 2)) = 1
        _TrailFrequency ("Trail Frequency", Range(1, 20)) = 10
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.5

        _NoiseTex ("Wave Noise", 2D) = "gray" {}
        _NoiseScale ("Noise Scale", Range(0, 1)) = 0.05

        _G ("Gravity", Float) = 9.8
        [Header(X Y direction Z stepness W wave length)]
        [Space(15)]
        _Wave1 ("Wave 1", Vector) = (0, 1, 0.2, 10)
        _Wave2 ("Wave 2", Vector) = (1, 1, 0.4, 5)
        _Wave3 ("Wave 3", Vector) = (1, 0, 0.3, 7)
        _Wave4 ("Wave 4", Vector) = (1, -1, 0.3, 7)

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

        _FoamNoiseScale ("Foam Noise Scale", Range(0, 1)) = 0.05
        _FoamNoiseStrength ("Foam Noise Strength", Range(0, 1)) = 0.02
        _FoamNoiseSpeed ("Foam Noise Speed", Range(0, 10)) = 0.05

        _FoamStrength ("Foam Strength", Range(0, 1)) = 0.5

        _FogColor ("Fog Color", Color) = (1, 1, 1, 1)
        _Fog ("Fog", Range(0, 1)) = 0
        _FogMin ("Fog Min", Float) = 200
        _FogMax ("Fog Max", Float) = 500
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
            float4 _FoamColor;
            float _FoamRange;
            float _TrailRange;
            float _DeepRange;
            float _ReflectionStrength;
            float _TrailFrequency;
            float _TrailSpeed;

            sampler2D _NoiseTex;
            float _NoiseScale;

            float _G;
            float4 _Wave1;
            float4 _Wave2;
            float4 _Wave3;
            float4 _Wave4;

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

            float _FoamNoiseScale;
            float _FoamNoiseSpeed;
            float _FoamNoiseStrength;

            float _FoamStrength;

            float4 _FogColor;
            float _Fog;
            float _FogMin;
            float _FogMax;

            struct v2f
            {
                float4 screenPos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD2;
                float foamCrest : TEXCOORD3;
            };

            float3 GerstnerWave (float4 wave, float3 pos, inout float3 basisFront, inout float3 basisRight, inout float foamCrest) {
                float2 dir = normalize(wave.xy);
                float stepness = wave.z;
                float _lambda = wave.w;

                float k = 2 * UNITY_PI / _lambda;
                float c = sqrt(_G / k); 
                float f = k * (dir.x*pos.x + dir.y*pos.z - _Time.y * c);
                float a = stepness / k;

                float2 noiseUV = pos.xz * _NoiseScale;
                float noise = tex2Dlod(_NoiseTex, float4(noiseUV, 0, 0)).r * 2 - 1;
                f += noise;

                foamCrest += saturate(stepness * sin(f));

                basisFront += normalize(float3(
                    1 - dir.x * dir.x * (stepness * sin(f)), 
                    dir.x * (stepness * cos(f)), 
                    -dir.x * dir.y * (stepness * sin(f))
                ));
                basisRight += normalize(float3(
                    - dir.x * dir.y * (stepness * sin(f)), 
                    dir.y * (stepness * cos(f)), 
                    1 - dir.y * dir.y * (stepness * sin(f))
                ));

                return float3(
                    dir.x * a * cos(f),
                    a * sin(f),
                    dir.y * a * sin(f)
                );
            }

            v2f vert (appdata_full v)
            {
                float3 base = v.vertex.xyz;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                float3 p = base;

                float3 basisFront = 0;
                float3 basisRight = 0;
                float foamCrest = 0;

                p += GerstnerWave(_Wave1, worldPos, basisFront, basisRight, foamCrest);
                p += GerstnerWave(_Wave2, worldPos, basisFront, basisRight, foamCrest);
                p += GerstnerWave(_Wave3, worldPos, basisFront, basisRight, foamCrest);
                p += GerstnerWave(_Wave4, worldPos, basisFront, basisRight, foamCrest);

                float3 newnormal = normalize(cross(basisFront, basisRight));

                v.normal = newnormal;
			    v.vertex.xyz = p;
                
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = worldPos;
                o.screenPos = ComputeScreenPos(o.pos);
                o.normal = normalize(UnityObjectToWorldNormal(v.normal));
                o.foamCrest = foamCrest;

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {                
                float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
                float waterDepth = LinearEyeDepth(i.screenPos.z / i.screenPos.w);

                float dist2 = length(_WorldSpaceCameraPos - i.worldPos);

                float diff = abs(sceneDepth - waterDepth);

                float w_x = ((cos(_TrailFrequency * diff - _Time.x * _TrailSpeed) + 1) * (max(_TrailRange - diff, 0))/_TrailRange)/2;
                float f_x = -(1/_FoamRange) * diff + 1;
                float mask = max(w_x, f_x) * _FoamStrength;

                float foamNoiseUV = i.worldPos.xz * _FoamNoiseScale + _Time.y * _FoamNoiseSpeed * _FoamNoiseStrength;
                float foamNoise = tex2D(_NormalNoiseTex, foamNoiseUV).r;
                foamNoise = foamNoise * 0.5 + 0.5;
                float foam = smoothstep(.5, 1, i.foamCrest + foamNoise * _FoamNoiseStrength) * _FoamStrength;

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

                float4 col = lerp(baseColor, _FoamColor, smoothstep(0, 1, foam + mask));

                col = lerp(col, _FogColor, smoothstep(_FogMin, _FogMax, dist2) * _Fog);

                return col;
            }

            ENDCG
        }
    }
}