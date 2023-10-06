using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Security.Cryptography;
using System;
using Codice.CM.Client.Differences.Graphic;
using Codice.Client.Common;

public class SkinAnimationBakeEditor : EditorWindow
{
    static SkinAnimationBakeEditor instance;

    AnimationClip animationClip;
    Transform root;
    SkinnedMeshRenderer smr;

    [MenuItem("Tools/Skin Animation/Animation Baker")]
    static void Open()
    {
        if (instance != null)
        {
            instance.Close();
            instance = null;
        }

        instance = GetWindow<SkinAnimationBakeEditor>();
        instance.titleContent = new GUIContent("蒙皮动画烘焙器");
        instance.Show();
    }

    private void AlignKeyframes(AnimationClip animClip, int boneCount,
        Dictionary<int, UnityEngine.AnimationCurve[]> curvePSInBone, Dictionary<int, UnityEngine.AnimationCurve[]> curveRInBone)
    {
        Vector4[] tPosAndScl = new Vector4[boneCount];
        Vector4[] tRot = new Vector4[boneCount];

        EditorCurveBinding[] cbs = AnimationUtility.GetCurveBindings(animClip);
        foreach (var curveBinding in cbs)
        {
            Transform tBone = root.Find(curveBinding.path);
            int index = -1;
            if (tBone != null)
            {
                for (int i = 0; i < boneCount; ++i)
                {
                    Transform bone = smr.bones[i];
                    if (bone == tBone)
                    {
                        index = i;
                        tPosAndScl[i] = new Vector4(bone.localPosition.x, bone.localPosition.y, bone.localPosition.z, bone.localScale.x);
                        tRot[i] = new Vector4(bone.localRotation.x, bone.localRotation.y, bone.localRotation.z, bone.localRotation.w);
                        break;
                    }
                }
            }
            if (index < 0)
            {
                continue;
            }

            if (!curvePSInBone.TryGetValue(index, out UnityEngine.AnimationCurve[] editorCurvePosScl))
            {
                editorCurvePosScl = new UnityEngine.AnimationCurve[4];
                curvePSInBone.Add(index, editorCurvePosScl);
            }
            if (!curveRInBone.TryGetValue(index, out UnityEngine.AnimationCurve[] editorCurveRot))
            {
                editorCurveRot = new UnityEngine.AnimationCurve[4];
                curveRInBone.Add(index, editorCurveRot);
            }

            UnityEngine.AnimationCurve editorCurve = AnimationUtility.GetEditorCurve(animClip, curveBinding);

            string propertyName = curveBinding.propertyName;
            if (propertyName.Contains("m_LocalRotation"))
            {
                if (propertyName.Contains(".x"))
                {
                    editorCurveRot[0] = editorCurve;
                }
                else if (propertyName.Contains(".y"))
                {
                    editorCurveRot[1] = editorCurve;
                }
                else if (propertyName.Contains(".z"))
                {
                    editorCurveRot[2] = editorCurve;
                }
                else if (propertyName.Contains(".w"))
                {
                    editorCurveRot[3] = editorCurve;
                }
            }
            else if (propertyName.Contains("m_LocalPosition"))
            {
                if (propertyName.Contains(".x"))
                {
                    editorCurvePosScl[0] = editorCurve;
                }
                else if (propertyName.Contains(".y"))
                {
                    editorCurvePosScl[1] = editorCurve;
                }
                else if (propertyName.Contains(".z"))
                {
                    editorCurvePosScl[2] = editorCurve;
                }
            }
            else if (propertyName.Contains("m_LocalScale"))
            {
                if (propertyName.Contains(".x"))
                {
                    editorCurvePosScl[3] = editorCurve;
                }
            }
        }

        foreach (var pair in curvePSInBone)
        {
            UnityEngine.AnimationCurve[] editorCurvePosScl = pair.Value;
            for (int i = 0; i < editorCurvePosScl.Length; ++i)
            {
                UnityEngine.AnimationCurve curve = editorCurvePosScl[i];
                if (curve != null)
                {
                    continue;
                }
                curve = new UnityEngine.AnimationCurve();
                curve.AddKey(new UnityEngine.Keyframe
                {
                    time = 0.0f,
                    value = tPosAndScl[pair.Key][i],
                    inTangent = 0.0f,
                    outTangent = 0.0f,
                });
                editorCurvePosScl[i] = curve;
            }
            AlignCurves(editorCurvePosScl);
        }
        foreach (var pair in curvePSInBone)
        {
            UnityEngine.AnimationCurve[] editorCurveRot = pair.Value;
            for (int i = 0; i < editorCurveRot.Length; ++i)
            {
                UnityEngine.AnimationCurve curve = editorCurveRot[i];
                if (curve != null)
                {
                    continue;
                }
                curve = new UnityEngine.AnimationCurve();
                curve.AddKey(new UnityEngine.Keyframe
                {
                    time = 0.0f,
                    value = tRot[pair.Key][i],
                    inTangent = 0.0f,
                    outTangent = 0.0f,
                });
                editorCurveRot[i] = curve;
            }
            AlignCurves(editorCurveRot);
        }
    }

    private static void AlignCurves(UnityEngine.AnimationCurve[] cs)
    {
        int[] curIndex = new int[4];
        bool[] notEnd = new bool[4];
        WrapMode[] recordWrapModes = new WrapMode[4];
        List<UnityEngine.Keyframe[]> copyKeys = new List<UnityEngine.Keyframe[]>();

        for (int i = 0; i < 4; ++i)
        {
            curIndex[i] = 0;

            copyKeys.Add(cs[i].keys.Clone() as UnityEngine.Keyframe[]);

            notEnd[i] = curIndex[i] < cs[i].keys.Length;
            recordWrapModes[i] = cs[i].postWrapMode;
            cs[i].postWrapMode = WrapMode.Clamp;
        }

        while (notEnd[0] || notEnd[1] || notEnd[2] || notEnd[3])
        {
            UnityEngine.Keyframe[] kfs = new UnityEngine.Keyframe[4];

            float minTime = float.MaxValue;
            for (int i = 0; i < 4; ++i)
            {
                if (notEnd[i])
                {
                    kfs[i] = copyKeys[i][curIndex[i]];
                    if (kfs[i].time <= minTime)
                    {
                        minTime = kfs[i].time;
                        ++curIndex[i];
                    }
                }
            }

            for (int i = 0; i < 4; ++i)
            {
                UnityEngine.Keyframe kf = kfs[i];
                bool curNotEnd = notEnd[i];
                if (notEnd[i])
                {
                    notEnd[i] = curIndex[i] < copyKeys[i].Length;
                }

                if (!curNotEnd || kf.time != minTime)
                {
                    float v = cs[i].Evaluate(minTime);
                    cs[i].AddKey(minTime, v);
                }
            }
        }

        for (int i = 0; i < 4; ++i)
        {
            cs[i].postWrapMode = recordWrapModes[i];
        }
    }

    private void OnGUI()
    {
        animationClip = EditorGUILayout.ObjectField("动画片段", animationClip, typeof(AnimationClip), false) as AnimationClip;
        root = EditorGUILayout.ObjectField("动画根节点", root, typeof(Transform), true) as Transform;
        smr = EditorGUILayout.ObjectField("蒙皮渲染器", smr, typeof(SkinnedMeshRenderer), true) as SkinnedMeshRenderer;

        if (animationClip != null && smr != null && root != null && GUILayout.Button("生成"))
        {
            string path = EditorUtility.SaveFilePanelInProject("保存", animationClip.name, "asset", "生成动画片段配置");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            AnimationClip animClip = UnityEngine.Object.Instantiate<AnimationClip>(animationClip);

            SkinnedMeshAnimationClip clip = CreateInstance<SkinnedMeshAnimationClip>();
            clip.length = animClip.length;
            clip.wrapType = animClip.wrapMode == WrapMode.Loop ? AnimationWrapType.Loop : AnimationWrapType.Clamp;

            int boneCount = smr.bones.Length;
            clip.posAndSclCurves = new BakedCurve[boneCount];
            clip.rotCurves = new BakedCurve[boneCount];

            Dictionary<int, UnityEngine.AnimationCurve[]> curvePSInBone = new Dictionary<int, UnityEngine.AnimationCurve[]>();
            Dictionary<int, UnityEngine.AnimationCurve[]> curveRInBone = new Dictionary<int, UnityEngine.AnimationCurve[]>();
            AlignKeyframes(animClip, boneCount, curvePSInBone, curveRInBone);

            foreach (var pair in curvePSInBone)
            {
                int boneIndex = pair.Key;
                BakedCurve bakedCurve = new BakedCurve();
                clip.posAndSclCurves[boneIndex] = bakedCurve;
                bakedCurve.boneIndex = boneIndex;

                UnityEngine.AnimationCurve[] curveXYZW = pair.Value;

                for (int i = 0; i < curveXYZW.Length; ++i)
                {
                    UnityEngine.AnimationCurve curvePart = curveXYZW[i];
                    for (int j = 0; j < curvePart.keys.Length; ++j)
                    {
                        UnityEngine.Keyframe key = curvePart.keys[j];
                        if (i == 0)
                        {
                            bakedCurve.keyframes.Add(new BakedKeyframe()
                            {
                                time = key.time,
                            });
                        }
                        BakedKeyframe bakedKeyframe = bakedCurve.keyframes[j];
                        bakedKeyframe.value[i] = key.value;
                        bakedKeyframe.inTangent[i] = key.inTangent;
                        bakedKeyframe.outTangent[i] = key.outTangent;
                    }
                }
            }
            foreach (var pair in curveRInBone)
            {
                int boneIndex = pair.Key;
                BakedCurve bakedCurve = new BakedCurve();
                clip.rotCurves[boneIndex] = bakedCurve;
                bakedCurve.boneIndex = boneIndex;

                UnityEngine.AnimationCurve[] curveXYZW = pair.Value;

                for (int i = 0; i < curveXYZW.Length; ++i)
                {
                    UnityEngine.AnimationCurve curvePart = curveXYZW[i];
                    for (int j = 0; j < curvePart.keys.Length; ++j)
                    {
                        UnityEngine.Keyframe key = curvePart.keys[j];
                        if (i == 0)
                        {
                            bakedCurve.keyframes.Add(new BakedKeyframe()
                            {
                                time = key.time,
                            });
                        }
                        BakedKeyframe bakedKeyframe = bakedCurve.keyframes[j];
                        bakedKeyframe.value[i] = key.value;
                        bakedKeyframe.inTangent[i] = key.inTangent;
                        bakedKeyframe.outTangent[i] = key.outTangent;
                    }
                }
            }
            
            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
        }
    }
}
