using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public enum KeyframePropertyType : byte
{
    PositionAndScale,
    Rotation,
}

public enum AnimationWrapType
{
    Clamp,
    Loop,
    Once
}

/// <summary>
/// 运行时关键帧
/// </summary>
public struct Keyframe
{
    public float time;
    public float4 value;
    public float4 inTangent;
    public float4 outTangent;
}

/// <summary>
/// 运行时曲线
/// </summary>
public struct AnimationCurve
{
    /// <summary>
    /// 关键帧数据
    /// </summary>
    public BlobArray<Keyframe> keyframes;
    public int boneIndex;
    /// <summary>
    /// 曲线类型：PositionAndScale或者Rotation
    /// </summary>
    public KeyframePropertyType propertyType;
}

/// <summary>
/// 运行时动画曲线数据
/// </summary>
internal struct SkinnedMeshAnimationCurve : IBufferElementData
{
    /// <summary>
    /// 曲线所处的动画层
    /// </summary>
    public int layer;
    /// <summary>
    /// 曲线的开始播放时间
    /// </summary>
    public float startTime;
    /// <summary>
    /// 曲线的持续时间
    /// </summary>
    public float duration;
    /// <summary>
    /// 曲线的播放速度
    /// </summary>
    public float speed;
    /// <summary>
    /// 曲线的包装类型
    /// </summary>
    public AnimationWrapType wrapType;
    /// <summary>
    /// 曲线的帧数据
    /// </summary>
    public BlobAssetReference<AnimationCurve> curveRef;
    
    /// <summary>
    /// 运行时临时变量：正在进行Cross Fade的动画信息
    /// </summary>
    public SkinnedMeshLayerFadeout layerFadeout;
    /// <summary>
    /// 运行时临时变量：即将取代当前曲线的下一条曲线信息
    /// </summary>
    public SkinnedMeshAninationFadeout nextCurve;
}

internal struct SkinnedMeshLayerFadeout
{
    public float startTime;
    public float duration;
    public float fadeoutTime;
}

internal struct SkinnedMeshAninationFadeout
{
    public float fadeOutTime;
    public float startTime;
    public float duration;
    public float speed;
    public AnimationWrapType wrapType;
    public BlobAssetReference<AnimationCurve> curveRef;
}

public struct SkinnedMeshAnimationController : IComponentData
{
    /// <summary>
    /// 标记当前骨骼是否受动画影响
    /// </summary>
    public bool enable;
    /// <summary>
    /// 当前骨骼正在播放的层级
    /// </summary>
    public int currentLayer;
}

public struct AnimationRequest : IBufferElementData
{
    /// <summary>
    /// 动画路径
    /// </summary>
    public FixedString128Bytes animationName;
    public int layer;
    public float speed;
    public float fadeoutTime;
    public FixedString128Bytes maskPath;
}

/// <summary>
/// 非根骨骼组件，用于获取骨骼Entity
/// </summary>
internal struct BoneEntity : IBufferElementData
{
    public Entity entity;
}

/// <summary>
/// 根骨骼组件，用于获取根骨骼Entity
/// </summary>
internal struct RootEntity : IComponentData
{
    public Entity value;
}

/// <summary>
/// BindPose逆矩阵
/// </summary>
internal struct BindPose : IBufferElementData
{
    public float4x4 value;
}

internal struct BoneIndex : IComponentData
{
    public int value;
}

internal struct BoneTag : IComponentData { }

internal struct RootTag : IComponentData { }

[BakingType]
internal struct BoneBakedTag : IComponentData { }