using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // One line of the settings screen (a setting or a read-only key binding). Hovering it shows its description in the
    // screen's side panel; the screen fills in the controls its kind uses.
    public sealed class SettingsRow : MonoBehaviour, IPointerEnterHandler
    {
        public SettingDef Def { get; internal set; }       // null for key-list rows
        public KeyBinding Binding { get; internal set; }   // key-list rows only

        public string Label => Def != null ? Def.Label : Binding != null ? Binding.Label : "";
        public string Description => Def != null ? Def.Description : Binding != null ? Binding.Description : "";

        public Slider Slider { get; internal set; }      // sliders
        public Text Value { get; internal set; }         // value text of sliders, choices and key rows
        public Button KeyButton { get; internal set; }   // key rows: press, then the new key
        public Button Previous { get; internal set; }    // choices: ◀
        public Button Next { get; internal set; }        // choices: ▶
        public Button Off { get; internal set; }         // toggles
        public Button On { get; internal set; }

        internal SettingsScreen Owner;
        internal Image Background;
        internal Image Marker;
        internal Image[] Pips;
        internal string[] SliderTexts;
        internal Color BaseColor;

        void Update()
        {
            if (Owner != null && Binding != null) Owner.PollRebind(this);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Owner != null) Owner.Focus(this);
        }
    }
}
