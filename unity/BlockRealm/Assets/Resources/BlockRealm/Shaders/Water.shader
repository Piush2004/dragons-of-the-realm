// Translucent water: tinted, gently animated, lit by the same baked light as the terrain.
Shader "BlockRealm/Water"
{
    Properties
    {
        _TileArray ("Tile array", 2DArray) = "" {}
        _Alpha ("Opacity", Range(0, 1)) = 0.66
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        LOD 200

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            UNITY_DECLARE_TEX2DARRAY(_TileArray);
            float _Alpha;
            float _BR_Daylight;
            float4 _BR_SkyAmbient;
            float4 _BR_TorchColor;
            float4 _BR_NightTint;

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
                float3 wpos : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                // Faces flagged as the surface (alpha byte 1) ripple a little.
                float surface = step(0.001, v.color.a) * step(v.color.a, 0.006);
                world.y += surface * sin(_Time.y * 1.6 + world.x * 0.9 + world.z * 0.7) * 0.025;
                o.pos = mul(UNITY_MATRIX_VP, world);
                o.uv = v.uv;
                o.col = v.color;
                o.nrm = UnityObjectToWorldNormal(v.normal);
                o.wpos = world.xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 flow = float2(_Time.y * 0.03, _Time.y * 0.021);
                float3 uv = float3(i.uv.xy + flow * 0.4, i.uv.z);
                fixed4 tex = UNITY_SAMPLE_TEX2DARRAY(_TileArray, uv);
                float sky = pow(saturate(i.col.r), 1.7);
                float blk = pow(saturate(i.col.g), 1.7);
                float ndl = saturate(dot(normalize(i.nrm), _WorldSpaceLightPos0.xyz));
                float3 light = (_BR_SkyAmbient.rgb * 0.9 + _LightColor0.rgb * ndl * 0.35) * sky * _BR_Daylight
                             + _BR_NightTint.rgb * sky * (1.0 - _BR_Daylight) + _BR_TorchColor.rgb * blk + 0.03;
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.wpos);
                float fresnel = pow(1.0 - saturate(dot(viewDir, float3(0, 1, 0))), 3.0);
                fixed4 c;
                c.rgb = tex.rgb * light + fresnel * 0.12 * sky * _BR_Daylight;
                c.a = saturate(_Alpha + fresnel * 0.25);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
