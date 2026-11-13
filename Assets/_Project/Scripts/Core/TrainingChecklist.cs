using System;

namespace Game.Core
{
    // The things the training range (#153) asks a newcomer to do, in the order the list shows them.
    public enum TrainingStep { Move, Sprint, Crouch, Prone, Vault, Ledge, Swim, Pickup, Attach, Shoot, Reload, Aim, Zero, Heal, Throw, Vehicle, Door }

    // What the range reads off the local player each frame. Counters only ever grow; the checklist ticks when one grows
    // (the first reading is only the starting point, so a pickup or shot made before the range opened counts for nothing).
    public struct TrainingObservation
    {
        public float PlanarSpeed;   // m/s
        public bool Sprinting;
        public Stance Stance;
        public ParkourMove Parkour;
        public bool Swimming;
        public bool Seated;         // in a vehicle seat
        public bool Reloading;
        public AimMode Aim;
        public int Shots;           // shots fired by the player
        public int Pickups;         // loot taken off the ground
        public int Attachments;     // attachments fitted to guns
        public int Heals;           // healing / boost items finished
        public int Throws;          // throwables thrown
        public int DoorOpens;       // doors opened
        public int ZeroChanges;     // times the player changed the scope's zero distance
    }

    // The training range's checklist: pure rules, no scene. Steps are ticked by Observe and never un-ticked.
    public class TrainingChecklist
    {
        public const float MoveSpeed = 0.5f;   // m/s over which the player is "moving"

        public static readonly int Count = Enum.GetValues(typeof(TrainingStep)).Length;

        readonly bool[] _done = new bool[Count];
        bool _started;
        TrainingObservation _last;

        public event Action<TrainingStep> Completed;

        public int DoneCount { get; private set; }
        public bool AllDone => DoneCount == Count;
        public bool IsDone(TrainingStep step) => _done[(int)step];

        // Short Korean name shown in the list; the HUD adds the instruction with the live key bindings.
        public static string Title(TrainingStep step)
        {
            switch (step)
            {
                case TrainingStep.Move: return "걷기";
                case TrainingStep.Sprint: return "달리기";
                case TrainingStep.Crouch: return "앉기";
                case TrainingStep.Prone: return "엎드리기";
                case TrainingStep.Vault: return "낮은 장애물 넘기";
                case TrainingStep.Ledge: return "턱 매달려 오르기";
                case TrainingStep.Swim: return "수영";
                case TrainingStep.Pickup: return "아이템 줍기";
                case TrainingStep.Attach: return "부착물 달기";
                case TrainingStep.Shoot: return "사격";
                case TrainingStep.Reload: return "재장전";
                case TrainingStep.Aim: return "조준경 조준";
                case TrainingStep.Zero: return "영점 바꾸기";
                case TrainingStep.Heal: return "회복·부스트";
                case TrainingStep.Throw: return "투척";
                case TrainingStep.Vehicle: return "차량 타기";
                case TrainingStep.Door: return "문 열기";
                default: return step.ToString();
            }
        }

        // Ticks what this frame shows; returns how many steps it completed.
        public int Observe(in TrainingObservation o)
        {
            int before = DoneCount;
            ObserveStates(o);
            if (_started)
            {
                if (o.Shots > _last.Shots) Tick(TrainingStep.Shoot);
                if (o.Pickups > _last.Pickups) Tick(TrainingStep.Pickup);
                if (o.Attachments > _last.Attachments) Tick(TrainingStep.Attach);
                if (o.Heals > _last.Heals) Tick(TrainingStep.Heal);
                if (o.Throws > _last.Throws) Tick(TrainingStep.Throw);
                if (o.DoorOpens > _last.DoorOpens) Tick(TrainingStep.Door);
                if (o.ZeroChanges > _last.ZeroChanges) Tick(TrainingStep.Zero);
            }
            _started = true;
            _last = o;
            return DoneCount - before;
        }

        void ObserveStates(in TrainingObservation o)
        {
            if (o.PlanarSpeed > MoveSpeed && !o.Seated) Tick(TrainingStep.Move);
            if (o.Sprinting && o.PlanarSpeed > MoveSpeed) Tick(TrainingStep.Sprint);
            if (o.Stance == Stance.Crouch) Tick(TrainingStep.Crouch);
            if (o.Stance == Stance.Prone && !o.Swimming) Tick(TrainingStep.Prone);
            if (o.Parkour == ParkourMove.Vault || o.Parkour == ParkourMove.Climb) Tick(TrainingStep.Vault);
            if (o.Parkour == ParkourMove.LedgeClimb) Tick(TrainingStep.Ledge);
            if (o.Swimming) Tick(TrainingStep.Swim);
            if (o.Seated) Tick(TrainingStep.Vehicle);
            if (o.Reloading) Tick(TrainingStep.Reload);
            if (o.Aim == AimMode.Sights) Tick(TrainingStep.Aim);
        }

        void Tick(TrainingStep step)
        {
            if (_done[(int)step]) return;
            _done[(int)step] = true;
            DoneCount++;
            Completed?.Invoke(step);
        }
    }
}
