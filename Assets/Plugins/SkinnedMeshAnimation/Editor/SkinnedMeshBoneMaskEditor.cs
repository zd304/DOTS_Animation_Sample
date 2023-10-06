using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal class BoneTransformHierachy
{
    public Transform bone;
    public BoneTransformHierachy parent;
    public List<BoneTransformHierachy> children;

    public int index = -1;
    public bool mask;
    public int depth = 0;
}

internal class SkinnedMeshBoneMaskEditor : EditorWindow
{
    private static SkinnedMeshBoneMaskEditor instance;

    private SkinnedMeshRenderer skinnedMeshRenderer;
    private BoneTransformHierachy boneHierachy;
    private SkinnedMeshBoneMask importMask;

    private Vector2 scrollPos = Vector2.zero;

    [MenuItem("Tools/Skin Animation/Bone Mask Editor")]
    static void Open()
    {
        if (instance != null)
        {
            instance.Close();
            instance = null;
        }
        instance = GetWindow<SkinnedMeshBoneMaskEditor>();
        instance.titleContent = new GUIContent("骨骼遮罩编辑器");
        instance.Show();
    }

    private void OnDestroy()
    {
        instance = null;
    }

    private void OnGUI()
    {
        SkinnedMeshRenderer oldSkinnedMeshRenderer = skinnedMeshRenderer;
        skinnedMeshRenderer = EditorGUILayout.ObjectField("蒙皮渲染器", oldSkinnedMeshRenderer, typeof(SkinnedMeshRenderer), true) as SkinnedMeshRenderer;

        if (skinnedMeshRenderer != oldSkinnedMeshRenderer && skinnedMeshRenderer != null)
        {
            if (skinnedMeshRenderer.rootBone != null)
            {
                Queue<Transform> q = new Queue<Transform>();
                q.Enqueue(skinnedMeshRenderer.rootBone);

                Dictionary<Transform, BoneTransformHierachy> dic = new Dictionary<Transform, BoneTransformHierachy>();
                boneHierachy = null;

                while (q.Count > 0)
                {
                    Transform bone = q.Dequeue();

                    BoneTransformHierachy bth = CreateBoneHierachy(bone, skinnedMeshRenderer.bones);
                    if (dic.TryGetValue(bone.parent, out var parentBTH))
                    {
                        bth.parent = parentBTH;
                        bth.depth = parentBTH.depth + 1;
                        if (parentBTH.children == null)
                        {
                            parentBTH.children = new List<BoneTransformHierachy>();
                        }
                        parentBTH.children.Add(bth);
                    }
                    dic.Add(bone, bth);
                    if (boneHierachy == null)
                    {
                        boneHierachy = bth;
                    }

                    for (int i = 0; i < bone.childCount; ++i)
                    {
                        Transform child = bone.GetChild(i);
                        q.Enqueue(child);
                    }
                }
            }
        }

        if (skinnedMeshRenderer == null || skinnedMeshRenderer.bones == null || boneHierachy == null)
        {
            return;
        }

        importMask = EditorGUILayout.ObjectField("导入遮罩", importMask, typeof(SkinnedMeshBoneMask), true) as SkinnedMeshBoneMask;

        if (importMask != null && GUILayout.Button("导入"))
        {
            Traversal((bth) =>
            {
                if (importMask.mask.Contains(bth.index))
                {
                    bth.mask = true;
                }
            });
            importMask = null;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("骨骼遮罩状态");

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        Traversal((bth) =>
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(15.0f * (bth.depth + 1));

            if (bth.index == -1)
            {
                GUI.enabled = false;
            }
            bth.mask = EditorGUILayout.ToggleLeft("[index:" + bth.index + "] " + bth.bone.name, bth.mask);
            if (bth.index == -1)
            {
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
        });

        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("反选"))
        {
            Traversal((bth) =>
            {
                if (bth.index < 0)
                {
                    return;
                }
                bth.mask = !bth.mask;
            });
        }
        if (GUILayout.Button("保存"))
        {
            string path = EditorUtility.SaveFilePanelInProject("保存遮罩", "NewMask", "asset", "将骨骼遮罩保存为ScriptableObject");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            SkinnedMeshBoneMask maskAsset = CreateInstance<SkinnedMeshBoneMask>();
            maskAsset.mask = new List<int>();
            {
                Traversal((bth) =>
                {
                    if (bth.mask)
                    {
                        maskAsset.mask.Add(bth.index);
                    }
                });
            }

            if (File.Exists(path))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.SaveAssets(); 
                AssetDatabase.Refresh();
            }
            AssetDatabase.CreateAsset(maskAsset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    private void Traversal(Action<BoneTransformHierachy> callback)
    {
        if (callback == null)
        {
            return;
        }
        Stack<BoneTransformHierachy> stack = new Stack<BoneTransformHierachy>();
        stack.Push(boneHierachy);
        while (stack.Count > 0)
        {
            BoneTransformHierachy bth = stack.Pop();

            callback(bth);

            if (bth.children != null)
            {
                for (int i = bth.children.Count - 1; i >= 0; --i)
                {
                    BoneTransformHierachy child = bth.children[i];
                    stack.Push(child);
                }
            }
        }
    }

    private BoneTransformHierachy CreateBoneHierachy(Transform bone, Transform[] bones)
    {
        BoneTransformHierachy bth = new BoneTransformHierachy();
        bth.bone = bone;
        bth.mask = false;
        for (int i = 0; i < bones.Length; ++i)
        {
            Transform b = bones[i];
            if (b == bone)
            {
                bth.index = i;
                break;
            }
        }
        return bth;
    }
}