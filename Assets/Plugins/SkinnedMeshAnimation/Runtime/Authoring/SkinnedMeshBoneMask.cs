using System.Collections.Generic;
using UnityEngine;

// ReSharper disable once CheckNamespace
public class SkinnedMeshBoneMask : ScriptableObject
{
    /// <summary>
    /// 允许播放动画的骨骼index
    /// </summary>
    public List<int> mask;
}