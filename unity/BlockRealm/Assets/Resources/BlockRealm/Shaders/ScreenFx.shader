// Final screen pass: gentle contrast and saturation, a soft vignette and a bright-pass glow. Used by ScreenFx.
Shader "Hidden/BlockRealm/ScreenFx"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Vignette;
            float _Contrast;
            float _Saturation;
            float _Glow;
            float4 _Tint;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                fixed4 c = tex2D(_MainTex, uv);

                // Cheap bloom: average a few wide taps and add the bright part back.
                float2 t = _MainTex_TexelSize.xy * 3.0;
                float3 blur = (tex2D(_MainTex, uv + float2(t.x, 0)).rgb + tex2D(_MainTex, uv - float2(t.x, 0)).rgb +
                               tex2D(_MainTex, uv + float2(0, t.y)).rgb + tex2D(_MainTex, uv - float2(0, t.y)).rgb) * 0.25;
                float bright = max(0.0, dot(blur, float3(0.299, 0.587, 0.114)) - 0.72);
                c.rgb += blur * bright * _Glow;

                float luma = dot(c.rgb, float3(0.299, 0.587, 0.114));
                c.rgb = lerp(float3(luma, luma, luma), c.rgb, _Saturation);
                c.rgb = (c.rgb - 0.5) * _Contrast + 0.5;
                c.rgb *= _Tint.rgb;

                float2 d = uv - 0.5;
                float v = 1.0 - dot(d, d) * _Vignette * 2.2;
                c.rgb *= saturate(v);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
