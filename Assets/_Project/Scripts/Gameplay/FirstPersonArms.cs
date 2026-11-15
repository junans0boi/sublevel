using UnityEngine;

namespace Game.Gameplay
{
    // The player's own forearms and gloved hands in the eye view (#216): the body is hidden there, so two arms are
    // built from a few tapered tubes and boxes and reached with two-bone IK from shoulders just below and beside the
    // camera to the spots the weapon says its right hand and support hand hold. Placed by WeaponVisual every frame
    // the weapon is at the eye. Sleeve and glove colours follow the third-person model's uniform (dark olive, black).
    public sealed class FirstPersonArms
    {
        // Viewmodel arms run a little long so the support hand reaches a rifle's handguard from the shoulder.
        public const float UpperMeters = 0.36f, ForeMeters = 0.34f, PalmCentre = 0.05f, MinReach = 0.46f;
        static readonly Vector3 ShoulderRight = new Vector3(0.2f, -0.2f, -0.08f);

        readonly GameObject _root;
        readonly Transform[] _upper = new Transform[2], _fore = new Transform[2], _elbow = new Transform[2], _hand = new Transform[2];
        // Per hand: four fingers and the thumb, each two jointed segments (the joint is the pivot that is turned).
        readonly Transform[][][] _digit = new Transform[2][][];
        static readonly float[][] DigitLength = { new[] { 0.042f, 0.034f }, new[] { 0.046f, 0.036f }, new[] { 0.042f, 0.033f }, new[] { 0.034f, 0.028f }, new[] { 0.04f, 0.034f } };

        // Hand parts (world positions this frame) for the review shots and tests.
        public Vector3 HandPosition(int side) => _hand[side].position;
        public Vector3 PalmPosition(int side) => _hand[side].TransformPoint(new Vector3(0f, -0.02f, 0.07f));
        public static Vector3 Shoulder(Transform camera, int side) =>
            camera.TransformPoint(new Vector3(ShoulderRight.x * (side == 1 ? 1f : -1f), ShoulderRight.y, ShoulderRight.z));
        public bool Visible => _root.activeSelf;

        public FirstPersonArms(Transform parent, Material sleeve, Material glove)
        {
            _root = new GameObject("FirstPersonArms");
            _root.transform.SetParent(parent, false);
            var cube = Primitive(PrimitiveType.Cube);
            var sphere = Primitive(PrimitiveType.Sphere);
            var capsule = Primitive(PrimitiveType.Capsule);
            var tubeUpper = Tube(0.052f, 0.046f);
            var tubeFore = Tube(0.046f, 0.04f);
            for (int s = 0; s < 2; s++)
            {
                _upper[s] = Part($"Upper{s}", tubeUpper, sleeve, _root.transform);
                _fore[s] = Part($"Fore{s}", tubeFore, sleeve, _root.transform);
                _elbow[s] = Part($"Elbow{s}", sphere, sleeve, _root.transform);
                _elbow[s].localScale = Vector3.one * 0.092f;
                _hand[s] = new GameObject($"Hand{s}").transform;
                _hand[s].SetParent(_root.transform, false);
                // Origin at the wrist, z along the forearm, y the back of the hand.
                var cuff = Part("Cuff", Tube(0.047f, 0.044f), glove, _hand[s]);
                cuff.localPosition = new Vector3(0f, 0f, -0.07f); cuff.localScale = new Vector3(1f, 1f, 0.09f);
                var palm = Part("Palm", cube, glove, _hand[s]);
                palm.localPosition = new Vector3(0f, 0f, 0.05f); palm.localScale = new Vector3(0.086f, 0.034f, 0.095f);
                _digit[s] = new Transform[5][];
                float sign = s == 1 ? 1f : -1f;
                for (int d = 0; d < 5; d++)
                {
                    float thick = d == 4 ? 0.034f : d == 3 ? 0.027f : 0.03f;
                    var joint = new GameObject($"Digit{d}").transform;
                    joint.SetParent(_hand[s], false);
                    // Index to little finger across the knuckles (the index on the thumb's side); the thumb starts at the heel of the hand.
                    joint.localPosition = d == 4 ? new Vector3(-sign * 0.043f, -0.006f, 0.03f) : new Vector3(-sign * (0.032f - 0.0215f * d), 0.004f, 0.097f);
                    _digit[s][d] = new Transform[2];
                    Transform parentJoint = joint;
                    for (int k = 0; k < 2; k++)
                    {
                        float len = DigitLength[d][k];
                        var pivot = k == 0 ? joint : new GameObject($"Digit{d}b").transform;
                        if (k == 1) { pivot.SetParent(parentJoint, false); pivot.localPosition = new Vector3(0f, 0f, DigitLength[d][0]); }
                        var seg = Part("Seg", capsule, glove, pivot);
                        seg.localPosition = new Vector3(0f, 0f, len * 0.5f);
                        seg.localRotation = Quaternion.Euler(90f, 0f, 0f);
                        seg.localScale = new Vector3(thick, len * 0.5f + 0.004f, thick);
                        _digit[s][d][k] = pivot;
                        parentJoint = pivot;
                    }
                }
                var heel = Part("Heel", sphere, glove, _hand[s]);
                heel.localPosition = new Vector3(0f, -0.002f, 0.015f); heel.localScale = new Vector3(0.088f, 0.044f, 0.07f);
            }
            _root.SetActive(false);
        }

        public void Destroy() { if (_root != null) Object.Destroy(_root); }
        public void SetVisible(bool on) { if (_root.activeSelf != on) _root.SetActive(on); }

        // `centre`: where the middle of the palm goes (world); `back`: the way the back of the hand faces (world);
        // `curl`: 0 flat .. 1 closed round the weapon. Side 0 left, 1 right.
        public void Place(Transform camera, int side, Vector3 centre, Vector3 back, float curl, bool keepOutOfLens = true)
        {
            float sign = side == 1 ? 1f : -1f;
            var shoulder = camera.TransformPoint(new Vector3(ShoulderRight.x * sign, ShoulderRight.y, ShoulderRight.z));
            // Never folded up close to the lens (an over-bent arm swings its elbow and forearm across the whole view):
            // the palm stays at least MinReach from the shoulder and a hand's reach in front of the camera.
            var local = camera.InverseTransformPoint(centre);
            if (keepOutOfLens && local.z < 0.3f) { local.z = 0.3f; centre = camera.TransformPoint(local); }
            var away = centre - shoulder;
            if (keepOutOfLens && away.magnitude < MinReach) centre = shoulder + away.normalized * MinReach;
            // The wrist sits a palm's half-length behind the palm centre, along the line from the shoulder.
            var toCentre = (centre - shoulder).normalized;
            var wrist = centre - toCentre * PalmCentre;
            var pole = camera.TransformDirection(new Vector3(0.45f * sign, -1f, -0.2f));
            Solve(shoulder, wrist, UpperMeters, ForeMeters, pole, out var elbow, out var reached);

            Stretch(_upper[side], shoulder, elbow);
            Stretch(_fore[side], elbow, reached);
            _elbow[side].position = elbow;

            var z = (reached - elbow).normalized;
            var y = Vector3.ProjectOnPlane(back, z);
            if (y.sqrMagnitude < 1e-6f) y = Vector3.ProjectOnPlane(camera.up, z);
            _hand[side].SetPositionAndRotation(reached, Quaternion.LookRotation(z, y.normalized));
            // Closed hand: each finger folds down over the palm in two joints, the thumb lies along the side.
            float c = Mathf.Clamp01(curl);
            var digit = _digit[side];
            for (int d = 0; d < 4; d++)
            {
                digit[d][0].localRotation = Quaternion.Euler(Mathf.Lerp(8f, 62f, c), 0f, 0f);
                digit[d][1].localRotation = Quaternion.Euler(Mathf.Lerp(5f, 95f, c), 0f, 0f);
            }
            digit[4][0].localRotation = Quaternion.Euler(Mathf.Lerp(0f, 26f, c), -sign * Mathf.Lerp(35f, 12f, c), 0f);
            digit[4][1].localRotation = Quaternion.Euler(Mathf.Lerp(0f, 40f, c), 0f, 0f);
        }

        // Two-bone solution: the middle joint on the circle round the shoulder-wrist line, towards `pole`.
        public static void Solve(Vector3 shoulder, Vector3 target, float a, float b, Vector3 pole, out Vector3 elbow, out Vector3 wrist)
        {
            var line = target - shoulder;
            float d = Mathf.Clamp(line.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
            var dir = line.sqrMagnitude > 1e-8f ? line.normalized : Vector3.forward;
            float x = (a * a - b * b + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
            var perp = Vector3.ProjectOnPlane(pole, dir);
            perp = perp.sqrMagnitude > 1e-8f ? perp.normalized : Vector3.up;
            wrist = shoulder + dir * d;
            elbow = shoulder + dir * x + perp * h;
        }

        static void Stretch(Transform t, Vector3 from, Vector3 to)
        {
            var v = to - from;
            float len = Mathf.Max(0.01f, v.magnitude);
            t.SetPositionAndRotation(from, Quaternion.LookRotation(v / len));
            t.localScale = new Vector3(1f, 1f, len);
        }

        static Transform Part(string name, Mesh mesh, Material mat, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        static Mesh Primitive(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            go.SetActive(false);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return mesh;
        }

        // A tapered tube from z = 0 (radius r0) to z = 1 (radius r1); the part's z scale is its length.
        static Mesh Tube(float r0, float r1)
        {
            const int sides = 10;
            var verts = new Vector3[sides * 2];
            var normals = new Vector3[sides * 2];
            var uv = new Vector2[sides * 2];
            var tris = new int[sides * 6];
            for (int i = 0; i < sides; i++)
            {
                float ang = i * Mathf.PI * 2f / sides;
                var dirn = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                verts[i] = dirn * r0; verts[sides + i] = dirn * r1 + Vector3.forward;
                normals[i] = normals[sides + i] = dirn;
                uv[i] = new Vector2((float)i / sides, 0f); uv[sides + i] = new Vector2((float)i / sides, 1f);
                int j = (i + 1) % sides, k = i * 6;
                tris[k] = i; tris[k + 1] = sides + i; tris[k + 2] = j;
                tris[k + 3] = j; tris[k + 4] = sides + i; tris[k + 5] = sides + j;
            }
            return new Mesh { name = "ArmTube", vertices = verts, normals = normals, uv = uv, triangles = tris };
        }
    }
}
