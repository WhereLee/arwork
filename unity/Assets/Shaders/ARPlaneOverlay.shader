Shader "ARWork/PlaneOverlay"
{
    Properties
    {
        _Color ("Color", Color) = (1.0, 1.0, 1.0, 0.22)
        _GridSize ("Grid Cell Size (m)", Float) = 0.25
        _LineWidth ("Grid Line Width (m)", Float) = 0.012
        _LineBoost ("Grid Line Alpha Boost", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _GridSize;
            float _LineWidth;
            float _LineBoost;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 按平面法线选投影面：朝上铺 xz，朝前后铺 xy，朝左右铺 zy
                float3 n = abs(normalize(i.worldNormal));
                float2 coord;

                if (n.y >= n.x && n.y >= n.z)
                {
                    coord = i.worldPos.xz;
                }
                else if (n.z >= n.x)
                {
                    coord = i.worldPos.xy;
                }
                else
                {
                    coord = i.worldPos.zy;
                }

                float2 cell = frac(coord / _GridSize);
                float2 distToEdge = min(cell, 1.0 - cell) * _GridSize;
                float lineDist = min(distToEdge.x, distToEdge.y);
                float lineMask = 1.0 - smoothstep(0.0, _LineWidth, lineDist);

                fixed4 col = _Color;
                col.a = saturate(col.a + lineMask * _LineBoost * col.a * 2.0);
                return col;
            }
            ENDCG
        }
    }
}
