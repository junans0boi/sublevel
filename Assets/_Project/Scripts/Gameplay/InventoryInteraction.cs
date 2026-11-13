using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Gameplay
{
    internal enum InventoryDropKind { Bag, Ground, Weapon, Gear }

    internal sealed class InventoryDragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        InventoryScreen _owner;
        InventoryDragSource _source;

        public void Configure(InventoryScreen owner, InventoryDragSource source)
        {
            _owner = owner;
            _source = source;
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

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_owner != null && !_owner.Dragging) _owner.ShowTooltip(_source, eventData.position, eventData.enterEventCamera);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_owner != null) _owner.HideTooltip();
        }
    }

    internal sealed class InventoryDropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        InventoryScreen _owner;
        InventoryDropKind _kind;
        int _index;
        Image _overlay;
        bool _valid;

        internal InventoryDropKind Kind => _kind;
        internal int Index => _index;

        public void Configure(InventoryScreen owner, InventoryDropKind kind, int index, Image overlay)
        {
            _owner = owner;
            _kind = kind;
            _index = index;
            _overlay = overlay;
        }

        public void SetDragState(bool active, bool valid)
        {
            _valid = active && valid;
            if (_overlay == null) return;
            _overlay.enabled = active;
            _overlay.color = valid ? new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.18f) : new Color(0f, 0f, 0f, 0.42f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_overlay != null && _valid)
                _overlay.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.34f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_overlay != null && _valid)
                _overlay.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.18f);
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
