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
        public GuardRules Guard = new GuardRules(); // #170, ADR-142
        public BallisticsRules Ballistics = new BallisticsRules(); // ADR-098
        public PenetrationRules Penetration = new PenetrationRules(); // ADR-114
        public CombatFxRules CombatFx = new CombatFxRules();
        public GaitRules Gait = new GaitRules();
        public RifleHoldRules RifleHold = new RifleHoldRules(); // ADR-083
        public RifleHoldRules PistolHold = RifleHoldRules.Pistol(); // #218: a hand gun in both hands
        public JumpRules Jump = new JumpRules();
        public StanceRules Stance = new StanceRules();
        public FallRules Fall = new FallRules();
        public FreeLookRules FreeLook = new FreeLookRules();
        public ArmorRules Armor = new ArmorRules();
        public CrateRules Crate = new CrateRules();
        public ThrowableRules Throwables = new ThrowableRules();
        public HealRules Heal = new HealRules();
        public BoostRules Boost = new BoostRules();
        public CarryRules Carry = new CarryRules();
        public SmokeRules Smoke = new SmokeRules();
        public SupplyRules Supply = new SupplyRules();
        public FlashRules Flash = new FlashRules();
        public MolotovRules Molotov = new MolotovRules();
        public BotRules Bot = new BotRules();
        public MeleeRules Melee = new MeleeRules();
        public MatchRules Match = new MatchRules();
        public IslandRules Island = new IslandRules();   // the waiting island's range and board (ADR-501/502)
        public InterestRules Interest = new InterestRules(); // #167, ADR-135
        public InteractRules Interact = new InteractRules(); // ADR-276
        public ParkourRules Parkour = new ParkourRules();
        public VehicleRules Vehicles = new VehicleRules();
        public VehicleRules Motorcycles = VehicleRules.MotorcycleDefaults();
        public TeamRules Team = new TeamRules();
        public FuelRules Fuel = new FuelRules();
        public VehicleDamageRules VehicleDamage = new VehicleDamageRules();                         // #90, ADR-074
        public VehicleDamageRules MotorcycleDamage = VehicleDamageRules.MotorcycleDefaults();
    }
}
