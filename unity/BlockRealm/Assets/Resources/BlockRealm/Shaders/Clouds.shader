// Flat, blocky cloud layer that follows the camera and drifts with the wind.
Shader "BlockRealm/Clouds"
{
    Properties
    {
        _MainTex ("Cloud mask", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Scroll ("Scroll", Vector) = (0, 0, 0, 0)
        _Scale ("World units per texel", Float) = 12
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Color;
            float4 _Scroll;
            float _Scale;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float dist : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                o.pos = mul(UNITY_MATRIX_VP, world);
                o.uv = (world.xz + _Scroll.xy) / (_Scale * 64.0);
                o.dist = distance(world.xz, _WorldSpaceCameraPos.xz);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 m = tex2D(_MainTex, i.uv);
                fixed4 c = _Color;
                c.a = m.a * _Color.a * (1.0 - smoothstep(600.0, 1100.0, i.dist));
                clip(c.a - 0.01);
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
