using Unity.Entities;
using UnityEngine;

public struct AnimationTestRootTag : IComponentData {}

public class AnimationTestAuthoring : MonoBehaviour
{
    private class AnimationTestBaker : Baker<AnimationTestAuthoring>
    {
        public override void Bake(AnimationTestAuthoring authoring)
        {
			Entity entity = GetEntity(TransformUsageFlags.Dynamic);
			AddComponent(entity, new AnimationTestRootTag());
        }
    }
}