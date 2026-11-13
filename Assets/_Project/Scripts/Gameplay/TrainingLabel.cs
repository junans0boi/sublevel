using UnityEngine;

namespace Game.Gameplay
{
    // A sign in the world that turns to face the camera (distance boards, area names).
    public class TrainingLabel : MonoBehaviour
    {
        public string Text = "";
        public float Size = 1f;
        public Color Color = Color.white;

        void Awake()
        {
            var mesh = gameObject.AddComponent<TextMesh>();
            mesh.text = Text;
            mesh.font = UiKit.Font;
            mesh.fontSize = 64;
            mesh.characterSize = 0.04f * Size;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = Color;
            GetComponent<MeshRenderer>().sharedMaterial = UiKit.Font.material;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var away = transform.position - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(away);
        }
    }
}
