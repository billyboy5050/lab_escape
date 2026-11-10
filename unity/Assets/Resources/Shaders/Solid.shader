// The greybox's one lit material. A fixed key light and fill are worked out in the vertex stage, so the three visible faces of
// a block read apart without any light objects, shadow maps or a render pipeline package. The room's brightness and up to four
// short-lived point lights (a muzzle flash, a blast) come from global values that Lighting.cs sets.
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
                float3 world : TEXCOORD0;
                float lit : TEXCOORD1;
            };

            fixed4 _Color;
            float _Flash;
            float4 _LabAmbient;
            float4 _LabLightPos[4];
            float4 _LabLightCol[4];

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = UnityObjectToWorldNormal(v.normal);
                float key = saturate(dot(n, normalize(float3(0.45, 0.85, -0.2))));
                o.lit = 0.5 + 0.75 * key;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 base = i.color.rgb;
                float3 c = base * i.lit * _LabAmbient.rgb;
                for (int k = 0; k < 4; k++)
                {
                    float3 d = _LabLightPos[k].xyz - i.world;
                    float r = _LabLightPos[k].w;
                    float a = saturate(1.0 - dot(d, d) / (r * r));
                    // Mostly the surface's own colour lit up, plus a little of the light's, so dark steel still catches it.
                    c += (base * 1.6 + 0.07) * _LabLightCol[k].rgb * (a * a);
                }
                c = lerp(c, float3(1, 1, 1), _Flash);
                return fixed4(c, i.color.a);
            }
            ENDCG
        }
    }
}
