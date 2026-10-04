//using Animancer;
//using DG.Tweening;
//using UnityEngine;

//public class PlayerVaultState : PlayerMovementState
//{
//    PlayerClimbData HandWallData;
//    Vector3 targetRightHand;
//    Vector3 targetPos;
//    Transform rightHandBone;
//    int drawID;
//    AnimancerState animancerState;
//    public PlayerVaultState(PlayerStateMachine stateMachine) : base(stateMachine)
//    {
//        HandWallData = playerSO.playerMovementData.PlayerClimbData;
//        rightHandBone = player.animancer.GetBoneTransform(HumanBodyBones.RightHand);
//    }

//    public override void OnEnter()
//    {
      
//        player.disEnableGravity = true;
//        targetRightHand = reusableData.hit.point + Vector3.Cross(reusableData.hit.normal, Vector3.up)*0.8f;
//        targetPos = reusableData.hit.point + Vector3.up * 0.3f;
//        animancerState = animancer.Play(HandWallData.vault);
//        drawID = GameRoot.Instance.AddDrawGizmos(() => { Gizmos.color = Color.green; Gizmos.DrawSphere(targetRightHand, 0.1f); });
//        base.OnEnter();
//    }
//    protected override void AddEventListening()
//    {
//        base.AddEventListening();
//        animancerState.Events(player).OnEnd += OnStateDefaultEnd;
//        animancerState.Events(player).SetCallback(playerSO.playerParameterData.moveInterruptEvent, OnInputInterruption);
//        animancerState.Events(player).Add(0.3f, () => { player.disEnableGravity = false; });
//    }
//    protected override void OnInputInterruption()
//    {
       
//        base.OnInputInterruption();
//    }
//    public override void OnExit()
//    {
//        base.OnExit();
//        reusableData.inputInterruptionCB = null;
//        GameRoot.Instance.RemoveDrawGizmos(drawID);
//    }
//    public override void OnUpdate()
//    {
//        base.OnUpdate();
//    }
//    public override void OnAnimationUpdate()
//    {
//        base.OnAnimationUpdate();
//        player.animancer.applyRootMotion = true;
//        //MatchRightHand(0.1f,0.22f);

//    }
//    /// <summary>
//    /// 手部匹配
//    /// </summary>
//    /// <param name="startTime"></param>
//    /// <param name="endTime"></param>
//    private void MatchRightHand(float startTime, float endTime)
//    {
//        float normalizedTime = animancer.States.Current.NormalizedTime;
//        float duration = endTime - startTime;
//        if (normalizedTime >= startTime && normalizedTime <= endTime)
//        {
//            // 计算当前时间在这个时间段内的进度
//            float t = (normalizedTime - startTime) / duration;

//            // 对右手骨骼的位置进行插值
//            rightHandBone.position = Vector3.Lerp(rightHandBone.position, targetRightHand, t);
//        }
//    }
//    private void MatchPlayerPos(float startTime, float endTime)
//    {
//        if (endTime < startTime)
//        {
//            return;
//        }
//        float normalizedTime = animancer.States.Current.NormalizedTime;
//        float duration = endTime - startTime;
//        if (normalizedTime >= startTime && normalizedTime <= endTime)
//        {
//            float t = (normalizedTime - startTime) / duration;
//            player.transform.position = Vector3.Lerp(player.transform.position, targetPos, t);
//            player.controller.enabled = false;
//        }
//        else
//        {
//            player.controller.enabled = true;
//        }
//    }
//}