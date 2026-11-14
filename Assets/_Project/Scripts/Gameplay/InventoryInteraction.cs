using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Gameplay
{
    internal enum InventoryDropKind { Bag, Ground, Weapon, Gear, Throwable, Attach }

    // A draggable, clickable thing of the inventory screen (a row, a slot, a card). Gives hover and press feedback on
    // its own plate (colour and a small press-in), and tells the screen about hover (tooltip), clicks and drags.
    internal sealed class InventoryDragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        const float FadeSpeed = 16f;
        static readonly Color HoverLift = new Color(0.05f, 0.055f, 0.06f, 0.06f);
        static readonly Color PressDim = new Color(-0.05f, -0.05f, -0.05f, 0f);

        InventoryScreen _owner;
        InventoryDragSource _source;
        Image _plate;
        Color _base;
        bool _baseKnown;
        float _hover, _press;
        bool _inside, _down;

        public void Configure(InventoryScreen owner, InventoryDragSource source)
        {
            _owner = owner;
            _source = source;
        }

        void EnsurePlate()
        {
            if (_baseKnown) return;
            _plate = GetComponent<Image>();
            if (_plate != null) _base = _plate.color;
            _baseKnown = true;
        }

        void Update()
        {
            EnsurePlate();
            bool hover = _inside && _owner != null && !_owner.Dragging && _source.IsValid;
            float hoverTarget = hover ? 1f : 0f, pressTarget = _down && hover ? 1f : 0f;
            if (Mathf.Approximately(_hover, hoverTarget) && Mathf.Approximately(_press, pressTarget)) return;
            float step = FadeSpeed * Time.unscaledDeltaTime;
            _hover = Mathf.MoveTowards(_hover, hoverTarget, step);
            _press = Mathf.MoveTowards(_press, pressTarget, step * 1.5f);
            if (_plate != null) _plate.color = _base + HoverLift * _hover + PressDim * _press;
            transform.localScale = Vector3.one * (1f - 0.02f * _press);
        }

        void OnDisable()
        {
            _inside = _down = false;
            _hover = _press = 0f;
            transform.localScale = Vector3.one;
            if (_plate != null && _baseKnown) _plate.color = _base;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_owner != null && _owner.BeginDrag(_source))
            {
                _owner.HideTooltip();
                _owner.MoveDrag(eventData.position, eventData.pressEventCamera);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_owner != null) _owner.MoveDrag(eventData.position, eventData.pressEventCamera);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _down = false;
            if (_owner != null) _owner.EndDrag(eventData.position, eventData.pressEventCamera);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_owner == null) return;
            if (_owner.Dragging && eventData.button == PointerEventData.InputButton.Right)
            {
                _owner.CancelDragFromPointer();
                return;
            }
            if (eventData.button == PointerEventData.InputButton.Right) _owner.QuickDrop(_source);
            else if (eventData.button == PointerEventData.InputButton.Left) _owner.Activate(_source, eventData.clickCount);
        }

        public void OnPointerDown(PointerEventData eventData) => _down = true;
        public void OnPointerUp(PointerEventData eventData) => _down = false;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _inside = true;
            if (_owner != null && !_owner.Dragging) _owner.ShowTooltip(_source, eventData.position, eventData.enterEventCamera);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _inside = false;
            if (_owner != null) _owner.HideTooltip();
        }
    }

    // A place something can be dropped. While a drag is on, it shows whether the dragged thing fits: big areas get a thin
    // accent outline with a faint wash (and a dimmed wash when it does not fit); an attachment slot (`slotLook`) is
    // washed green when it fits and pulses red when it does not.
    internal sealed class InventoryDropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        static readonly Color Fits = new Color(0.36f, 0.86f, 0.56f, 1f);

        InventoryScreen _owner;
        InventoryDropKind _kind;
        int _index;
        Image _overlay;
        Image[] _outline;
        bool _valid, _active, _hover, _slotLook;

        internal InventoryDropKind Kind => _kind;
        internal int Index => _index;
        internal bool ShowsInvalid => _active && !_valid && _slotLook;
        internal bool ShowsValid => _active && _valid;

        public void Configure(InventoryScreen owner, InventoryDropKind kind, int index, Image overlay, Image[] outline, bool slotLook = false)
        {
            _owner = owner;
            _kind = kind;
            _index = index;
            _overlay = overlay;
            _outline = outline;
            _slotLook = slotLook;
        }

        public void SetDragState(bool active, bool valid)
        {
            _active = active;
            _valid = active && valid;
            _hover = false;
            Paint();
        }

        static Color With(Color c, float a) => new Color(c.r, c.g, c.b, a);

        void Paint()
        {
            if (_overlay == null) return;
            bool show = _active && (_valid || _slotLook || _kind != InventoryDropKind.Attach);
            _overlay.enabled = show;
            if (_outline != null) foreach (var line in _outline) line.enabled = show && (_valid || _slotLook);
            if (!show) return;
            Color edge;
            if (_slotLook)
            {
                edge = _valid ? Fits : UiKit.Danger;
                _overlay.color = _valid ? With(Fits, _hover ? 0.5f : 0.3f) : With(UiKit.Danger, 0.3f);
            }
            else if (_valid)
            {
                edge = UiKit.Accent;
                _overlay.color = With(UiKit.Accent, _hover ? 0.1f : 0.035f);
            }
            else
            {
                edge = UiKit.Accent;
                _overlay.color = new Color(0f, 0f, 0f, 0.25f);
            }
            if (_outline != null) foreach (var line in _outline) line.color = With(edge, _valid && _hover ? 1f : 0.85f);
        }

        void Update()
        {
            // The red of a slot that does not fit pulses so it reads as "no" in a glance.
            if (_active && !_valid && _slotLook && _overlay != null && _overlay.enabled)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f);
                _overlay.color = With(UiKit.Danger, 0.2f + 0.4f * pulse);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true;
            if (_valid) Paint();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hover = false;
            if (_valid) Paint();
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (_owner == null || !_owner.Dragging) return;
            bool accepted = _owner.DropOn(_kind, _index, _owner.ActiveDragSource);
            if (accepted) _owner.MarkDropHandled();
            _owner.CancelDrag();
        }
    }
}
