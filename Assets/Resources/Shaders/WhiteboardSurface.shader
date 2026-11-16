Shader "Luna/WhiteboardSurface"
{
    // 白板书写面：只负责把笔迹 RenderTexture 贴到表面，不保存任何笔画数据。
    //
    // 采样坐标不使用网格 UV，而是由物体本地坐标算出来：
    //   u = (positionOS.x - center.x) / size.x + 0.5
    //   v = (positionOS.y - center.y) / size.y + 0.5
    // 与 WhiteboardSurface.LocalToUv 是同一个公式，
    // 所以"笔尖在哪"和"笔迹显示在哪"用的是同一套坐标。
    Properties
    {
        _MainTex ("Stroke Map", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _SurfaceCenter ("Surface Center (Object Space)", Vector) = (0,0,0,0)
        _SurfaceSize ("Surface Size (Object Space)", Vector) = (1,1,1,0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                float4 _SurfaceCenter;
                float4 _SurfaceSize;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;

                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);

                float2 size = max(_SurfaceSize.xy, float2(1e-4, 1e-4));

                o.uv = (v.positionOS.xy - _SurfaceCenter.xy) / size + 0.5;

                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Tint;
            }
            ENDHLSL
        }
    }
}
