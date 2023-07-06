Shader "Unlit/Grass"
{
    Properties
    {
        _Colour("Colour", Color) = (1, 1, 1)
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
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            StructuredBuffer<float4> _Position;
            float _Rotation;
            float4 _Colour;
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
                float4 pos = _Position[instanceID];
                float3 localPos = RotateAroundYInDegrees(v.vertex, _Rotation).xyz;
                float4 worldPos = float4(pos.xyz + localPos,1.0f);

                //if (ShouldCullVert(worldPos.xyz,_CullingBias)) {
                //    o.vertex = 0.0f; //TODO: Find a better way to cull
                //}
                //else {
                    o.vertex = UnityObjectToClipPos(worldPos);
                //}
                //worldPos.y *= _Position[instanceID].w;
                
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture

                float3 lightDir = _WorldSpaceLightPos0.xyz;
                float ndotl = DotClamped(lightDir, normalize(float3(0, 1, 0)));

                return _Colour * ndotl * i.uv.y;
            }
            ENDCG
        }
    }
}
