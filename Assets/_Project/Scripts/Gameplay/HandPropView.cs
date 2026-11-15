using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // The small thing a hand holds while it throws, heals or drinks (#220 #221): built from primitives the first time
    // it is needed, one object shared by third and first person. Colours only, no textures.
    public sealed class HandPropView
    {
        readonly GameObject _root;
        readonly GameObject[] _kinds = new GameObject[10];
        HandProp _shown;

        public HandPropView(Material source)
        {
            _root = new GameObject("HandProp");
            Make(HandProp.Grenade, PrimitiveType.Sphere, new Vector3(0.07f, 0.07f, 0.07f), new Color(0.2f, 0.26f, 0.15f), source, out var g);
            Add(g, PrimitiveType.Cylinder, new Vector3(0.018f, 0.012f, 0.018f), new Vector3(0f, 0.04f, 0f), Quaternion.identity, new Color(0.5f, 0.5f, 0.52f), source);
            Make(HandProp.Smoke, PrimitiveType.Cylinder, new Vector3(0.06f, 0.07f, 0.06f), new Color(0.55f, 0.56f, 0.58f), source, out _);
            Make(HandProp.Flash, PrimitiveType.Cylinder, new Vector3(0.055f, 0.06f, 0.055f), new Color(0.65f, 0.58f, 0.38f), source, out _);
            Make(HandProp.Molotov, PrimitiveType.Cylinder, new Vector3(0.06f, 0.09f, 0.06f), new Color(0.3f, 0.2f, 0.1f), source, out _);
            Make(HandProp.Bandage, PrimitiveType.Cylinder, new Vector3(0.085f, 0.03f, 0.085f), new Color(0.88f, 0.86f, 0.8f), source, out _);
            Make(HandProp.Kit, PrimitiveType.Cube, new Vector3(0.2f, 0.07f, 0.13f), new Color(0.85f, 0.85f, 0.85f), source, out var kit);
            Add(kit, PrimitiveType.Cube, new Vector3(0.05f, 0.075f, 0.016f), new Vector3(0f, 0f, 0f), Quaternion.identity, new Color(0.75f, 0.1f, 0.1f), source);
            Add(kit, PrimitiveType.Cube, new Vector3(0.016f, 0.076f, 0.05f), new Vector3(0f, 0f, 0f), Quaternion.identity, new Color(0.75f, 0.1f, 0.1f), source);
            Make(HandProp.Bottle, PrimitiveType.Cylinder, new Vector3(0.065f, 0.1f, 0.065f), new Color(0.2f, 0.45f, 0.75f), source, out _);
            Make(HandProp.Pill, PrimitiveType.Cylinder, new Vector3(0.025f, 0.015f, 0.025f), new Color(0.9f, 0.6f, 0.15f), source, out _);
            Make(HandProp.Syringe, PrimitiveType.Cylinder, new Vector3(0.022f, 0.07f, 0.022f), new Color(0.7f, 0.85f, 0.9f), source, out _);
            _root.SetActive(false);
        }

        void Make(HandProp kind, PrimitiveType type, Vector3 scale, Color color, Material source, out GameObject holder)
        {
            holder = new GameObject(kind.ToString());
            holder.transform.SetParent(_root.transform, false);
            Add(holder, type, scale, Vector3.zero, Quaternion.identity, color, source);
            holder.SetActive(false);
            _kinds[(int)kind] = holder;
        }

        static void Add(GameObject parent, PrimitiveType type, Vector3 scale, Vector3 at, Quaternion turn, Color color, Material source)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = at; go.transform.localRotation = turn; go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            var m = new Material(source) { name = "HandProp", color = color };
            m.mainTexture = null;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.3f);
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public HandProp Shown => _shown;

        // `at`: the middle of the palm; `up`: the way the prop's top points (a bottle tips at the mouth).
        public void Show(HandProp kind, Vector3 at, Quaternion turn)
        {
            if (kind != _shown)
            {
                if (_kinds[(int)_shown] != null) _kinds[(int)_shown].SetActive(false);
                _shown = kind;
                if (_kinds[(int)kind] != null) _kinds[(int)kind].SetActive(true);
            }
            if (kind == HandProp.None) { if (_root.activeSelf) _root.SetActive(false); return; }
            if (!_root.activeSelf) _root.SetActive(true);
            _root.transform.SetPositionAndRotation(at, turn);
        }

        public void Hide() => Show(HandProp.None, Vector3.zero, Quaternion.identity);
        public void Destroy() { if (_root != null) Object.Destroy(_root); }
    }
}
