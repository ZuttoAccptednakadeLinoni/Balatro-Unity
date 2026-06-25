Shader "Custom/GodotSwirl"
{
    Properties
    {
        _MainTex ("MainTex", 2D) = "white" {}
        _Colour1 ("Colour 1", Color) = (1, 0, 0, 1)
        _Colour2 ("Colour 2", Color) = (0, 0, 1, 1)
        _Colour3 ("Colour 3", Color) = (0, 0, 0, 1)
        _Colour4 ("Colour 4", Color) = (1, 1, 1, 1)
        [IntRange] _Contrast ("Contrast", Range(0, 10)) = 5
        _Gradual ("Gradual", Range(0.0, 2.0)) = 2.0
        _Width1 ("Width 1", Range(0.01, 1.0)) = 0.04
        _Width2 ("Width 2", Range(0.01, 1.0)) = 0.1
        _Scale1 ("Scale 1", Range(0.0, 100.0)) = 10.0
        _Scale2 ("Scale 2", Range(0.0, 10.0)) = 1.0
        _Offset ("Offset", Vector) = (0, 0, 0, 0)
        _Intensity ("Intensity", Range(0.0, 4.0)) = 0.2
        _SpinSpeed ("Spin Speed", Range(0.0, 10.0)) = 0.2
        _SpinAmount ("Spin Amount", Range(0.0, 10.0)) = 1.5
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 screenPos : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _Colour1;
            float4 _Colour2;
            float4 _Colour3;
            float4 _Colour4;
            int _Contrast;
            float _Gradual;
            float _Width1;
            float _Width2;
            float _Scale1;
            float _Scale2;
            float2 _Offset;
            float _Intensity;
            float _SpinSpeed;
            float _SpinAmount;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                screenUV.y = 1.0 - screenUV.y;

                float aspect = _ScreenParams.x / _ScreenParams.y;

                float2 uv = screenUV * float2(aspect, 1.0) - float2(0.5 * aspect, 0.5);
                uv *= 2.0;
                uv += _Offset;

                float uv_len = length(uv);
                float angle = atan2(uv.y, uv.x);

                float speed = _Time.y * _SpinSpeed;

                angle -= _SpinAmount * uv_len;
                angle += speed;

                uv = float2(uv_len * cos(angle), uv_len * sin(angle)) * _Scale2;
                uv *= _Scale1;

                float2 uv2 = float2(uv.x + uv.y, uv.x + uv.y);

                [unroll(10)]
                for (int j = 0; j < 10; j++)
                {
                    if (j < _Contrast)
                    {
                        uv2 += sin(uv);
                        uv += float2(cos(_Intensity * uv2.y + speed), sin(_Intensity * uv2.x - speed));
                        uv -= cos(uv.x + uv.y) - sin(uv.x - uv.y);
                    }
                }

                float paint_res = smoothstep(0.0, _Gradual, length(uv) / _Scale1);

                float c3p = 1.0 - min(_Width2, abs(paint_res - 0.5)) * (1.0 / _Width2);
                float c_out = max(0.0, (paint_res - (1.0 - _Width1))) * (1.0 / _Width1);
                float c_in  = max(0.0, -(paint_res - _Width1)) * (1.0 / _Width1);
                float c4p = c_out + c_in;

                float3 ret_col = lerp(_Colour1.rgb, _Colour2.rgb, paint_res);
                ret_col = lerp(ret_col, _Colour3.rgb, c3p);
                ret_col = lerp(ret_col, _Colour4.rgb, c4p);

                return fixed4(ret_col, 1.0);
            }
            ENDCG
        }
    }
}
