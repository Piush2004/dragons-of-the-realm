// Block terrain: samples a texture array, lights each vertex with baked sky light, block light and ambient occlusion,
// and receives and casts real-time sun shadows. Built-in render pipeline, forward rendering.
Shader "BlockRealm/Block"
{
    Properties
    {
        _TileArray ("Tile array", 2DArray) = "" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            UNITY_DECLARE_TEX2DARRAY(_TileArray);
            float _Cutoff;
            float _BR_Daylight;          // 0 = night, 1 = full day
            float4 _BR_SkyAmbient;       // ambient colour contributed by sky light at full day
            float4 _BR_TorchColor;       // warm colour of block light
            float4 _BR_NightTint;        // cool moonlit ambient

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float3 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 uv : TEXCOORD0;
                fixed4 col : TEXCOORD1;
                float3 nrm : TEXCOORD2;
                SHADOW_COORDS(3)
                UNITY_FOG_COORDS(4)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.col = v.color;
                o.nrm = UnityObjectToWorldNormal(v.normal);
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = UNITY_SAMPLE_TEX2DARRAY(_TileArray, i.uv);
                clip(tex.a - _Cutoff);

                float sky = pow(saturate(i.col.r), 1.7);
                float blk = pow(saturate(i.col.g), 1.7);
                float ao = lerp(0.40, 1.0, i.col.b);
                float shadow = SHADOW_ATTENUATION(i);
                float ndl = saturate(dot(normalize(i.nrm), _WorldSpaceLightPos0.xyz));

                float3 ambient = _BR_SkyAmbient.rgb * sky * _BR_Daylight;
                float3 sun = _LightColor0.rgb * ndl * shadow * sky * _BR_Daylight;
                float3 moon = _BR_NightTint.rgb * sky * (1.0 - _BR_Daylight);
                float3 torch = _BR_TorchColor.rgb * blk * 1.15;

                float3 light = (ambient + sun + moon) * ao + torch * lerp(0.75, 1.0, ao) + 0.025;
                fixed4 c;
                c.rgb = tex.rgb * light;
                c.a = 1;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma target 3.5
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_TileArray);
            float _Cutoff;

            struct appdataS
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float3 uv : TEXCOORD0;
            };

            struct v2fS
            {
                V2F_SHADOW_CASTER;
                float3 uv : TEXCOORD1;
            };

            v2fS vertShadow(appdataS v)
            {
                v2fS o;
                o.uv = v.uv;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 fragShadow(v2fS i) : SV_Target
            {
                fixed4 tex = UNITY_SAMPLE_TEX2DARRAY(_TileArray, i.uv);
                clip(tex.a - _Cutoff);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    FallBack Off
}
