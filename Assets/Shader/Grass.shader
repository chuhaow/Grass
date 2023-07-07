Shader "Unlit/Grass"
{
    Properties
    {
        _Colour1("Colour1", Color) = (1, 1, 1)
        _Colour2("Colour2", Color) = (1, 1, 1)
        _AOColour("Ambient Occlusion Colour", Color) = (1,1,1)
        _TipColour("Grass Tip Colour", Color) = (1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _CullingBias("Cull Bias", Range(0.1, 1000.0)) = 500
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull Off
        Zwrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"
            #include "UnityPBSLighting.cginc"
            #include "AutoLight.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                float saturation : TEXCOORD1;
            };

            struct GrassData {
                float4 position;
                float saturation;
                float2 worldUV;
            };

            sampler2D _MainTex, _WindTex;
            float4 _MainTex_ST;
            StructuredBuffer<GrassData> _GrassData;
            float _Rotation;
            float4 _Colour1, _Colour2, _AOColour, _TipColour;
            float _CullingBias;

            float4 RotateAroundYInDegrees(float4 vertex, float degrees) {
                float alpha = 0 * UNITY_PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float4(mul(m, vertex.xz), vertex.yw).xzyw;
            }

            bool ShouldCullVert(float3 vert, float bias) {
                float4 leftPlane = unity_CameraWorldClipPlanes[0];
                float4 rightPlane = unity_CameraWorldClipPlanes[1];
                float4 botPlane = unity_CameraWorldClipPlanes[2];
                float4 topPlane = unity_CameraWorldClipPlanes[3];
                float4 nearPlane = unity_CameraWorldClipPlanes[4];
                float4 farPlane = unity_CameraWorldClipPlanes[5];
                return  dot(float4(vert, 1), leftPlane) > bias || dot(float4(vert, 1), rightPlane) > bias
                    || dot(float4(vert, 1), botPlane) > bias || dot(float4(vert, 1), topPlane) > bias
                    || dot(float4(vert, 1), nearPlane) > bias || dot(float4(vert, 1), farPlane) > bias;
            }

            v2f vert (appdata v, uint instanceID : SV_INSTANCEID)
            {
                v2f o;
                float4 pos = _GrassData[instanceID].position;
                float3 localPos = RotateAroundYInDegrees(v.vertex, _Rotation).xyz;
                localPos.y *= pos.w;
                localPos.xz += pos.w * tex2Dlod(_WindTex, _GrassData[instanceID].worldUV.y) * v.uv.y; // multiple by uv.y to keep base still
                float4 worldPos = float4(pos.xyz + localPos,1.0f);
                //worldPos.xz += tex2Dlod(_WindTex, _GrassData[instanceID].uv.y);

                //if (ShouldCullVert(worldPos.xyz,_CullingBias)) {
                //    o.vertex = 0.0f; //TODO: Find a better way to cull
                //}
                //else {
                    o.vertex = UnityObjectToClipPos(worldPos);
                //}
                //worldPos.y *= _Position[instanceID].w;
                
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.saturation = max(0.5f, 1.0f - (localPos.y- 1.0f / 1.5f));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 col = lerp(_Colour1, _Colour2, i.uv.y);
                //float4 col = tex2D(_WindTex,i.uv);
                //return col;
                float3 lightDir = _WorldSpaceLightPos0.xyz;
                float ndotl = DotClamped(lightDir, normalize(float3(0, 1, 0)));
                col += lerp(0.0f, _TipColour, i.uv.y * i.uv.y * i.uv.y); // cubing uv.y so it is closer to tip color near top
                float sat = lerp(1.0f, i.saturation, i.uv.y );
                col /= i.saturation;
                col = saturate(col);
                float4 aoCol = lerp(_AOColour, 1.0f, i.uv.y);
                return col * ndotl * aoCol;
            }
            ENDCG
        }
    }
}
