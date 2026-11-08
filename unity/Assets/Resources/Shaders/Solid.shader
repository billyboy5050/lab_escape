// The greybox's one lit material. A fixed key light and fill are worked out in the vertex stage, so the three visible faces of
// a block read apart without any light objects, shadow maps or a render pipeline package.
Shader "LabEscape/Solid"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Flash ("Flash", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            fixed4 _Color;
            float _Flash;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = UnityObjectToWorldNormal(v.normal);
                float key = saturate(dot(n, normalize(float3(0.45, 0.85, -0.2))));
                float light = 0.5 + 0.75 * key;
                fixed4 c = v.color * _Color;
                c.rgb = lerp(c.rgb * light, float3(1, 1, 1), _Flash);
                o.color = c;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
