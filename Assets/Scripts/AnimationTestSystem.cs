using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

public partial struct AnimationTestSystem : ISystem
{
    private const float AUTO_PLAY_INTERVAL = 2.0f;
    private float time;
    private int animIndex;
    private int attackIndex;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        time = AUTO_PLAY_INTERVAL;
        animIndex = 0;
        attackIndex = 0;
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (time > 0.0f)
        {
            time -= SystemAPI.Time.DeltaTime;
            return;
        }
        time = AUTO_PLAY_INTERVAL;
        animIndex = ++animIndex % 2;
        attackIndex = ++attackIndex % 3;
        
        foreach (DynamicBuffer<AnimationRequest> requests in SystemAPI.Query<DynamicBuffer<AnimationRequest>>())
        {
            FixedString128Bytes animPath1 = "AnimationAssets/Knight/Knight@Idle";
            FixedString128Bytes animPath2 = "AnimationAssets/Knight/Knight@Run";
            requests.Add(new AnimationRequest()
            {
                animationName = animIndex == 0 ? animPath1 : animPath2,
                fadeoutTime = 0.15f,
                layer = 1,
                speed = 1.0f
            });
            if (attackIndex == 0)
            {
                requests.Add(new AnimationRequest()
                {
                    animationName = "AnimationAssets/Knight/Knight@Slash",
                    fadeoutTime = 0.15f,
                    layer = 2,
                    speed = 1.0f,
                    maskPath = "AnimationMasks/Knight/Knight@UpperBody"
                });
            }
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }
}