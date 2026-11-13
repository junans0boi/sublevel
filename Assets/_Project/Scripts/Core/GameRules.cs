using System;

namespace Game.Core
{
    // Match-wide tuning values that are not tied to one weapon. Loaded from data (ADR-032).
    [Serializable]
    public class GameRules
    {
        public float MedkitHeal = 50f;
        public SlotRules Slots = new SlotRules();
        public LocomotionParams Locomotion = new LocomotionParams();
        public AimRules Aim = new AimRules();
        public LeanRules Lean = new LeanRules();
        public RecoilRules Recoil = new RecoilRules();
        public FireRules Fire = new FireRules();
        public CombatFxRules CombatFx = new CombatFxRules();
        public GaitRules Gait = new GaitRules();
        public JumpRules Jump = new JumpRules();
        public StanceRules Stance = new StanceRules();
        public FallRules Fall = new FallRules();
        public FreeLookRules FreeLook = new FreeLookRules();
        public ArmorRules Armor = new ArmorRules();
        public CrateRules Crate = new CrateRules();
        public ThrowableRules Throwables = new ThrowableRules();
        public HealRules Heal = new HealRules();
        public CarryRules Carry = new CarryRules();
        public SmokeRules Smoke = new SmokeRules();
        public BotRules Bot = new BotRules();
        public MeleeRules Melee = new MeleeRules();
        public MatchRules Match = new MatchRules();
        public ParkourRules Parkour = new ParkourRules();
        public VehicleRules Vehicles = new VehicleRules();
        public VehicleRules Motorcycles = VehicleRules.MotorcycleDefaults();
        public TeamRules Team = new TeamRules();
        public FuelRules Fuel = new FuelRules();
        public VehicleDamageRules VehicleDamage = new VehicleDamageRules();                         // #90, ADR-074
        public VehicleDamageRules MotorcycleDamage = VehicleDamageRules.MotorcycleDefaults();
    }
}
