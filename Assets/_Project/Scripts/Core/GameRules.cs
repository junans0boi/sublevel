using System;

namespace Game.Core
{
    // Match-wide tuning values that are not tied to one weapon. Loaded from data (ADR-032).
    [Serializable]
    public class GameRules
    {
        public float MedkitHeal = 50f;
        public LocomotionParams Locomotion = new LocomotionParams();
        public AimRules Aim = new AimRules();
        public LeanRules Lean = new LeanRules();
        public RecoilRules Recoil = new RecoilRules();
        public GaitRules Gait = new GaitRules();
        public JumpRules Jump = new JumpRules();
    }
}
