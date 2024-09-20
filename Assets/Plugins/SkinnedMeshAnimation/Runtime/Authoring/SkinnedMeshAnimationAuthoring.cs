using Unity.Collections;
using Unity.Entities;
using UnityEngine;

// ReSharper disable once CheckNamespace
public class SkinnedMeshAnimationAuthoring : MonoBehaviour
{
    /// <summary>
    /// 默认动画名称
    /// </summary>
    public string defaultAnimation;
    /// <summary>
    /// 默认动画的播放层级
    /// </summary>
    public int defaultAnimationLayer = 1;
}

internal class SkinnedMeshAnimationBaker : Baker<SkinnedMeshAnimationAuthoring>
{
    public override void Bake(SkinnedMeshAnimationAuthoring authoring)
    {
        var skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
        if (skinnedMeshRenderer == null)
        {
            return;
        }

        DependsOn(skinnedMeshRenderer.sharedMesh);

        bool hasSkinning = skinnedMeshRenderer.bones.Length > 0 && skinnedMeshRenderer.sharedMesh.bindposes.Length > 0;
        if (hasSkinning)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            // 接收动画请求，决定动画系统播放指定动画，以及如何播放
            var requestBuffer = AddBuffer<AnimationRequest>(entity);
            if (!string.IsNullOrEmpty(authoring.defaultAnimation) && authoring.defaultAnimationLayer > 0)
            {
                // 如果Prefab上配置了默认动画，则播放默认动画
                requestBuffer.Add(new AnimationRequest() { animationName = authoring.defaultAnimation, fadeoutTime = 0.0f, speed = 1.0f, layer = authoring.defaultAnimationLayer });
            }
            
            // 添加BoneBakedTag组件，表明该SkinnedMesh已经烘焙完成，可以交给ComputeSkinMatricesBakingSystem去初始化了
            AddComponent(entity, new BoneBakedTag());

            // 获得根骨骼的引用
            Transform rootTransform = skinnedMeshRenderer.rootBone ? skinnedMeshRenderer.rootBone : skinnedMeshRenderer.transform;
            Entity rootEntity = GetEntity(rootTransform, TransformUsageFlags.Dynamic);
            AddComponent(entity, new RootEntity { value = rootEntity });

            // 获得所有骨骼的引用
            DynamicBuffer<BoneEntity> boneEntities = AddBuffer<BoneEntity>(entity);
            boneEntities.ResizeUninitialized(skinnedMeshRenderer.bones.Length);
            for (int boneIndex = 0; boneIndex < skinnedMeshRenderer.bones.Length; ++boneIndex)
            {
                var bone = skinnedMeshRenderer.bones[boneIndex];
                // 为每根骨骼创建Entity
                var boneEntity = GetEntity(bone, TransformUsageFlags.Dynamic);
                boneEntities[boneIndex] = new BoneEntity { entity = boneEntity };
            }

            // 获得每一根骨骼的BindPose逆矩阵
            DynamicBuffer<BindPose> bindPoseArray = AddBuffer<BindPose>(entity);
            bindPoseArray.ResizeUninitialized(skinnedMeshRenderer.bones.Length);
            for (int boneIndex = 0; boneIndex != skinnedMeshRenderer.bones.Length; ++boneIndex)
            {
                Matrix4x4 bindPose = skinnedMeshRenderer.sharedMesh.bindposes[boneIndex];
                bindPoseArray[boneIndex] = new BindPose { value = bindPose };
            }
        }
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
public partial class ComputeSkinMatricesBakingSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var ecb = new EntityCommandBuffer(Allocator.TempJob);

        foreach (var (rootEntityRef, bonesBuffer, entity) in SystemAPI.Query<RefRO<RootEntity>, DynamicBuffer<BoneEntity>>()
                     .WithEntityAccess().WithAll<BoneBakedTag>().WithOptions(EntityQueryOptions.IncludeDisabledEntities))
        {
            RootEntity rootEntity = rootEntityRef.ValueRO;
            
            // 在骨骼的Entity上绑RootTag，标记这个Entity是根骨骼
            ecb.AddComponent<RootTag>(rootEntity.value);

            // 给所有骨骼加上一个Tag，以便当计算SkinMatrices的时候可以获取到
            for (int boneIndex = 0; boneIndex < bonesBuffer.Length; ++boneIndex)
            {
                // 获取所有骨骼的Entity
                var boneEntity = bonesBuffer[boneIndex].entity;
                    
                // 调试用，这个组件可有可无
                ecb.AddComponent(boneEntity, new BoneIndex { value = boneIndex });
                // 在骨骼的Entity上绑BoneTag，标记这个Entity是非根骨骼
                ecb.AddComponent(boneEntity, new BoneTag());
                // 在骨骼的Entity上绑SkinnedMeshAnimationController，用来控制骨骼随着动画帧运动
                ecb.AddComponent(boneEntity, new SkinnedMeshAnimationController() { enable = false });
                    
                // 骨骼当前受影响的动画曲线（AnimationCurve）
                ecb.AddBuffer<SkinnedMeshAnimationCurve>(boneEntity);
            }
            // ecb.SetName(rootEntity.value, "RootBone");
            // ecb.SetName(entity, "SkinnedMesh");
        }
        
        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}