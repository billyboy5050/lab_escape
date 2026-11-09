// Vertex-coloured, unlit: tile highlights, lines and combat effects. Its texture coordinate's x says how strongly a vertex pulses.
Shader "LabEscape/Overlay"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Int) = 4
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Int) = 5
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend [_SrcBlend] OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float pulse = 0.5 + 0.5 * sin(_Time.y * 5.0);
                o.color = v.color;
                o.color *= lerp(1.0, 0.5 + 0.5 * pulse, v.uv.x);
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
