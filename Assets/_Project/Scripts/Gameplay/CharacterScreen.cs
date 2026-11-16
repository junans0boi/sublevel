using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // 외형 (adopted mockup, Resources/Ui/Screens/char.json): the chrome and the roster grid are the measured mockup,
    // laid over the real 3D lobby with the camera in close on the local character (LobbyStage). All 15 roster models are
    // selectable (no locked slots), the colour variants come from the chosen model's CharacterDef, gear tiers are chosen
    // below. Every change is applied to the 3D character at once and saved as the PlayerAppearance (PlayerAppearanceStore);
    // 취소 restores the look the screen was opened with.
    public class CharacterScreen
    {
        public const int Total = 15;

        public MockScreen Screen { get; private set; }
        public PlayerAppearance Look { get; private set; }
        public string HeaderText => _header != null ? _header.text : "";
        public string NameText => _name != null ? _name.text : "";
        public string VariantText => _colorLabel != null ? _colorLabel.text : "";
        public int VariantCount { get; private set; }

        MainMenu _menu;
        Text _header, _name, _colorLabel, _desc;
        readonly List<Cell> _cells = new List<Cell>();
        readonly List<GameObject> _variantObjects = new List<GameObject>();
        readonly List<Image[]> _variantRings = new List<Image[]>();
        readonly List<Text> _gearTexts = new List<Text>();
        readonly List<Image> _gearFill = new List<Image>();
        PlayerAppearance _entry;

        class Cell { public Image Fill, Ring; public float X, Y; }

        public RectTransform Build(Transform parent, MainMenu menu)
        {
            _menu = menu;
            // The preview picture, its caption and the colour swatches are replaced: the preview is the 3D character, the
            // swatches follow the chosen model's variants.
            Screen = MockScreen.Build(parent, "char", OnClick, it => Drop(it), false, 0f);
            // The 24 mockup cells: the first 15 become the roster, the other 9 are not drawn (nothing is locked).
            var cellBoxes = new List<MockScreen.Item>();
            foreach (var kv in Screen.Rects)
                if (kv.Key.k == "box" && kv.Key.id != null && kv.Key.id.StartsWith("ro")) cellBoxes.Add(kv.Key);
            cellBoxes.Sort((a, b) => int.Parse(a.id.Substring(2)).CompareTo(int.Parse(b.id.Substring(2))));
            foreach (var it in Screen.Items)
            {
                if (it.k == "text" && it.t != null && it.t.StartsWith("외형 ") && it.r[0] > 1300f) _header = Screen.Rects[it].GetComponent<Text>();
                else if (it.k == "text" && it.fs == 21f && it.r[0] > 1300f && it.r[1] > 150f) _name = Screen.Rects[it].GetComponent<Text>();
                else if (it.k == "text" && it.t != null && it.t.StartsWith("색 변형")) _colorLabel = Screen.Rects[it].GetComponent<Text>();
                else if (it.k == "text" && it.t != null && it.t.StartsWith("섬 설비를")) _desc = Screen.Rects[it].GetComponent<Text>();
                else if (it.k == "text" && it.t != null && it.t.StartsWith("기본 12 +")) Screen.Rects[it].GetComponent<Text>().text = "전체 15종";
                else if (it.k == "text" && it.t != null && it.t.StartsWith("외형 로스터")) Screen.Rects[it].GetComponent<Text>().text = "외형 로스터 · 15종";
            }
            for (int k = 0; k < cellBoxes.Count; k++)
            {
                var box = cellBoxes[k];
                var rect = Screen.Rects[box];
                var c = new Cell { Fill = rect.GetComponent<Image>(), X = box.r[0], Y = box.r[1] };
                c.Ring = rect.Find("Ring") != null ? rect.Find("Ring").GetComponent<Image>() : null;
                _cells.Add(c);
                // Everything the mockup drew inside this cell: number, name, lock icon, check.
                foreach (var it in Screen.Items)
                {
                    if (it == box) continue;
                    bool inside = it.r[0] >= box.r[0] - 1f && it.r[1] >= box.r[1] - 1f && it.r[0] + it.r[2] <= box.r[0] + box.r[2] + 1f && it.r[1] + it.r[3] <= box.r[1] + box.r[3] + 1f;
                    if (!inside || it.r[0] < 48f || it.r[0] > 560f) continue;
                    var r = Screen.Rects[it];
                    if (k >= Total) { r.gameObject.SetActive(false); continue; }
                    if (it.k == "text" && it.fs == 14f) r.GetComponent<Text>().text = (k + 1).ToString("00");
                    else if (it.k == "text" && it.fs == 16f) { r.GetComponent<Text>().text = PlayerAppearance.ModelNames[k]; Center(r, box); }
                    else if (it.k == "text" || it.k == "img") r.gameObject.SetActive(false); // lock icon, check mark
                }
                if (k >= Total) rect.gameObject.SetActive(false);
            }
            BuildGear();
            return Screen.Root;
        }

        // Model names have other widths than the mockup's: centre them under the cell.
        static void Center(RectTransform r, MockScreen.Item cell)
        {
            r.anchoredPosition = new Vector2(cell.r[0], r.anchoredPosition.y);
            r.sizeDelta = new Vector2(cell.r[2], r.sizeDelta.y);
            var t = r.GetComponent<Text>();
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.fontSize = 14;
        }

        static bool Drop(MockScreen.Item it)
        {
            if (it.k == "img" && it.r[2] == 260f && it.r[3] == 520f) return true;                 // 2D preview figure
            if (it.k == "text" && it.t != null && it.t.StartsWith("끌어서 돌려")) return true;      // its caption
            if (it.id != null && it.id.StartsWith("col")) return true;                              // colour swatch buttons
            if (it.k == "box" && it.r[2] == 54f && it.r[3] == 54f) return true;                     // their discs
            if (it.k == "text" && it.r[1] >= 325f && it.r[1] <= 335f && it.r[0] > 1360f && it.fs == 16f) return true; // their captions
            if (it.k == "text" && it.t != null && it.t.StartsWith("팀 구분은")) return true;       // note below the swatches (moved below)
            return false;
        }

        // 헬멧 / 조끼 / 배낭 tiers (0..3) under the colour row.
        void BuildGear()
        {
            string[] names = { "헬멧", "조끼", "배낭" };
            for (int g = 0; g < 3; g++)
            {
                float y = 560f + g * 80f;
                UiPaint.Label(Screen.Stage, "GearName" + g, 1371, y, 120, 60, names[g], 22, UiPaint.Tx, TextAnchor.MiddleLeft);
                for (int t = 0; t < 4; t++)
                {
                    int gear = g, tier = t;
                    var b = UiPaint.Btn(Screen.Stage, "Gear" + g + t, 1500 + t * 90f, y, 80, 60, t == 0 ? "없음" : t + "단계", 18, 30, UiPaint.Pn, UiPaint.Ln, UiPaint.Tx, () => SetGear(gear, tier));
                    _gearFill.Add(b.targetGraphic as Image);
                    _gearTexts.Add(b.GetComponentInChildren<Text>());
                }
            }
        }

        // Called when the screen opens: the stage zooms to the local character and the saved look is shown.
        public void Open()
        {
            _entry = PlayerAppearanceStore.Load();
            Look = _entry;
            Refresh();
            if (_menu.Stage != null) _menu.Stage.SetCloseup(true);
        }

        public void Close() { if (_menu.Stage != null) _menu.Stage.SetCloseup(false); }

        public void SelectModel(int index)
        {
            if (index < 0 || index >= Total) return;
            Look = new PlayerAppearance { ModelId = PlayerAppearance.ModelIds[index], Variant = 0, Helmet = Look.Helmet, Vest = Look.Vest, Pack = Look.Pack }.Normalized();
            Commit();
        }

        public void SetVariant(int v)
        {
            if (v < 0 || v >= VariantCount) return;
            Look = new PlayerAppearance { ModelId = Look.ModelId, Variant = v, Helmet = Look.Helmet, Vest = Look.Vest, Pack = Look.Pack }.Normalized();
            Commit();
        }

        public void SetGear(int gear, int tier)
        {
            var l = Look;
            if (gear == 0) l.Helmet = tier; else if (gear == 1) l.Vest = tier; else l.Pack = tier;
            Look = l.Normalized();
            Commit();
        }

        // Saves, shows the new look on the 3D character at once and redraws this screen.
        void Commit()
        {
            PlayerAppearanceStore.Save(Look);
            if (_menu.Lobby != null && _menu.Panel != null) _menu.Panel.SyncStage();
            Refresh();
        }

        void Refresh()
        {
            int idx = Look.ModelIndex;
            for (int k = 0; k < _cells.Count && k < Total; k++)
            {
                bool on = k == idx;
                if (_cells[k].Fill != null) _cells[k].Fill.color = on ? UiPaint.AcS : UiPaint.Bg;
                if (_cells[k].Ring != null) { _cells[k].Ring.color = on ? UiPaint.Ac : UiPaint.Ln; _cells[k].Ring.sprite = UiPaint.Round(16, on ? 2 : 1); }
            }
            if (_header != null) _header.text = "외형 " + (idx + 1).ToString("00") + " / " + Total;
            if (_name != null) _name.text = PlayerAppearance.ModelNames[idx];
            if (_desc != null)
            {
                // A fixed three-line block (line height 1.5, 24 px above the buttons, ellipsis by truncation) so it never
                // runs into the colour label above it.
                _desc.text = "고르면 로비와 게임 속 내 캐릭터가 같이 바뀝니다. 장비는 시작할 때만 입고, 매치 중에는 주운 장비가 우선입니다.";
                _desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                _desc.verticalOverflow = VerticalWrapMode.Truncate;
                _desc.alignment = TextAnchor.UpperLeft;
                _desc.fontSize = 18;
                _desc.lineSpacing = 1.5f;
                var dr = (RectTransform)_desc.transform;
                dr.sizeDelta = new Vector2(466f, 84f);
                dr.anchoredPosition = new Vector2(1371f, -(897f - 24f - 84f));
            }
            BuildVariants(idx);
            for (int g = 0; g < 3; g++)
            {
                int cur = g == 0 ? Look.Helmet : g == 1 ? Look.Vest : Look.Pack;
                for (int t = 0; t < 4; t++)
                {
                    bool on = cur == t;
                    _gearFill[g * 4 + t].color = on ? UiPaint.AcS : UiPaint.Pn;
                    _gearTexts[g * 4 + t].color = on ? UiPaint.Tx : UiPaint.T2;
                }
            }
        }

        // The variant buttons of the chosen model (swatch = the model's material colour, label = the variant's name).
        void BuildVariants(int idx)
        {
            foreach (var o in _variantObjects) if (o != null) UnityEngine.Object.Destroy(o);
            _variantObjects.Clear();
            var stage = _menu.Stage;
            CharacterVariant[] variants = null;
            if (stage != null && stage.RosterPrefabs != null && idx < stage.RosterPrefabs.Length && stage.RosterPrefabs[idx] != null)
            {
                var def = stage.RosterPrefabs[idx].GetComponent<CharacterDef>();
                if (def != null) variants = def.Variants;
            }
            VariantCount = variants != null ? variants.Length : 1;
            int cur = Mathf.Clamp(Look.Variant, 0, VariantCount - 1);
            if (_colorLabel != null)
                _colorLabel.text = "색 변형 · " + (variants != null && cur < variants.Length && !string.IsNullOrEmpty(variants[cur].Name) ? variants[cur].Name : "기본");
            for (int v = 0; v < VariantCount; v++)
            {
                int vi = v;
                float x = 1371f + (v % 6) * 82f, y = 270f + (v / 6) * 100f;
                var holder = UiPaint.At(Screen.Stage, "Variant" + v, x, y, 66, 82);
                _variantObjects.Add(holder.gameObject);
                var btn = holder.gameObject.AddComponent<Image>();
                btn.color = Color.clear; btn.raycastTarget = true;
                holder.gameObject.AddComponent<Button>().onClick.AddListener(() => SetVariant(vi));
                var disc = UiPaint.At(holder, "Disc", 6, 0, 54, 54);
                UiPaint.Box(disc, SwatchColor(variants, v), 27, v == cur ? UiPaint.Tx : UiPaint.Ln, 3);
                UiPaint.Label(holder, "No", 0, 58, 66, 22, (v + 1).ToString(), 16, UiPaint.T2, TextAnchor.MiddleCenter);
            }
        }

        static Color SwatchColor(CharacterVariant[] variants, int v)
        {
            if (variants == null || v >= variants.Length || variants[v].Materials == null) return UiPaint.T2;
            foreach (var m in variants[v].Materials)
            {
                if (m == null) continue;
                var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                var tint = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                if (tex != null) { var a = Average(tex); return new Color(a.r * tint.r, a.g * tint.g, a.b * tint.b, 1f); }
                if (tint.a > 0.5f) return new Color(tint.r, tint.g, tint.b, 1f);
            }
            return UiPaint.T2;
        }

        // Average colour of a texture (blitted to 1x1 on the GPU): the swatch of a variant whose look is in its texture.
        static readonly Dictionary<Texture, Color> AverageCache = new Dictionary<Texture, Color>();
        static Color Average(Texture tex)
        {
            if (AverageCache.TryGetValue(tex, out var cached)) return cached;
            var rt = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var c = t.GetPixel(0, 0);
            UnityEngine.Object.Destroy(t);
            c.a = 1f;
            AverageCache[tex] = c;
            return c;
        }

        public void Cancel()
        {
            Look = _entry;
            PlayerAppearanceStore.Save(_entry);
            if (_menu.Panel != null) _menu.Panel.SyncStage();
            _menu.Back();
        }

        public void Apply() => _menu.Back();

        public void Step(int delta) => SelectModel(Mathf.Clamp(Look.ModelIndex + delta, 0, Total - 1));

        void OnClick(MockScreen.Item it)
        {
            string a = !string.IsNullOrEmpty(it.go) ? it.go : it.act;
            if (a != null && a.StartsWith("ch:")) { SelectModel(int.Parse(a.Substring(3))); return; }
            if (it.id == "apply") { Apply(); return; }
            if (it.id == "cancel") { Cancel(); return; }
            if (it.id == "topchar") return; // already here
            _menu.OnMock(it);
        }

        public void Tick()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.leftArrowKey.wasPressedThisFrame) Step(-1);
            if (kb.rightArrowKey.wasPressedThisFrame) Step(1);
            if (kb.enterKey.wasPressedThisFrame) Apply();
            if (kb.escapeKey.wasPressedThisFrame) Cancel();
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed && _menu.Stage != null && !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                _menu.Stage.Spin(-mouse.delta.ReadValue().x * 0.4f);
        }
    }
}
