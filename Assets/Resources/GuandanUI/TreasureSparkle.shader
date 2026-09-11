Shader "Guandan/TreasureSparkle"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = abs(i.uv * 2 - 1);
                float r2 = dot(p, p);
                float halo = exp(-r2 * 5.5) * 0.45;
                float core = exp(-r2 * 45);
                float star = (exp(-p.x * 65 - p.y * 4) + exp(-p.y * 65 - p.x * 4)) * 0.6;
                float edge = saturate((1 - max(p.x, p.y)) * 5);
                return fixed4(i.color.rgb, saturate(halo + core + star) * edge * i.color.a);
            }
            ENDCG
        }
    }
}
