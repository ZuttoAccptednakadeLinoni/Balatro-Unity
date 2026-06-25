Shader "UI/OldTV"
{
    Properties
    {
        [PerRendererData] _MainTex ("精灵纹理", 2D) = "white" {}
        _Color ("颜色", Color) = (1,1,1,1)

        // 旧电视 / CRT 效果
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

        // UI 必要属性
        _StencilComp ("模板比较", Float) = 8
        _Stencil ("模板ID", Float) = 0
        _StencilOp ("模板操作", Float) = 0
        _StencilWriteMask ("模板写入掩码", Float) = 255
        _StencilReadMask ("模板读取掩码", Float) = 255

        _ColorMask ("颜色掩码", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("使用Alpha裁剪", Float) = 0
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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "OldTV"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.5

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
                float4 vertex   : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord  : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float2 screenUV : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

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

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);

                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.screenUV = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            // Simple pseudo-random hash function
            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // Noise function for grain/static — time-based seed baked into uv by caller
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

            fixed4 frag(v2f IN) : SV_Target
            {
                float time = _Time.y;

                // --- Screen curvature distortion ---
                float2 uv = barrelDistortion(IN.screenUV, _DistortionIntensity * 0.3);

                // --- Vignette ---
                float2 centered = uv - 0.5;
                float vignette = 1.0 - dot(centered, centered) * 4.0 * _VignetteIntensity;
                vignette = saturate(vignette);
                // Add some organic variation to vignette
                float vignetteNoise = noise3D(uv, time * 0.5) * 0.15;
                vignette = saturate(vignette - vignetteNoise * _VignetteIntensity);

                // --- Scanlines ---
                // Two sets of scanlines for more realism
                float scanY = uv.y - time * _ScanlineSpeed;
                float scanline1 = sin(scanY * _ScanlineCount) * 0.5 + 0.5;
                float scanline2 = sin(scanY * _ScanlineCount * 1.5 + 0.3) * 0.5 + 0.5;
                float scanlines = lerp(1.0, scanline1 * scanline2, _ScanlineIntensity);
                // Soften scanlines
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

                float4 colorBase = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;
                float4 colorR = (tex2D(_MainTex, IN.texcoord + float2(rOffset, 0) * 0.01) + _TextureSampleAdd) * IN.color;
                float4 colorB = (tex2D(_MainTex, IN.texcoord + float2(bOffset, 0) * 0.01) + _TextureSampleAdd) * IN.color;

                float4 color;
                color.r = colorR.r;
                color.g = colorBase.g;
                color.b = colorB.b;
                color.a = colorBase.a;

                // --- Desaturation + Warm Shift ---
                float gray = dot(color.rgb, float3(0.299, 0.587, 0.114));
                float3 desaturated = lerp(color.rgb, gray.xxx, 1.0 - _Saturation);
                // Add warm sepia shift
                float3 warmShift = float3(1.05, 0.95, 0.8);
                float3 warmed = lerp(desaturated, desaturated * warmShift, _WarmthShift);

                // --- Noise / Static ---
                float grain = noise3D(uv, time * 50.0) * _NoiseIntensity;
                // Add some blue-channel noise for authenticity
                float blueNoise = noise3D(uv * 1.7, time * 60.0) * _NoiseIntensity * 0.5;
                float staticEffect = grainNoise(uv * 500.0 + time * 7.0) * _NoiseIntensity * 0.3;

                float3 noised = warmed + grain * 0.3 + staticEffect * 0.5;
                noised.b += blueNoise * 0.3;

                // --- Composite all effects ---
                float3 finalColor = noised * vignette * scanlines * flicker;
                float finalAlpha = color.a;

                // Apply vignette to alpha edges too (for soft edge on old TV)
                finalAlpha *= vignette;

                fixed4 result = fixed4(finalColor, finalAlpha);

                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif

                return result;
            }
        ENDCG
        }
    }

    Fallback "UI/Default"
}