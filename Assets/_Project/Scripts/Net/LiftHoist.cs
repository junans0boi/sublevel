using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The bungee lift as a site hoist (ADR-496, 준환 r5.2: "승강기 통과된다", "벨트가 돌아가면서 올라가는 것처럼"). Client only, built at
    // runtime beside the call buttons so the World scene need not be rebuilt:
    //  - solid cage sides (the gates only while they are shut, under "Doors"), colliders on the four shaft posts and a mesh fence along
    //    the shaft's east and west sides on the deck, so nobody walks through the lift;
    //  - the drive on the shaft head: a motor, a sheave that turns with the ride, the rope down to the car's roof and a counterweight that
    //    sinks in the gap east of the car as the car rises.
    // Riders are moved by BungeeRide (walking is off), so the moving walls never push them.
    public sealed class LiftHoist
    {
        const float CarHeight = 2.6f;                                   // WorldBuilder.BuildBungeeLiftCar's H
        const float CarRopeX = 0.55f, WeightX = 1.49f, WeightH = 1.6f;  // lift-local x of the car's rope and of the counterweight (car side 1.4, shaft hoops from 1.525)
        const float FenceHeight = 2.2f;
        static float HeadBottom => BungeeCurve.Top + 3.8f;              // the underside of the shaft head (WorldBuilder: a 0.4 m slab at Top + 4)
        static float SheaveR => (WeightX - CarRopeX) * 0.5f;
        // Under the head, where the top platform sees it: the car's roof at the top (Top + 2.66) passes under the sheave's rim.
        static float SheaveY => HeadBottom - 0.05f - SheaveR;

        readonly Transform _sheave, _carRope, _weightRope, _weight;

        public LiftHoist(Transform island, Transform car, Material steel, Material accent)
        {
            var lp = WaitingIsland.Lift; float sh = WaitingIsland.LiftShaftHalf, h = BungeeLift.HalfSize;
            var root = new GameObject("LiftHoist").transform;
            root.SetParent(island, false); root.localPosition = new Vector3(lp.x, 0f, lp.z);
            var rope = new Material(steel) { color = new Color(0.09f, 0.09f, 0.1f) };

            // Guards: the car's sides move with it (a kinematic body, so moving them is cheap), the gates only while shut.
            if (!car.TryGetComponent<Rigidbody>(out var body)) body = car.gameObject.AddComponent<Rigidbody>();   // a second board on the same island (two clients in one process) reuses it
            body.isKinematic = true; body.useGravity = false;
            foreach (float sx in new[] { -h, h }) Solid(car, new Vector3(sx, CarHeight * 0.5f, 0f), new Vector3(0.08f, CarHeight, h * 2f));
            var doors = car.Find("Doors");
            if (doors != null) foreach (float sz in new[] { -h, h }) Solid(doors, new Vector3(0f, CarHeight * 0.5f, sz), new Vector3(h * 2f, CarHeight, 0.08f));
            for (int i = 0; i < 4; i++)
                Solid(root, new Vector3((i & 1) == 0 ? -sh : sh, HeadBottom * 0.5f, (i & 2) == 0 ? -sh : sh), new Vector3(0.3f, HeadBottom, 0.3f));
            foreach (float sx in new[] { -sh, sh })
            {
                Solid(root, new Vector3(sx, FenceHeight * 0.5f, 0f), new Vector3(0.1f, FenceHeight, sh * 2f));
                foreach (float y in new[] { 0.1f, FenceHeight * 0.5f, FenceHeight }) Part(root, new Vector3(sx, y, 0f), new Vector3(0.06f, 0.06f, sh * 2f), steel);
                for (float z = -sh + 0.2f; z < sh - 0.1f; z += 0.2f) Part(root, new Vector3(sx, FenceHeight * 0.5f, z), new Vector3(0.025f, FenceHeight, 0.025f), steel);
            }

            // The drive, hung under the head: the motor beside the sheave, the sheave with amber spokes (so its turn shows), ropes, the weight.
            Part(root, new Vector3(CarRopeX - 0.4f, HeadBottom - 0.3f, 0f), new Vector3(0.7f, 0.6f, 0.6f), steel);
            Part(root, new Vector3((CarRopeX + WeightX) * 0.5f, HeadBottom - (SheaveR + 0.05f) * 0.5f, 0.2f), new Vector3(0.12f, SheaveR + 0.05f, 0.08f), steel);   // the bearing hanger behind the wheel
            _sheave = new GameObject("Sheave").transform; _sheave.SetParent(root, false);
            _sheave.localPosition = new Vector3((CarRopeX + WeightX) * 0.5f, SheaveY, 0f);
            var disc = Part(_sheave, Vector3.zero, new Vector3(SheaveR * 2f, 0.05f, SheaveR * 2f), steel, PrimitiveType.Cylinder);
            disc.localRotation = Quaternion.Euler(90f, 0f, 0f);          // the axle along z, the rope in the x-y plane
            Part(_sheave, Vector3.zero, new Vector3(SheaveR * 1.9f, 0.07f, 0.16f), accent);
            Part(_sheave, Vector3.zero, new Vector3(0.07f, SheaveR * 1.9f, 0.16f), accent);
            _carRope = Rope(root, CarRopeX, rope);
            _weightRope = Rope(root, WeightX, rope);
            _weight = Part(root, Vector3.zero, new Vector3(0.06f, WeightH, 1f), steel);
            Part(_weight, new Vector3(0f, 0.4f, 0f), new Vector3(1.2f, 0.08f, 1.02f), accent);   // a stripe (in the weight's scaled space)
        }

        // Each frame: the car's floor height over the deck.
        public void Move(float carY)
        {
            float roof = carY + CarHeight + 0.06f;
            Stretch(_carRope, roof, SheaveY);
            float weightBottom = BungeeLift.Height - carY + 1f;          // the car at the bottom: the weight hangs under the sheave; at the top: 1 m over the deck, inside the fence
            _weight.localPosition = new Vector3(WeightX, weightBottom + WeightH * 0.5f, 0f);
            Stretch(_weightRope, weightBottom + WeightH, SheaveY);
            _sheave.localRotation = Quaternion.Euler(0f, 0f, -carY / SheaveR * Mathf.Rad2Deg);   // rope out = wheel turned
        }

        static void Stretch(Transform rope, float from, float to)
        {
            var p = rope.localPosition; p.y = (from + to) * 0.5f; rope.localPosition = p;
            rope.localScale = new Vector3(1f, Mathf.Max(0.01f, to - from), 1f);
        }

        // Two parallel wires, 1 m tall unscaled: Stretch sets the length.
        static Transform Rope(Transform root, float x, Material m)
        {
            var r = new GameObject("Rope").transform; r.SetParent(root, false); r.localPosition = new Vector3(x, 0f, 0f);
            Part(r, new Vector3(0f, 0f, -0.08f), new Vector3(0.025f, 1f, 0.025f), m);
            Part(r, new Vector3(0f, 0f, 0.08f), new Vector3(0.025f, 1f, 0.025f), m);
            return r;
        }

        static Transform Part(Transform parent, Vector3 at, Vector3 size, Material m, PrimitiveType shape = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(shape);
            Object.DestroyImmediate(go.GetComponent<Collider>());   // looks only; the guards are Solid
            go.transform.SetParent(parent, false); go.transform.localPosition = at; go.transform.localScale = size;
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        static void Solid(Transform parent, Vector3 at, Vector3 size)
        {
            var go = new GameObject("Guard"); go.transform.SetParent(parent, false); go.transform.localPosition = at;
            go.AddComponent<BoxCollider>().size = size;
        }
    }
}
