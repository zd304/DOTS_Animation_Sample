using Unity.Collections;
using Unity.Deformations;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateBefore(typeof(DeformationsInPresentation))]
// ReSharper disable once CheckNamespace
partial class CalculateSkinMatrixSystemBase : SystemBase
{
    EntityQuery m_BoneEntityQuery;
    EntityQuery m_RootEntityQuery;

    protected override void OnCreate()
    {
        // 查询所有非根骨骼
        m_BoneEntityQuery = GetEntityQuery(
                ComponentType.ReadOnly<LocalToWorld>(),
                ComponentType.ReadOnly<BoneTag>()
            );
        
        // 查询所有根骨骼
        m_RootEntityQuery = GetEntityQuery(
                ComponentType.ReadOnly<LocalToWorld>(),
                ComponentType.ReadOnly<RootTag>()
            );
    }

    protected override void OnUpdate()
    {
        var dependency = Dependency;
        
        // 收集所有非根骨骼的变换矩阵
        var boneCount = m_BoneEntityQuery.CalculateEntityCount();
        var bonesLocalToWorld = new NativeParallelHashMap<Entity, float4x4>(boneCount, Allocator.TempJob);
        var bonesLocalToWorldParallel = bonesLocalToWorld.AsParallelWriter();
        var bone = Entities
            .WithName("GatherBoneTransforms")
            .WithAll<BoneTag>()
            .ForEach((Entity entity, in LocalToWorld localToWorld) =>
            {
                bonesLocalToWorldParallel.TryAdd(entity, localToWorld.Value);
            }).ScheduleParallel(dependency);
        
        // 收集所有根骨骼的变换矩阵
        var rootCount = m_RootEntityQuery.CalculateEntityCount();
        var rootWorldToLocal = new NativeParallelHashMap<Entity, float4x4>(rootCount, Allocator.TempJob);
        var rootWorldToLocalParallel = rootWorldToLocal.AsParallelWriter();
        var root = Entities
            .WithName("GatherRootTransforms")
            .WithAll<RootTag>()
            .ForEach((Entity entity, in LocalToWorld localToWorld) =>
            {
                rootWorldToLocalParallel.TryAdd(entity, math.inverse(localToWorld.Value));
            }).ScheduleParallel(dependency);
        
        // 以上两个Job执行完成才能执行下面的Job
        dependency = JobHandle.CombineDependencies(bone, root);
        
        // 计算SkinMatrix
        dependency = Entities
            .WithName("CalculateSkinMatrices")
            .WithReadOnly(bonesLocalToWorld)
            .WithReadOnly(rootWorldToLocal)
            .WithBurst()
            .ForEach((ref DynamicBuffer<SkinMatrix> skinMatrices, in DynamicBuffer<BindPose> bindPoses, in DynamicBuffer<BoneEntity> bones, in RootEntity rootEtt) =>
            {
                // 循环遍历每一根骨骼
                for (int i = 0; i < skinMatrices.Length; ++i)
                {
                    // 非根骨骼
                    var boneEntity = bones[i].entity;
                    // 根骨骼Entity
                    var rootEntity = rootEtt.value;

                    // #TODO: this is necessary for LiveLink?
                    if (!bonesLocalToWorld.ContainsKey(boneEntity) || !rootWorldToLocal.ContainsKey(rootEntity))
                        return;
                    
                    // 骨骼的世界空间变换矩阵
                    var matrix = bonesLocalToWorld[boneEntity];

                    // 将世界矩空间转换到模型局部空间的变换矩阵
                    var rootMatrixInv = rootWorldToLocal[rootEntity];
                    // 获得骨骼的模型局部空间的变换矩阵
                    matrix = math.mul(rootMatrixInv, matrix);

                    // BindPose的逆矩阵
                    var bindPose = bindPoses[i].value;
                    // 获得动画当前帧的最终变换矩阵，传入Shader和顶点Position相乘，获得最终位置
                    matrix = math.mul(matrix, bindPose);

                    // 奖最终变换矩阵赋值给SkinMatrix
                    skinMatrices[i] = new SkinMatrix
                    {
                        Value = new float3x4(matrix.c0.xyz, matrix.c1.xyz, matrix.c2.xyz, matrix.c3.xyz)
                    };
                }
            }).ScheduleParallel(dependency);

        Dependency = JobHandle.CombineDependencies(bonesLocalToWorld.Dispose(dependency), rootWorldToLocal.Dispose(dependency));
    }
}

