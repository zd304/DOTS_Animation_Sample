using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
// ReSharper disable once CheckNamespace
public class BakedKeyframe
{
    /// <summary>
    /// 帧时间
    /// </summary>
    public float time;
    /// <summary>
    /// 关键帧的值
    /// </summary>
    public Vector4 value;
    /// <summary>
    /// 关键帧进入的曲线切线
    /// </summary>
    public Vector4 inTangent;
    /// <summary>
    /// 关键帧出去的曲线切线
    /// </summary>
    public Vector4 outTangent;
}

[Serializable]
public class BakedCurve
{
    /// <summary>
    /// 曲线作用的骨骼索引
    /// </summary>
    public int boneIndex;
    /// <summary>
    /// 曲线的所有关键帧
    /// </summary>
    public List<BakedKeyframe> keyframes = new List<BakedKeyframe>();
}

public class SkinnedMeshAnimationClip : ScriptableObject
{
    /// <summary>
    /// 动画长度
    /// </summary>
    public float length;
    /// <summary>
    /// 动画包装模式
    /// </summary>
    public AnimationWrapType wrapType;
    
    /// <summary>
    /// 所有骨骼的位置和缩放曲线：xyz保存位置信息，w保存缩放信息
    /// </summary>
    public BakedCurve[] posAndSclCurves;
    /// <summary>
    /// 所有骨骼的旋转曲线：xyzw保存四元数信息
    /// </summary>
    public BakedCurve[] rotCurves;
}