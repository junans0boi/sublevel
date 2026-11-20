using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // 친구 · 파티 drawer of the main menu (#391), opened by the bell. Same vocabulary as the settings (UiPaint: Sf panel, Ln ring, Ac accent).
    // It draws SocialClient's snapshots; every change goes through the backend and comes back with the next poll.
    public sealed class FriendsPanel
    {
        // Between the lobby's top bar (96) and its bottom console (~930) so it never covers 시작.
        const float W = 560f, H = 816f, Top = 104f, Pad = 24f, Row = 52f;

        readonly SocialClient _social;
        readonly RectTransform _root;
        RectTransform _rows;
        InputField _nick;
        Text _error;

        public bool Shown => _root.gameObject.activeSelf;
        public RectTransform Root => _root;

        FriendsPanel(SocialClient social, RectTransform root) { _social = social; _root = root; }

        public static FriendsPanel Build(Transform parent, SocialClient social)
        {
            var root = UiKit.Rect("FriendsPanel", parent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -Top), new Vector2(W, H));
            var solid = UiPaint.PnSolid; solid.a = 1f; // opaque: the cards behind must not show through
            UiPaint.Box(root, solid, 14, UiPaint.Ln);
            var p = new FriendsPanel(social, root);
            UiPaint.Label(root, "Title", Pad, 20f, 380f, 40f, "친구 · 파티", 28, UiPaint.Tx, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiPaint.Btn(root, "Close", W - Pad - 44f, 20f, 44f, 40f, "×", 26, 8, UiPaint.Sf, UiPaint.Ln, UiPaint.T2, () => p.Show(false));
            var box = UiPaint.At(root, "NickBox", Pad, 76f, 370f, 48f);
            var image = UiPaint.Box(box, UiPaint.Bg, 8, UiPaint.Ln);
            image.raycastTarget = true;
            var text = UiKit.Label(UiKit.Fill("Text", box, 12f), "", 22, UiPaint.Tx, TextAnchor.MiddleLeft);
            p._nick = box.gameObject.AddComponent<InputField>();
            p._nick.targetGraphic = image; p._nick.textComponent = text; p._nick.characterLimit = 16;
            UiPaint.Btn(root, "Add", Pad + 382f, 76f, W - 2 * Pad - 382f, 48f, "친구 추가", 20, 8, UiPaint.AcS, UiPaint.Ac, UiPaint.Tx, p.OnAdd);
            p._error = UiPaint.Label(root, "Error", Pad, H - 52f, W - 2 * Pad, 32f, "", 18, UiPaint.Wr);
            p._rows = UiPaint.At(root, "Rows", 0f, 140f, W, H - 200f);
            social.Changed += p.Redraw;
            root.gameObject.SetActive(false);
            return p;
        }

        public void Show(bool on) { _root.gameObject.SetActive(on); if (on) Redraw(); }
        public void Toggle() => Show(!Shown);

        void OnAdd()
        {
            string n = _nick.text.Trim();
            if (n.Length < 2) return;
            _social.AddFriend(n);
            _nick.text = "";
        }

        void Redraw()
        {
            if (!Shown) return;
            _error.text = _social.LastError;
            foreach (Transform c in _rows) Object.Destroy(c.gameObject);
            float y = 0f;
            long me = LobbyNet.AccountId;
            bool leader = _social.IsLeader(me);
            var inv = _social.Invites.invites;
            if (inv.Length > 0) Section(ref y, "파티 초대");
            foreach (var i in inv)
            {
                long id = i.party_id;
                Line(ref y, i.from.nickname + " 님의 초대", null, ("수락", () => _social.AcceptInvite(id)), ("거절", () => _social.DeclineInvite(id)));
            }
            if (_social.Party != null)
            {
                Section(ref y, "파티 " + _social.PartySize + "/" + SocialRules.MaxPartySize);
                foreach (var m in _social.Party.members) Line(ref y, (m.account_id == _social.Party.leader_id ? "★ " : "") + m.nickname + (m.account_id == me ? " (나)" : ""), SocialRules.StatusText(m.status));
                Line(ref y, "", null, ("파티 나가기", _social.LeaveParty));
            }
            var friends = _social.Friends;
            if (friends.incoming.Length > 0) Section(ref y, "친구 요청");
            foreach (var f in friends.incoming) { long id = f.account_id; Line(ref y, f.nickname, null, ("수락", () => _social.AcceptFriend(id)), ("거절", () => _social.RemoveFriend(id))); }
            Section(ref y, "친구 " + friends.friends.Length);
            if (friends.friends.Length == 0) Line(ref y, "아직 친구가 없다. 닉네임으로 추가하자.", null);
            foreach (var f in friends.friends)
            {
                long id = f.account_id;
                if (leader && f.status != "offline") Line(ref y, f.nickname, SocialRules.StatusText(f.status), ("초대", () => _social.Invite(id)), ("삭제", () => _social.RemoveFriend(id)));
                else Line(ref y, f.nickname, SocialRules.StatusText(f.status), ("삭제", () => _social.RemoveFriend(id)));
            }
        }

        void Section(ref float y, string title)
        {
            UiPaint.Label(_rows, "Section", Pad, y + 8f, W - 2 * Pad, 28f, title, 18, UiPaint.T2, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 40f;
        }

        void Line(ref float y, string text, string state, params (string label, UnityEngine.Events.UnityAction act)[] buttons)
        {
            float x = W - Pad;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                float bw = buttons[i].label.Length > 3 ? 120f : 76f;
                x -= bw;
                UiPaint.Btn(_rows, "Act", x, y + 6f, bw, 40f, buttons[i].label, 18, 8, UiPaint.Sf, UiPaint.Ln, UiPaint.Tx, buttons[i].act);
                x -= 8f;
            }
            UiPaint.Label(_rows, "Name", Pad, y, Mathf.Max(80f, x - Pad - 90f), Row, text, 22, UiPaint.Tx, TextAnchor.MiddleLeft);
            if (state != null) UiPaint.Label(_rows, "State", x - 90f, y, 90f, Row, state, 18, state == "오프라인" ? UiPaint.T2 : UiPaint.Ok, TextAnchor.MiddleRight);
            y += Row;
        }
    }
}
