// Creatures, players and dragons: a textured, simply lit shader that follows the world's light level at the
// entity's position, with a tint used for the red hurt flash.
Shader "BlockRealm/Entity"
{
    Properties
    {
        _MainTex ("Skin", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Light ("Sky, block light", Vector) = (1, 0, 0, 0)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            float4 _Light;
            float _Cutoff;
            float _BR_Daylight;
            float4 _BR_SkyAmbient;
            float4 _BR_TorchColor;
            float4 _BR_NightTint;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 nrm : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.nrm = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                clip(tex.a - _Cutoff);
                float sky = pow(saturate(_Light.x), 1.7);
                float blk = pow(saturate(_Light.y), 1.7);
                float ndl = saturate(dot(normalize(i.nrm), _WorldSpaceLightPos0.xyz));
                float3 light = (_BR_SkyAmbient.rgb * 0.8 + _LightColor0.rgb * ndl * 0.85) * sky * _BR_Daylight
                             + _BR_NightTint.rgb * sky * (1.0 - _BR_Daylight) * 0.9 + _BR_TorchColor.rgb * blk + 0.05;
                fixed4 c;
                c.rgb = tex.rgb * _Tint.rgb * light;
                c.a = 1;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    // The fallback's shadow pass lets creatures cast shadows.
    FallBack "Legacy Shaders/VertexLit"
}
