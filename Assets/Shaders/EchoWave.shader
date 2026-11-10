// EchoDev/EchoWave —— 全黑场景的"回声可视化"材质（URP / HLSL）
//
// 原理：EchoWaveManager 每帧把最多 16 圈波写入全局数组
//   _EchoOrigins[i] : xyz = 波心(世界坐标)  w = 强度
//   _EchoData[i]    : x = 当前半径  y = 最大半径  z = 扩散速度  w = 已存活时间
//   _EchoColors[i]  : rgb = 波颜色
//   _EchoCount      : 当前波数
//
// 像素级效果：
//   1. 主波环 —— 细锐线条，带手绘感的线宽抖动 + 断线
//   2. 拖尾环 —— 主环后 2 层渐宽渐淡的晕开环（多层涟漪）
//   3. 波内   —— 波扫过的区域内，物体获得带色微光（"被声音照亮"）
//   4. 菲涅尔 —— 波照明下的物体边缘泛光，轮廓浮现
//   5. 消散   —— 亮度随扩散进度平滑淡出（尾段缓慢消散，无跳变）
//
// 地面和道具都用这一个材质即可。

Shader "EchoDev/EchoWave"
{
    Properties
    {
        _BaseColor         ("底色(近黑)", Color) = (0.008, 0.010, 0.014, 1)
        _RingWidth         ("主环线宽(米)", Float) = 0.08
        _TrailGap          ("拖尾环间距(米)", Float) = 0.55
        _TrailFade         ("拖尾环衰减", Range(0, 1)) = 0.45
        _WobbleAmount      ("手绘线宽抖动", Range(0, 1)) = 0.5
        _DashAmount        ("手绘断线程度", Range(0, 1)) = 0.35
        _RingIntensity     ("波环亮度", Float) = 2.2
        _InteriorIntensity ("波内照明亮度", Float) = 0.45
        _InteriorFalloff   ("波内前沿渐变(米)", Float) = 1.2
        _FresnelPower      ("边缘光聚拢", Range(1, 8)) = 3
        _FresnelIntensity  ("边缘光亮度", Float) = 1.2
        _OutlineColor      ("描边颜色(独立于波色)", Color) = (0.85, 0.97, 1.0, 1)
        _OutlineWidth      ("描边宽度(像素)", Float) = 2.0
        _OutlineDepthThreshold ("描边深度阈值(米)", Float) = 0.05
        _OutlineIntensity  ("描边亮度", Float) = 2.2

        _HullWidth         ("轮廓壳宽度(米)", Float) = 0.03
        _HullColor         ("轮廓颜色", Color) = (0.55, 1.0, 0.85, 1)
        _HullDelay         ("波扫过后浮现延迟(米)", Float) = 0.8
        _HullRamp          ("浮现渐变长度(米)", Float) = 1.5
        _HullIntensity     ("轮廓亮度", Float) = 2.0
        _MinVisible        ("波外微光", Range(0, 1)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "EchoUnlit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define ECHO_MAX_WAVES 16

            // ---- 由 EchoWaveManager 每帧写入的全局数据（非材质属性）----
            float4 _EchoOrigins[ECHO_MAX_WAVES];
            float4 _EchoData[ECHO_MAX_WAVES];
            float4 _EchoColors[ECHO_MAX_WAVES];
            int    _EchoCount;

            // ---- 抓取/落地高亮（全局，由 DevPlayerGrab 写入）----
            float4 _HighlightPos;       // xyz=中心 w=包围球半径
            float  _HighlightIntensity; // 0..1

            // ---- 材质属性（SRP Batcher 兼容）----
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _RingWidth;
                float  _TrailGap;
                float  _TrailFade;
                float  _WobbleAmount;
                float  _DashAmount;
                float  _RingIntensity;
                float  _InteriorIntensity;
                float  _InteriorFalloff;
                float  _FresnelPower;
                float  _FresnelIntensity;
                float  _OutlineWidth;
                float  _OutlineDepthThreshold;
                float  _OutlineIntensity;
                float4 _OutlineColor;
                float  _HullWidth;
                float4 _HullColor;
                float  _HullDelay;
                float  _HullRamp;
                float  _HullIntensity;
                float  _MinVisible;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 screenUV   : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO // VR 双目：传递 eye index（SPI 下深度采样依赖它）
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionWS = vpi.positionWS;
                OUT.positionCS = vpi.positionCS;
                OUT.screenUV   = vpi.positionNDC; // 用于采样屏幕深度图
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            // 简易哈希噪声（手绘感来源）
            float Hash11(float x) { return frac(sin(x) * 43758.5453); }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 posWS = IN.positionWS;
                float3 nrmWS = normalize(IN.normalWS);

                float  bestCrest  = 0; float3 ringCol = 0;   // 最强波环 + 它的颜色
                float  bestInside = 0; float3 insideCol = 0; // 最强波内照明 + 它的颜色

                int count = min(_EchoCount, ECHO_MAX_WAVES);
                [loop]
                for (int i = 0; i < count; i++)
                {
                    float4 org = _EchoOrigins[i];
                    float4 dat = _EchoData[i];
                    float3 waveCol = _EchoColors[i].rgb;

                    float radius   = dat.x;
                    float maxR     = max(dat.y, 0.001);
                    float strength = org.w;

                    // 消散曲线：出生快速淡入，尾段 40% 平滑淡出（死亡时亮度已为 0）
                    float t    = saturate(radius / maxR);
                    float fade = smoothstep(0.0, 0.06, t) * (1.0 - smoothstep(0.6, 1.0, t));
                    if (fade * strength < 0.001) continue;

                    // 球面波：从三维波心向外扩散，地面/墙面/道具上都是同心圆
                    float3 off = posWS - org.xyz;
                    float  d   = length(off);
                    float3 dir = off / max(d, 1e-4);

                    // 手绘感：随球面方向变化的线宽抖动 + 分段断线
                    float ang = atan2(dir.z, dir.x) + dir.y * 2.0;
                    float h1 = Hash11(ang * 3.0 + org.x * 13.7);
                    float h2 = Hash11(ang * 7.0 - org.z * 5.3);
                    float wobble = 1.0 - _WobbleAmount * 0.6 * h1;
                    float dashVal = frac(ang * 0.797 + h2 * 0.3); // 约 5 段
                    float dashMask = smoothstep(_DashAmount * 0.45, _DashAmount * 0.45 + 0.08, dashVal);

                    // 主环：细锐线条
                    float w = max(_RingWidth * wobble, 0.02);
                    float crest = 1.0 - saturate(abs(d - radius) / w);
                    crest *= crest;
                    crest *= fade * strength * dashMask;

                    // 拖尾环 ×2：主环内侧渐宽渐淡的晕开层
                    float r2 = radius - _TrailGap;
                    float crest2 = r2 > 0.0 ? 1.0 - saturate(abs(d - r2) / (w * 2.5)) : 0.0;
                    crest2 *= crest2 * _TrailFade * fade * strength;

                    float r3 = radius - _TrailGap * 2.0;
                    float crest3 = r3 > 0.0 ? 1.0 - saturate(abs(d - r3) / (w * 4.0)) : 0.0;
                    crest3 *= crest3 * _TrailFade * _TrailFade * fade * strength;

                    // 波内照明：波外严格为 0；前沿向内从峰值衰减到 0.3 底值
                    float insideMask = saturate((radius - d) / _InteriorFalloff); // 波外 = 0
                    float inside = insideMask * lerp(1.0, 0.3, insideMask) * fade * strength;

                    // 取每像素最强的波，保持颜色干净不混色
                    float c = crest + crest2 + crest3;
                    if (c > bestCrest)  { bestCrest = c; ringCol = waveCol; }
                    if (inside > bestInside) { bestInside = inside; insideCol = waveCol; }
                }

                // 菲涅尔：退化为白色结构光（不染波色，避免与波纹浑浊）
                float3 viewDir = normalize(GetWorldSpaceViewDir(posWS));
                float  fres    = pow(1.0 - saturate(dot(nrmWS, viewDir)), _FresnelPower);

                // 屏幕空间描边：比较邻域深度差检测轮廓（门/墙角/把手靠深度差分离）
                // 锐利做法：窄过渡带 + 固定描边色（独立于波色，不混色）
                float2 uv    = IN.screenUV.xy / IN.screenUV.w;
                // 用 uv 的屏幕导数求纹素尺寸（不依赖额外全局 uniform）
                float2 texel = abs(float2(ddx(uv).x, ddy(uv).y)) * _OutlineWidth;
                float d0 = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float d1 = LinearEyeDepth(SampleSceneDepth(uv + texel * float2( 1, 0)), _ZBufferParams);
                float d2 = LinearEyeDepth(SampleSceneDepth(uv + texel * float2(-1, 0)), _ZBufferParams);
                float d3 = LinearEyeDepth(SampleSceneDepth(uv + texel * float2( 0, 1)), _ZBufferParams);
                float d4 = LinearEyeDepth(SampleSceneDepth(uv + texel * float2( 0,-1)), _ZBufferParams);
                float maxDiff = max(max(abs(d1 - d0), abs(d2 - d0)), max(abs(d3 - d0), abs(d4 - d0)));
                float edge = smoothstep(_OutlineDepthThreshold, _OutlineDepthThreshold * 1.15, maxDiff);

                float waveLight = saturate(bestCrest + bestInside);
                float3 outlineCol = _OutlineColor.rgb;

                // 抓取/落地高亮：高亮球内的物体无视波照明，获得描边色高亮
                float hlDist = distance(posWS, _HighlightPos.xyz);
                float hl = saturate((_HighlightPos.w - hlDist) / max(_HighlightPos.w * 0.2, 1e-3)) * _HighlightIntensity;

                // 合成：黑底 + 波内照明(压暗当背景) + 波环(主+拖尾) + 白色结构光 + 固定色锐利描边 + 高亮
                float vis = _MinVisible + bestInside * _InteriorIntensity * 0.7 + bestCrest * _RingIntensity;
                float3 col = _BaseColor.rgb * vis
                           + ringCol * (bestCrest * _RingIntensity)
                           + insideCol * (bestInside * _InteriorIntensity * 0.45)
                           + outlineCol * (fres * waveLight * _FresnelIntensity * 0.28)
                           + outlineCol * (edge * waveLight * _OutlineIntensity)
                           + outlineCol * (edge * hl * _OutlineIntensity)
                           + outlineCol * (fres * hl * 0.5);

                return half4(col, 1);
            }
            ENDHLSL
        }

        // ============ Pass 2：物体轮廓壳（inverted hull）============
        // 像场景视图里 Collider 线框那样勾住整个物体的外形。
        // 时序：波前沿扫过该物体 _HullDelay 米后轮廓浮现（_HullRamp 内渐显），
        //       随波自身的尾段消散而淡出，波死亡 = 完全透明（真 alpha 混合，透出背景波纹）。
        Pass
        {
            Name "EchoHullOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front          // 只画外壳背面 → 露出的是物体外圈的轮廓线
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define ECHO_MAX_WAVES 16

            float4 _EchoOrigins[ECHO_MAX_WAVES];
            float4 _EchoData[ECHO_MAX_WAVES];
            float4 _EchoColors[ECHO_MAX_WAVES];
            int    _EchoCount;

            float4 _HighlightPos;
            float  _HighlightIntensity;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _RingWidth;
                float  _TrailGap;
                float  _TrailFade;
                float  _WobbleAmount;
                float  _DashAmount;
                float  _RingIntensity;
                float  _InteriorIntensity;
                float  _InteriorFalloff;
                float  _FresnelPower;
                float  _FresnelIntensity;
                float  _OutlineWidth;
                float  _OutlineDepthThreshold;
                float  _OutlineIntensity;
                float4 _OutlineColor;
                float  _HullWidth;
                float4 _HullColor;
                float  _HullDelay;
                float  _HullRamp;
                float  _HullIntensity;
                float  _MinVisible;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO // VR 双目：传递 eye index
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                // 球形法线外扩：对以自身原点为中心的白盒（门/柱/箱）外扩方向连续，
                // 硬边处不像逐顶点法线那样开裂
                float3 dirOS = IN.positionOS.xyz;
                float  len   = max(length(dirOS), 1e-4);
                float3 posOS = IN.positionOS.xyz + (dirOS / len) * _HullWidth;
                OUT.positionWS = TransformObjectToWorld(posOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 轮廓浮现强度：取"扫过最久"的波
                float reveal = 0;
                int count = min(_EchoCount, ECHO_MAX_WAVES);
                [loop]
                for (int i = 0; i < count; i++)
                {
                    float4 org = _EchoOrigins[i];
                    float4 dat = _EchoData[i];
                    float radius   = dat.x;
                    float maxR     = max(dat.y, 0.001);
                    float strength = org.w;

                    float t    = saturate(radius / maxR);
                    float fade = smoothstep(0.0, 0.06, t) * (1.0 - smoothstep(0.6, 1.0, t));
                    if (fade * strength < 0.001) continue;

                    float d          = distance(IN.positionWS, org.xyz);
                    float sinceSweep = radius - d; // 波前沿越过该点后的距离（负 = 还没扫到）
                    float r = smoothstep(_HullDelay, _HullDelay + _HullRamp, sinceSweep) * fade * strength;
                    reveal = max(reveal, r);
                }

                // 抓取/落地高亮也点亮轮廓（无视波）
                float hlDist = distance(IN.positionWS, _HighlightPos.xyz);
                float hl = saturate((_HighlightPos.w - hlDist) / max(_HighlightPos.w * 0.2, 1e-3)) * _HighlightIntensity;

                float a = saturate(max(reveal * _HullIntensity, hl));
                if (a < 0.004) discard; // 完全淡出时不产生 overdraw
                return half4(_HullColor.rgb * a, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
