Shader "Luna/WhiteboardBrush"
{
    // 只用来往笔迹 RenderTexture 里盖章：一段带圆头的线段就是一次 DrawProcedural。
    // 顶点直接输出裁剪空间坐标，不依赖相机矩阵。
    Properties
    {
        _BrushColor ("Color", Color) = (0,0,0,1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            float2 _BrushA;      // 线段起点（UV）
            float2 _BrushB;      // 线段终点（UV）
            float2 _BrushMin;    // 覆盖四边形的 UV 左下角
            float2 _BrushMax;    // 覆盖四边形的 UV 右上角
            float2 _BrushRadius; // 每轴半径（UV），用于画正圆
            float4 _BrushColor;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(uint id : SV_VertexID)
            {
                float2 corner;

                if (id == 0)      corner = float2(0, 0);
                else if (id == 1) corner = float2(1, 0);
                else if (id == 2) corner = float2(1, 1);
                else if (id == 3) corner = float2(0, 0);
                else if (id == 4) corner = float2(1, 1);
                else              corner = float2(0, 1);

                v2f o;
                o.uv = lerp(_BrushMin, _BrushMax, corner);

                float2 clip = o.uv * 2.0 - 1.0;

                // 直接写裁剪空间会绕过 Unity 渲染到纹理时对投影矩阵的翻转，
                // 在 D3D 上写进去的图上下颠倒。这里补回这一次翻转，
                // 让 RT 的 v 轴与 uv.y 一致（显示端才能直接用本地坐标采样）。
                #if defined(UNITY_UV_STARTS_AT_TOP)
                    clip.y = -clip.y;
                #endif

                o.pos = float4(clip, 0.5, 1.0);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 pa = i.uv - _BrushA;
                float2 ba = _BrushB - _BrushA;

                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-8));
                float2 q = (pa - ba * h) / _BrushRadius;

                if (dot(q, q) > 1.0)
                    discard;

                return _BrushColor;
            }
            ENDCG
        }
    }
}
