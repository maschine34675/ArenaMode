// Independently authored for ArenaMode. No external shader/code/asset copied.
Shader "ArenaMode/VisibleBotEffects"
{
    Properties { _MainTex ("Visible body mask", 2D) = "black" {} }
    SubShader
    {
        // Mask contains effect bits, eye depth, body tag and coverage. No body
        // expansion, depth tolerance, stencil edits or scene material mutation.
        Pass
        {
            Name "VISIBLE_BODY_MASK"
            Cull Back ZWrite Off ZTest Always Blend Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vertexMask
            #pragma fragment fragmentMask
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _CameraDepthTexture_TexelSize;
            float _ArenaEffectFlags, _ArenaBodyTag;
            struct MaskOutput { float4 position : SV_POSITION; float4 screen : TEXCOORD0; float depth : TEXCOORD1; };
            MaskOutput vertexMask(float4 vertex : POSITION)
            {
                MaskOutput o;
                o.position = UnityObjectToClipPos(vertex);
                o.screen = ComputeScreenPos(o.position);
                o.depth = -UnityObjectToViewPos(vertex).z;
                return o;
            }
            float4 fragmentMask(MaskOutput i) : SV_Target
            {
                // The engine's missing/default depth texture is 1x1: never
                // render with it, even if a previous camera requested depth.
                clip(min(abs(_CameraDepthTexture_TexelSize.z), abs(_CameraDepthTexture_TexelSize.w)) - 2);
                float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                clip(sceneDepth - i.depth); // zero positive bias: thin cover wins
                clip(i.depth - 0.001);
                return float4(_ArenaEffectFlags, i.depth, _ArenaBodyTag, 1);
            }
            ENDCG
        }
        Pass
        {
            Name "INNER_CONTOUR"
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment fragmentContour
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _CameraDepthTexture_TexelSize;
            float4 _ArenaSpawnColor, _ArenaShieldColor, _ArenaDamageColor;
            float DifferentBody(float2 uv, float tag)
            {
                float4 other = tex2D(_MainTex, uv);
                return other.a < 0.5 || abs(other.b - tag) > 0.25;
            }
            float4 fragmentContour(v2f_img i) : SV_Target
            {
                float4 center = tex2D(_MainTex, i.uv);
                clip(center.a - 0.5); // strictly inside visible geometry, never an outer halo
                clip(min(abs(_CameraDepthTexture_TexelSize.z), abs(_CameraDepthTexture_TexelSize.w)) - 2);
                float2 depthUV = i.uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0) depthUV.y = 1 - depthUV.y;
                #endif
                float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, depthUV));
                clip(sceneDepth - center.g);
                float2 pixel = abs(_MainTex_TexelSize.xy) * 2;
                float edge = DifferentBody(i.uv + float2(pixel.x,0), center.b)
                           + DifferentBody(i.uv - float2(pixel.x,0), center.b)
                           + DifferentBody(i.uv + float2(0,pixel.y), center.b)
                           + DifferentBody(i.uv - float2(0,pixel.y), center.b);
                clip(edge - 0.5);
                int bits = (int)round(center.r);
                int spawn = bits & 1;
                int shield = (bits >> 1) & 1;
                int damage = (bits >> 2) & 1;
                int count = spawn + shield + damage;
                clip(count - 0.5);
                // Fixed eight-pixel color segments make simultaneous effects
                // readable without oversized concentric objects or flicker.
                uint segment = (uint)floor(i.uv.y / abs(_MainTex_TexelSize.y) / 8) % (uint)count;
                float4 color;
                if (spawn != 0 && segment == 0) color = _ArenaSpawnColor;
                else if (shield != 0 && segment == (uint)spawn) color = _ArenaShieldColor;
                else color = _ArenaDamageColor;
                color.a = 0.9;
                return color;
            }
            ENDCG
        }
    }
    FallBack Off
}
