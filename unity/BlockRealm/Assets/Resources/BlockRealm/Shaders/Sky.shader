// Procedural sky: gradient, square sun and moon from sprites, and stars at night. Driven by SkyController.
Shader "BlockRealm/Sky"
{
    Properties
    {
        _SunTex ("Sun", 2D) = "white" {}
        _MoonTex ("Moon", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _SunTex;
            sampler2D _MoonTex;
            float4 _ZenithColor;
            float4 _HorizonColor;
            float4 _GroundColor;
            float4 _SunDir;        // normalised direction towards the sun
            float4 _SunRight;      // a vector perpendicular to _SunDir, for sprite coordinates
            float4 _SunUp;
            float _NightAmount;    // 0 day .. 1 night
            float _SunSize;
            float _MoonSize;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float4 sprite(sampler2D tex, float3 d, float3 center, float3 right, float3 up, float size)
            {
                float facing = dot(d, center);
                if (facing <= 0.0) return float4(0, 0, 0, 0);
                // Project the view direction onto the sprite plane, scaled so the plane is at distance 1.
                float2 p = float2(dot(d, right), dot(d, up)) / facing;
                float2 uv = p / (size * 2.0) + 0.5;
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return float4(0, 0, 0, 0);
                return tex2D(tex, uv);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 col;
                if (h >= 0.0)
                {
                    float t = pow(saturate(h), 0.55);
                    col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);
                }
                else
                {
                    col = lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-h * 6.0));
                }

                // Stars fade in as night falls and only above the horizon.
                float starCell = hash(floor(d * 220.0));
                float star = step(0.9965, starCell) * saturate(h * 4.0) * _NightAmount;
                col += star * 0.9;

                float3 sunDir = normalize(_SunDir.xyz);
                float4 sun = sprite(_SunTex, d, sunDir, _SunRight.xyz, _SunUp.xyz, _SunSize);
                col = lerp(col, sun.rgb * 1.6, sun.a);
                float4 moon = sprite(_MoonTex, d, -sunDir, _SunRight.xyz, _SunUp.xyz, _MoonSize);
                col = lerp(col, moon.rgb * 1.1, moon.a * _NightAmount);

                // A soft glow around the sun (and warmer near the horizon at dusk and dawn).
                float glow = pow(saturate(dot(d, sunDir)), 24.0) * (1.0 - _NightAmount);
                col += glow * float3(1.0, 0.82, 0.55) * 0.35;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
