using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Profiling;

[BurstCompile]
internal partial struct SkinnedMeshBoneSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    private struct FrameInfo
    {
        public int layer;
        public float startTime;
        public float duration;
        public float4 value;
        public float fadeoutTime;
    }

    [BurstCompile, WithAll(typeof(LocalTransform), typeof(SkinnedMeshAnimationController), typeof(SkinnedMeshAnimationCurve), typeof(BoneTag))]
    internal partial struct BoneAnimationJob : IJobEntity
    {
        public float time;

        private struct FrameInfoSortByLayer : IComparer<FrameInfo>
        {
            int IComparer<FrameInfo>.Compare(FrameInfo x, FrameInfo y)
            {
                return x.layer.CompareTo(y.layer);
            }
        }

        public void Execute(ref LocalTransform localTransform, ref DynamicBuffer<SkinnedMeshAnimationCurve> curves, ref SkinnedMeshAnimationController controller)
        {
            if (!controller.enable)
            {
                return;
            }

            // 用于做层与层之间的CrossFade
            //NativeParallelMultiHashMap<byte, FrameInfo> frameInfos = new NativeParallelMultiHashMap<byte, FrameInfo>((int)KeyframePropertyType.Count, Allocator.Temp);

            byte lastProperty = 255;
            float4 layerValue = 0.0f;
            for (int i = curves.Length - 1; i >= 0; --i)
            {
                SkinnedMeshAnimationCurve curve = curves[i];

                //Profiler.BeginSample("RemoveLayer");
                if (curve.wrapType == AnimationWrapType.Once)
                {
                    float t = time - curve.startTime;
                    t *= curve.speed;
                    if (t >= curve.duration)
                    {
                        curves.RemoveAt(i);

                        UpdateControllerLowerLayer(ref controller, curve.layer, ref curves, time);
                        continue;
                    }
                }
                //Profiler.EndSample();

                //Profiler.BeginSample("Interpolate");
                ref AnimationCurve animCurve = ref curve.curveRef.Value;

                float span = GetSpan(time, curve.startTime, curve.speed, curve.duration, curve.wrapType);
                float4 value = Interpolate(ref animCurve.keyframes, span);
                //Profiler.EndSample();

                //Profiler.BeginSample("Crossfade");
                // 同一层前后两个动画之间的CrossFade
                if (curve.nextCurve.fadeOutTime >= 0.0f)
                {
                    float blendTime = time - curve.nextCurve.startTime;

                    // 要融合的动画曲线
                    ref AnimationCurve nextAnimCurve = ref curve.nextCurve.curveRef.Value;

                    // 计算融合插值因子
                    float nextSpan = GetSpan(time, curve.nextCurve.startTime, curve.nextCurve.speed, curve.nextCurve.duration, curve.nextCurve.wrapType);
                    float4 nextValue = Interpolate(ref nextAnimCurve.keyframes, nextSpan);
                    float t = blendTime / curve.nextCurve.fadeOutTime;

                    // 线性插值进行动画融合
                    value = t * nextValue + (1 - t) * value;

                    // 如果超过了Crossfade的时间
                    if (blendTime >= curve.nextCurve.fadeOutTime)
                    {
                        curve.startTime = curve.nextCurve.startTime;
                        curve.duration = curve.nextCurve.duration;
                        curve.wrapType = curve.nextCurve.wrapType;
                        curve.curveRef = curve.nextCurve.curveRef;
                        curve.speed = curve.nextCurve.speed;

                        curve.layerFadeout.startTime = curve.nextCurve.startTime;
                        curve.layerFadeout.duration = curve.nextCurve.duration;
                        curve.layerFadeout.fadeoutTime = curve.nextCurve.fadeOutTime;
                        curve.nextCurve.fadeOutTime = -999.0f;
                    }
                }
                curves[i] = curve;
                //Profiler.EndSample();

                //Profiler.BeginSample("NewFrameInfo");
                FrameInfo frameInfo = new FrameInfo()
                {
                    layer = curve.layer,
                    startTime = curve.layerFadeout.startTime,
                    duration = curve.layerFadeout.duration,
                    fadeoutTime = curve.layerFadeout.fadeoutTime,
                    value = value,
                };
                //Profiler.EndSample();

                //Profiler.BeginSample("LayerBlend");
                byte propertyType = (byte)animCurve.propertyType;
                //TransformLocal(ref localTransform, animCurve.propertyType, value);
                //frameInfos.Add(propertyType, frameInfo);

                if (lastProperty == propertyType)
                {
                    // 动画开始时间
                    float elapsedTime = time - frameInfo.startTime;
                    // 动画剩余时间
                    float remainTime = frameInfo.duration - elapsedTime;

                    if (remainTime <= frameInfo.fadeoutTime)
                    {
                        float t = math.clamp(remainTime / frameInfo.fadeoutTime, 0.0f, 1.0f);
                        layerValue = t * frameInfo.value + (1 - t) * layerValue;
                    }
                    else if (elapsedTime <= frameInfo.fadeoutTime)
                    {
                        float t = math.clamp(elapsedTime / frameInfo.fadeoutTime, 0.0f, 1.0f);
                        layerValue = t * frameInfo.value + (1 - t) * layerValue;
                    }
                    else
                    {
                        layerValue = frameInfo.value;
                    }
                }
                else
                {
                    layerValue = frameInfo.value;
                }
                lastProperty = propertyType;
                //Profiler.EndSample();

                //Profiler.BeginSample("Apply");
                bool hasNext = (i - 1) >= 0;
                bool nextPropertyDiff = false;
                if (hasNext)
                {
                    byte nextProtpertyType = (byte)curves[i - 1].curveRef.Value.propertyType;
                    nextPropertyDiff = nextProtpertyType != propertyType;
                }
                if (!hasNext || nextPropertyDiff)
                {
                    TransformLocal(ref localTransform, animCurve.propertyType, layerValue);
                }
                //Profiler.EndSample();
            }
        }

        private static void UpdateControllerLowerLayer(ref SkinnedMeshAnimationController controller, int currentLayer, ref DynamicBuffer<SkinnedMeshAnimationCurve> curves, float time)
        {
            if (controller.currentLayer != currentLayer)
            {
                return;
            }

            int maxLayer = int.MinValue;
            for (int cIndex = 0; cIndex < curves.Length; ++cIndex)
            {
                SkinnedMeshAnimationCurve c = curves[cIndex];
                if (c.layer < currentLayer)
                {
                    //c.startTime = time;
                    maxLayer = math.max(maxLayer, c.layer);
                    controller.currentLayer = maxLayer;
                    //curves[cIndex] = c;
                }
            }
        }

        private static float GetSpan(float time, float startTime, float speed, float duration, AnimationWrapType wrapType)
        {
            float span = time - startTime;
            span *= speed;
            if (wrapType == AnimationWrapType.Loop)
            {
                span = math.fmod(span, duration);
            }
            else if (wrapType == AnimationWrapType.Clamp)
            {
                span = math.min(span, duration);
            }
            return span;
        }

        private static float4 Interpolate(ref BlobArray<Keyframe> keyframes, float span)
        {
            float4 value = 0.0f;
            int kfCount = keyframes.Length;
            for (int k = 0; k < kfCount; ++k)
            {
                ref Keyframe kfCurrent = ref keyframes[k];
                if (k == 0)
                {
                    if (span < kfCurrent.time)
                    {
                        value = kfCurrent.value;
                        break;
                    }
                }
                if (k < kfCount - 1)
                {
                    ref Keyframe kfNext = ref keyframes[k + 1];
                    if (span >= kfCurrent.time && span < kfNext.time)
                    {
                        value = HermiteInterpolate(span, ref kfCurrent, ref kfNext);
                        break;
                    }
                }
                else
                {
                    value = kfCurrent.value;
                    break;
                }
            }
            return value;
        }

        private static void TransformLocal(ref LocalTransform transform, KeyframePropertyType propertyType, float4 value)
        {
            if (propertyType == KeyframePropertyType.PositionAndScale)
            {
                transform.Position = value.xyz;
                transform.Scale = 1.0f;
            }
            else if (propertyType == KeyframePropertyType.Rotation)
            {
                transform.Rotation = value.xyzw;
            }
        }

        private static float4 HermiteInterpolate(float time, ref Keyframe lhs, ref Keyframe rhs)
        {
            float dx = rhs.time - lhs.time;
            float4 m0;
            float4 m1;
            float t;
            if (dx != 0.0f)
            {
                t = (time - lhs.time) / dx;
                m0 = lhs.outTangent * dx;
                m1 = rhs.inTangent * dx;
            }
            else
            {
                t = 0.0f;
                m0 = 0;
                m1 = 0;
            }
            return HermiteInterpolate(t, lhs.value, m0, m1, rhs.value);
        }

        private static float4 HermiteInterpolate(float t, float4 p0, float4 m0, float4 m1, float4 p1)
        {
            // (2 * t^3 -3 * t^2 +1) * p0 + (t^3 - 2 * t^2 + t) * m0 + (-2 * t^3 + 3 * t^2) * p1 + (t^3 - t^2) * m1

            var a = 2.0f * p0 + m0 - 2.0f * p1 + m1;
            var b = -3.0f * p0 - 2.0f * m0 + 3.0f * p1 - m1;
            var c = m0;
            var d = p0;

            return t * (t * (a * t + b) + c) + d;
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float time = (float)SystemAPI.Time.ElapsedTime;

        BoneAnimationJob boneAnimationJob = new BoneAnimationJob()
        {
            time = time,
        };
        state.Dependency = boneAnimationJob.ScheduleParallel(state.Dependency);
    }
}