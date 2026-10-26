Shader "ARWork/PlaneOverlay"
{
    Properties
    {
        _Color ("Color", Color) = (0.25, 0.65, 1.0, 0.22)
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
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 按世界坐标铺方格，不规则多边形的边缘看起来就像扫描网格
                float2 coord = i.worldPos.xz;

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
