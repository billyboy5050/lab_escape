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
                float side : TEXCOORD2;
            };

            fixed4 _Color;
            float _Flash;
            float4 _LabAmbient;
            float4 _LabLightPos[4];
            float4 _LabLightCol[4];
            // The room's light: x and y are the middle of the floor (world x and z), z how far it reaches; 0 turns it off.
            float4 _LabRoom;

            // A face that stands up is darker near the floor, so blocks and figures stand on it: this much at the floor, back to
            // full by this height.
            static const float FootShade = 0.58;
            static const float FootHeight = 0.5;
            // Under the room's light the middle of the floor is this much brighter and the walls this much darker.
            static const float PoolBright = 1.1;
            static const float PoolDim = 0.8;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = UnityObjectToWorldNormal(v.normal);
                float key = saturate(dot(n, normalize(float3(0.45, 0.85, -0.2))));
                o.lit = 0.5 + 0.75 * key;
                o.side = 1.0 - saturate(n.y);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 base = i.color.rgb;
                float lit = i.lit;
                lit *= lerp(1.0, lerp(FootShade, 1.0, smoothstep(-0.02, FootHeight, i.world.y)), i.side);
                if (_LabRoom.z > 0.0)
                {
                    float d = length(i.world.xz - _LabRoom.xy) / _LabRoom.z;
                    lit *= lerp(PoolBright, PoolDim, smoothstep(0.15, 1.05, d));
                }
                float3 c = base * lit * _LabAmbient.rgb;
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
