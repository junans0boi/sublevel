using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The checklist of the training range (#153): a column at the left with every step ticked off as the player does it,
    // and one line at the top saying what to do next and where. Keys come from the live bindings.
    public class TrainingHud : MonoBehaviour
    {
        const float RowHeight = 30f, PanelWidth = 400f;
        static readonly Color Done = new Color(0.45f, 0.78f, 0.52f, 1f);

        Text _title, _next;
        Text[] _rows;
        int _shownDone = -1, _shownFirst = -2;

        void Awake()
        {
            var canvas = UiKit.Canvas("TrainingHud", 8, false);
            canvas.transform.SetParent(transform, false);
            var root = UiKit.Fill("Root", canvas.transform);
            int count = TrainingChecklist.Count;
            float height = 52f + count * RowHeight + 14f;
            var panel = UiKit.Rect("List", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(PanelWidth, height));
            UiKit.Box(panel, UiKit.Panel);
            _title = UiKit.Label(UiKit.Rect("Title", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -8f), new Vector2(PanelWidth - 32f, 36f)),
                "", 24, UiKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _rows = new Text[count];
            for (int i = 0; i < count; i++)
            {
                var row = UiKit.Rect("Row" + i, panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -52f - i * RowHeight), new Vector2(PanelWidth - 32f, RowHeight));
                _rows[i] = UiKit.Label(row, "", 20, UiKit.Text, TextAnchor.MiddleLeft);
            }
            var bar = UiKit.Rect("Next", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(1000f, 56f));
            UiKit.Box(bar, UiKit.Panel);
            _next = UiKit.Label(UiKit.Fill("Text", bar), "", 24, UiKit.Text, TextAnchor.MiddleCenter);
        }

        void Update()
        {
            var range = TrainingRange.Instance;
            if (range == null) return;
            var list = range.Checklist;
            int first = FirstOpen(list);
            if (list.DoneCount == _shownDone && first == _shownFirst) return;
            _shownDone = list.DoneCount;
            _shownFirst = first;
            _title.text = $"훈련장  {list.DoneCount} / {TrainingChecklist.Count}";
            for (int i = 0; i < _rows.Length; i++)
            {
                var step = (TrainingStep)i;
                bool done = list.IsDone(step);
                _rows[i].text = (done ? "[v] " : "[  ] ") + TrainingChecklist.Title(step);
                _rows[i].color = done ? Done : i == first ? UiKit.Accent : UiKit.Text;
            }
            _next.text = first < 0 ? "모두 완료했습니다. 자유롭게 연습하거나 Esc로 메인 메뉴로 나가세요." : TrainingChecklist.Title((TrainingStep)first) + "  -  " + Hint((TrainingStep)first);
        }

        static int FirstOpen(TrainingChecklist list)
        {
            for (int i = 0; i < TrainingChecklist.Count; i++)
                if (!list.IsDone((TrainingStep)i)) return i;
            return -1;
        }

        static string K(BindAction a) => KeyBinds.Name(a);

        // What to press and where to go, in short Korean sentences.
        public static string Hint(TrainingStep step)
        {
            switch (step)
            {
                case TrainingStep.Move: return $"{K(BindAction.MoveForward)} {K(BindAction.MoveLeft)} {K(BindAction.MoveBack)} {K(BindAction.MoveRight)}로 걷기";
                case TrainingStep.Sprint: return $"{K(BindAction.Sprint)}를 누른 채 앞으로 달리기";
                case TrainingStep.Crouch: return $"{K(BindAction.Crouch)}로 앉기 (다시 누르면 일어남)";
                case TrainingStep.Prone: return $"{K(BindAction.Prone)}로 엎드리기";
                case TrainingStep.Vault: return $"동쪽 코스의 낮은 상자로 달려가 {K(BindAction.Jump)}로 넘기";
                case TrainingStep.Ledge: return $"동쪽 높은 벽에 뛰어올라 턱을 잡고 {K(BindAction.Jump)}로 오르기";
                case TrainingStep.Swim: return "코스 끝 수영장에 들어가 헤엄치기";
                case TrainingStep.Pickup: return $"서쪽 테이블 아이템 앞에서 {K(BindAction.Interact)}로 줍기";
                case TrainingStep.Attach: return $"{K(BindAction.Inventory)} 가방에서 부착물을 총 칸으로 끌어 달기";
                case TrainingStep.Shoot: return $"총을 들고 {K(BindAction.Fire)}로 북쪽 사격장 표적 쏘기";
                case TrainingStep.Reload: return $"{K(BindAction.Reload)}로 재장전";
                case TrainingStep.Aim: return $"{K(BindAction.Aim)}를 눌러 조준경으로 조준";
                case TrainingStep.Zero: return "조준경을 든 채 PageUp / PageDown으로 영점 거리 바꾸기";
                case TrainingStep.Heal: return $"{K(BindAction.Heal)} 붕대·구급상자, {K(BindAction.Boost)} 음료·진통제로 회복";
                case TrainingStep.Throw: return $"{K(BindAction.Throw)}를 누르고 있다 떼서 던지기 ({K(BindAction.CycleThrowable)} 종류 바꾸기)";
                case TrainingStep.Vehicle: return $"남쪽 주차장 차량 곁에서 {K(BindAction.Interact)}로 타기";
                case TrainingStep.Door: return $"남서쪽 집 문 앞에서 {K(BindAction.Interact)}로 열기";
                default: return "";
            }
        }
    }
}
