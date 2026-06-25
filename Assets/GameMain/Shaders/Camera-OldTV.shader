Shader "Camera/OldTV"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}

        // 旧电视 / CRT 效果
        _EffectStrength ("效果强度", Range(0, 1)) = 1.0
        _ScanlineIntensity ("扫描线强度", Range(0, 1)) = 0.3
        _ScanlineCount ("扫描线密度", Range(10, 500)) = 150
        _ScanlineSpeed ("扫描线漂移速度", Range(0, 0.5)) = 0.05
        _VignetteIntensity ("暗角强度", Range(0, 1)) = 0.6
        _AberrationIntensity ("色差强度", Range(0, 1)) = 0.15
        _NoiseIntensity ("噪点强度", Range(0, 1)) = 0.08
        _DistortionIntensity ("屏幕曲率", Range(0, 1)) = 0.05
        _Saturation ("饱和度", Range(0, 2)) = 0.75
        _FlickerIntensity ("闪烁强度", Range(0, 1)) = 0.04
        _WarmthShift ("暖色偏移", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "OldTVEffect"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.5

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float _EffectStrength;
            float _ScanlineIntensity;
            float _ScanlineCount;
            float _ScanlineSpeed;
            float _VignetteIntensity;
            float _AberrationIntensity;
            float _NoiseIntensity;
            float _DistortionIntensity;
            float _Saturation;
            float _FlickerIntensity;
            float _WarmthShift;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // Simple pseudo-random hash function
            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // Noise function for grain/static
            float grainNoise(float2 uv)
            {
                return hash(uv);
            }

            // 3D noise for organic feel
            float noise3D(float2 uv, float time)
            {
                float n1 = hash(floor(uv * 10.0) + time);
                float n2 = hash(floor(uv * 10.0) + float2(1.0, 0.0) + time);
                float n3 = hash(floor(uv * 10.0) + float2(0.0, 1.0) + time);
                float n4 = hash(floor(uv * 10.0) + float2(1.0, 1.0) + time);

                float2 f = frac(uv * 10.0);
                f = f * f * (3.0 - 2.0 * f);

                float n12 = lerp(n1, n2, f.x);
                float n34 = lerp(n3, n4, f.x);
                return lerp(n12, n34, f.y);
            }

            // Apply barrel distortion for screen curvature
            float2 barrelDistortion(float2 uv, float intensity)
            {
                float2 centered = uv - 0.5;
                float r2 = dot(centered, centered);
                float distortion = 1.0 + intensity * r2;
                return 0.5 + centered * distortion;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = _Time.y;

                // --- Screen curvature distortion ---
                float2 uv = barrelDistortion(i.uv, _DistortionIntensity * 0.3);

                // --- Vignette ---
                float2 centered = uv - 0.5;
                float vignette = 1.0 - dot(centered, centered) * 4.0 * _VignetteIntensity;
                vignette = saturate(vignette);
                float vignetteNoise = noise3D(uv, time * 0.5) * 0.15;
                vignette = saturate(vignette - vignetteNoise * _VignetteIntensity);

                // --- Scanlines ---
                float scanY = uv.y - time * _ScanlineSpeed;
                float scanline1 = sin(scanY * _ScanlineCount) * 0.5 + 0.5;
                float scanline2 = sin(scanY * _ScanlineCount * 1.5 + 0.3) * 0.5 + 0.5;
                float scanlines = lerp(1.0, scanline1 * scanline2, _ScanlineIntensity);
                scanlines = lerp(1.0, scanlines, _ScanlineIntensity);

                // --- Flicker ---
                float flicker = 1.0 - grainNoise(float2(time * 60.0, 0.0)) * _FlickerIntensity;
                float fastFlicker = 1.0 - grainNoise(float2(time * 120.0, 10.0)) * _FlickerIntensity * 0.5;
                flicker = flicker * fastFlicker;

                // --- Chromatic Aberration ---
                float2 direction = centered;
                float dist = length(direction) * 2.0;
                float aberrationStrength = dist * dist * _AberrationIntensity;

                float rOffset = aberrationStrength * 1.0;
                float bOffset = aberrationStrength * -1.0;

                float4 colorBase = tex2D(_MainTex, uv);
                float4 colorR = tex2D(_MainTex, uv + float2(rOffset, 0) * 0.01);
                float4 colorB = tex2D(_MainTex, uv + float2(bOffset, 0) * 0.01);

                float4 color;
                color.r = colorR.r;
                color.g = colorBase.g;
                color.b = colorB.b;
                color.a = colorBase.a;

                // --- Desaturation + Warm Shift ---
                float gray = dot(color.rgb, float3(0.299, 0.587, 0.114));
                float3 desaturated = lerp(color.rgb, gray.xxx, 1.0 - _Saturation);
                float3 warmShift = float3(1.05, 0.95, 0.8);
                float3 warmed = lerp(desaturated, desaturated * warmShift, _WarmthShift);

                // --- Noise / Static ---
                float grain = noise3D(uv, time * 50.0) * _NoiseIntensity;
                float blueNoise = noise3D(uv * 1.7, time * 60.0) * _NoiseIntensity * 0.5;
                float staticEffect = grainNoise(uv * 500.0 + time * 7.0) * _NoiseIntensity * 0.3;

                float3 noised = warmed + grain * 0.3 + staticEffect * 0.5;
                noised.b += blueNoise * 0.3;

                // --- Composite all effects ---
                float3 finalColor = noised * vignette * scanlines * flicker;

                // Blend with original based on _EffectStrength
                float3 original = tex2D(_MainTex, i.uv).rgb;
                finalColor = lerp(original, finalColor, _EffectStrength);

                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
