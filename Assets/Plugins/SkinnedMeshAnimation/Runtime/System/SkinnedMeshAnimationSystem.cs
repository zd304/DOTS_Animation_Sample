using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;
using System.Security.Cryptography;

public partial class SkinnedMeshAnimationSystem : SystemBase
{
    private Dictionary<string, AnimationCurveCache> animationCurveCache = new Dictionary<string, AnimationCurveCache>();
    private BufferLookup<SkinnedMeshAnimationCurve> curveLookup;
    private ComponentLookup<SkinnedMeshAnimationController> curveCtrlLookup;
    
    /// <summary>
    /// 对应动画片段的缓存数据
    /// </summary>
    internal class AnimationCurveCache
    {
        /// <summary>
        /// 动画片段长度
        /// </summary>
        public float length;
        /// <summary>
        /// 动画包装类型
        /// </summary>
        public AnimationWrapType wrapType;
        
        /// <summary>
        /// Entities可以使用的位置和缩放曲线
        /// </summary>
        public SkinnedMeshAnimationCurve[] posAndSclCurves;
        /// <summary>
        /// Entities可以使用的旋转曲线
        /// </summary>
        public SkinnedMeshAnimationCurve[] rotCurves;
    }

    protected override void OnCreate()
    {
        curveLookup = GetBufferLookup<SkinnedMeshAnimationCurve>();
        curveCtrlLookup = GetComponentLookup<SkinnedMeshAnimationController>();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        foreach (var (_, cache) in animationCurveCache)
        {
            for (int i = 0; i < cache.posAndSclCurves.Length; ++i)
            {
                cache.posAndSclCurves[i].curveRef.Dispose();
            }
            for (int i = 0; i < cache.rotCurves.Length; ++i)
            {
                cache.rotCurves[i].curveRef.Dispose();
            }
        }
    }

    protected override void OnUpdate()
    {
        //var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        //EntityCommandBuffer ecb = ecbSingleton.CreateCommandBuffer(CheckedStateRef.WorldUnmanaged);

        curveLookup.Update(this);
        curveCtrlLookup.Update(this);

        float time = (float)SystemAPI.Time.ElapsedTime;

        Entities
            .WithName("RespondAnimationRequest")
            .WithoutBurst()
            .ForEach((Entity entity, ref DynamicBuffer<AnimationRequest> animationRequests, ref DynamicBuffer<BoneEntity> bonesBuffer) =>
            {
                if (animationRequests.IsEmpty)
                {
                    return;
                }

                var currentReq = animationRequests[animationRequests.Length - 1];

                // 请求的动画名称
                string animationName = currentReq.animationName.ToString();

                if (string.IsNullOrEmpty(animationName))
                {
                    animationRequests.Clear();
                    return;
                }

                // 查询该动画曲线资源是否已经被加载过
                if (!animationCurveCache.TryGetValue(animationName, out AnimationCurveCache animCache))
                {
                    // 通过SkinnedMeshAnimationClip类型的Asset来初始化动画曲线组件
                    SkinnedMeshAnimationClip clip = Resources.Load<SkinnedMeshAnimationClip>(animationName);

                    if (clip == null)
                    {
                        Debug.LogError($"Loading {animationName} failed!");
                        return;
                    }

                    animCache = new AnimationCurveCache()
                    {
                        length = clip.length,
                        wrapType = clip.wrapType,

                        posAndSclCurves = new SkinnedMeshAnimationCurve[clip.posAndSclCurves.Length],
                        rotCurves = new SkinnedMeshAnimationCurve[clip.rotCurves.Length],
                    };

                    // 加载（初始化）动画曲线资源
                    InitAnimationCache(animCache, clip, clip.posAndSclCurves.Length);

                    animationCurveCache.Add(animationName, animCache);
                }

                bool hasDynamicBuffer = false;

                SkinnedMeshBoneMask maskAsset = null;
                if (!currentReq.maskPath.IsEmpty)
                {
                    maskAsset = Resources.Load<SkinnedMeshBoneMask>(currentReq.maskPath.ToString());
                }

                for (int i = 0; i < bonesBuffer.Length; ++i)
                {
                    BoneEntity bone = bonesBuffer[i];
                    
                    // Avatar Mask包含此骨骼才允许更新此骨骼的动画
                    if (maskAsset != null && !maskAsset.mask.Contains(i))
                    {
                        continue;
                    }

                    if (!curveCtrlLookup.TryGetComponent(bone.entity, out SkinnedMeshAnimationController controller))
                    {
                        continue;
                    }
                    // 保证动画控制器开启
                    controller.enable = true;
                    // 动画控制器当前正在播放的动画layer：选最大的layer进行播放
                    controller.currentLayer = math.max(controller.currentLayer, currentReq.layer);
                    curveCtrlLookup[bone.entity] = controller;
                    
                    // 获取当前骨骼上所有正在播放的动画曲线的数组
                    if (!curveLookup.TryGetBuffer(bone.entity, out DynamicBuffer<SkinnedMeshAnimationCurve> curveBuffer))
                    {
                        continue;
                    }
                    hasDynamicBuffer = true;

                    // 如果对应layer的动画曲线已经存在于骨骼上，则新的动画曲线添加到已经存在的曲线的nextCurve上
                    bool posXExist = false;
                    bool rotXExist = false;
                    for (int cIndex = 0; cIndex < curveBuffer.Length; ++cIndex)
                    {
                        SkinnedMeshAnimationCurve curve = curveBuffer[cIndex];
                        // 新动画是否和已经在播放的动画在同一个layer
                        if (currentReq.layer != curve.layer)
                        {
                            continue;
                        }
                        KeyframePropertyType propertyType = curve.curveRef.Value.propertyType;
                        
                        // 新动画曲线类型是否和已经在播放的动画曲线类型相同
                        
                        switch (propertyType)
                        {
                            case KeyframePropertyType.PositionAndScale:
                                // 如果都是PositionAndScale类型的曲线，则将新曲线设置给nextCurve，准备Cross Fade
                                SetNextCurve(ref curveBuffer, cIndex, in animCache.posAndSclCurves[i], currentReq.fadeoutTime, time, animCache.length, currentReq.speed, animCache.wrapType);
                                posXExist = true;
                                break;
                            case KeyframePropertyType.Rotation:
                                // 如果都是Rotation类型的曲线，则将新曲线设置给nextCurve，准备Cross Fade
                                SetNextCurve(ref curveBuffer, cIndex, in animCache.rotCurves[i], currentReq.fadeoutTime, time, animCache.length, currentReq.speed, animCache.wrapType);
                                rotXExist = true;
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                    
                    // 如果新动画曲线和已经在播放的动画曲线不在同一个layer，也不是已存在的曲线类型，则新增动画曲线到骨骼的DynamicBuffer<SkinnedMeshAnimationCurve>
                    
                    if (!posXExist)
                    {
                        AppendNewCurve(ref curveBuffer, in animCache.posAndSclCurves[i], currentReq.layer, time, animCache.length, currentReq.speed, currentReq.fadeoutTime, animCache.wrapType);
                    }
                    if (!rotXExist)
                    {
                        AppendNewCurve(ref curveBuffer, in animCache.rotCurves[i], currentReq.layer, time, animCache.length, currentReq.speed, currentReq.fadeoutTime, animCache.wrapType);
                    }
                    
                    // 排序所有曲线：曲线类型相同的曲线优先归到数组相邻位置，其次再按layer升序进行排列
                    for (int j = 1; j < curveBuffer.Length; ++j)
                    {
                        for (int k = 0; k < (curveBuffer.Length - j); ++k)
                        {
                            SkinnedMeshAnimationCurve curve1 = curveBuffer[k];
                            int weight1 = (int)curve1.curveRef.Value.propertyType * 1000 + curve1.layer;

                            SkinnedMeshAnimationCurve curve2 = curveBuffer[k + 1];
                            int weight2 = (int)curve2.curveRef.Value.propertyType * 1000 + curve2.layer;

                            if (weight1 < weight2)
                            {
                                curveBuffer[k] = curve2;
                                curveBuffer[k + 1] = curve1;
                            }
                        }
                    }
                }
                
                // 清空动画播放请求
                if (hasDynamicBuffer)
                {
                    animationRequests.Clear();
                }
            }).Run();
    }

    private static void AppendNewCurve(ref DynamicBuffer<SkinnedMeshAnimationCurve> curveBuffer, in SkinnedMeshAnimationCurve cacheCurve, int layer,  float startTime, float duration, float speed, float fadeTime, AnimationWrapType wrapType)
    {
        SkinnedMeshAnimationCurve curve = new SkinnedMeshAnimationCurve()
        {
            layer = layer,
            startTime = startTime,
            speed = speed,
            duration = duration,
            wrapType = wrapType,
            curveRef = cacheCurve.curveRef,
            layerFadeout = new SkinnedMeshLayerFadeout()
            {
                startTime = startTime,
                duration = duration,
                fadeoutTime = fadeTime
            },
            nextCurve = new SkinnedMeshAninationFadeout()
            {
                fadeOutTime = -999.0f
            }
        };
        curveBuffer.Add(curve);
    }

    private static void SetNextCurve(ref DynamicBuffer<SkinnedMeshAnimationCurve> curveBuffer, int index, in SkinnedMeshAnimationCurve cacheCurve, float fadeoutTime, float startTime, float duration, float speed, AnimationWrapType wrapType)
    {
        SkinnedMeshAnimationCurve curve = curveBuffer[index];
        curve.nextCurve.speed = speed;
        curve.nextCurve.fadeOutTime = fadeoutTime;
        curve.nextCurve.startTime = startTime;
        curve.nextCurve.duration = duration;
        curve.nextCurve.wrapType = wrapType;
        curve.nextCurve.curveRef = cacheCurve.curveRef;

        curve.layerFadeout.startTime = startTime;
        curve.layerFadeout.duration = duration;
        curve.layerFadeout.fadeoutTime = fadeoutTime;

        curveBuffer[index] = curve;
    }

    private static void AppendCurve(in EntityCommandBuffer ecb, Entity entity, SkinnedMeshAnimationCurve[] curves, int index)
    {
        if (curves.Length <= index)
        {
            return;
        }
        ecb.AppendToBuffer(entity, curves[index]);
    }

    private static void InitAnimationCache(AnimationCurveCache cache, SkinnedMeshAnimationClip clip, int boneCount)
    {
        for (int i = 0; i < boneCount; ++i)
        {
            cache.posAndSclCurves[i] = BakedAnimationClipToBlob(clip.posAndSclCurves[i], KeyframePropertyType.PositionAndScale);
            cache.rotCurves[i] = BakedAnimationClipToBlob(clip.rotCurves[i], KeyframePropertyType.Rotation);
        }
    }

    private static SkinnedMeshAnimationCurve BakedAnimationClipToBlob(BakedCurve curve, KeyframePropertyType propertyType)
    {
        var builder = new BlobBuilder(Allocator.Temp);

        ref AnimationCurve animCurve = ref builder.ConstructRoot<AnimationCurve>();
        var kfBuilderArray = builder.Allocate(ref animCurve.keyframes, curve.keyframes.Count);
        for (int kfIndex = 0; kfIndex < curve.keyframes.Count; ++kfIndex)
        {
            BakedKeyframe keyframe = curve.keyframes[kfIndex];
            kfBuilderArray[kfIndex] = new Keyframe()
            {
                time = keyframe.time,
                value = keyframe.value,
                inTangent = keyframe.inTangent,
                outTangent = keyframe.outTangent,
            };
        }
        animCurve.boneIndex = curve.boneIndex;
        animCurve.propertyType = propertyType;

        SkinnedMeshAnimationCurve rst = new SkinnedMeshAnimationCurve();
        rst.curveRef = builder.CreateBlobAssetReference<AnimationCurve>(Allocator.Persistent);

        builder.Dispose();

        return rst;
    }
}