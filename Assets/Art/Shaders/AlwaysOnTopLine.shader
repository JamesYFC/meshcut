Shader "Custom/AlwaysOnTopLine"
{
    Properties
    {
        
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" }

        Pass
        {
            ZTest Always       // Always renders on top
            ZWrite Off         // Doesn't write to depth buffer
            Cull Off           // No face culling (good for 3D lines)
            Blend SrcAlpha OneMinusSrcAlpha // Alpha blending

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata_t {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert (appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                return i.color;
            }
            ENDHLSL
        }
    }
}