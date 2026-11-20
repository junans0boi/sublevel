using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum BoardPage { Countdown, BungeeTop, RangeTop, Regions, Tip }

    // The waiting island's extras as data (rules.json "Island", ADR-501/502): the range's loan and the board's rotation.
    [Serializable]
    public class IslandRules
    {
        public static IslandRules Current = new IslandRules();

        public int RangeWeaponId = 1;       // the booth's gun: the TrainingDummy's own pistol (VK-9)
        public int RangeRounds = 60;        // reserve lent with it, topped up while in the booth
        public float BoardPageSeconds = 6f; // each page of the board shows this long
        public float BoardFinalSeconds = 10f; // the last seconds always show the countdown
        public string[] BoardPages = { "Countdown", "BungeeTop", "RangeTop", "Regions", "Tip" };
        public string[] Tips =
        {
            "봉쇄 경보가 뜨면\n가까운 통로로 이동하세요",
            "엘리베이터 문이 열리면\n첫 사격 전까지 보호됩니다",
            "총소리는 멀리 퍼집니다\n봇도 소리를 듣고 다가옵니다",
            "엎드리면 탄이 덜 퍼집니다",
            "구급상자는 H 키로\n천천히 회복합니다",
            "다른 구역으로는\n엘리베이터를 타고 이동합니다",
            "사격장 부스에 들어가면\n연습용 권총을 빌려 줍니다",
            "섬에서 얻은 것은\n본 판으로 넘어가지 않습니다",
        };

        [NonSerialized] BoardPage[] _pages;
        [NonSerialized] string[] _pagesOf;
        // The parsed rotation, cached against the array it came from (no garbage per frame).
        public BoardPage[] Pages { get { if (_pages == null || !ReferenceEquals(_pagesOf, BoardPages)) { _pages = BoardRotation.Parse(BoardPages); _pagesOf = BoardPages; } return _pages; } }
    }

    // Which page the board shows (ADR-502, #422): a pure function of the countdown's seconds left, the number every client already gets
    // in WaitingBroadcast, so every client turns its board at the same moment without a message or a clock of its own. (When a full room
    // shortens the countdown the rotation jumps once with it.)
    public static class BoardRotation
    {
        static readonly BoardPage[] CountdownOnly = { BoardPage.Countdown };

        // Page names from data; unknown names are skipped, and an empty list leaves the countdown alone.
        public static BoardPage[] Parse(string[] names)
        {
            var list = new List<BoardPage>();
            if (names != null) foreach (var n in names) if (Enum.TryParse(n, out BoardPage p) && Enum.IsDefined(typeof(BoardPage), p)) list.Add(p);
            return list.Count > 0 ? list.ToArray() : CountdownOnly;
        }

        // `cycle` counts whole turns of the rotation (it picks the tip; it may be negative).
        public static BoardPage PageAt(float secondsLeft, IslandRules r, out int cycle)
        {
            cycle = 0;
            if (float.IsNaN(secondsLeft) || float.IsInfinity(secondsLeft)) return BoardPage.Countdown;
            var pages = r.Pages;
            long slot = (long)Math.Floor(-secondsLeft / Math.Max(1f, r.BoardPageSeconds));   // grows as the countdown runs down
            cycle = (int)Math.Floor(slot / (double)pages.Length);
            if (secondsLeft <= r.BoardFinalSeconds) return BoardPage.Countdown;
            return pages[(int)(((slot % pages.Length) + pages.Length) % pages.Length)];
        }

        public static string TipAt(int cycle, string[] tips) => tips == null || tips.Length == 0 ? "" : tips[((cycle % tips.Length) + tips.Length) % tips.Length];
    }

    // What the board needs to draw one page; everything comes from the server's broadcasts (or the map's region names).
    public struct BoardView
    {
        public float SecondsLeft; public int Total, Target; public float BungeeBest;
        public List<RecordRow> BungeeTop, RangeTop; public string[] Regions; public string Tip;
    }

    // The board's six lines for a page (Korean, no brands). Pure: the client hands the result to its text meshes.
    public static class BoardText
    {
        public const int Title = 0, Big = 1, Row0 = 2, Footer = 5, Lines = 6;

        public static string Clock(float seconds) { int s = Math.Max(0, (int)Math.Ceiling(seconds)); return $"{s / 60}:{s % 60:00}"; }

        public static void Compose(BoardPage page, in BoardView v, string[] lines)
        {
            for (int i = 0; i < Lines; i++) lines[i] = "";
            string departure = "출발까지 " + Clock(v.SecondsLeft) + "   ·   " + v.Total + " / " + v.Target;
            switch (page)
            {
                case BoardPage.Countdown:
                    lines[Title] = "출발까지";
                    lines[Big] = Clock(v.SecondsLeft);
                    lines[Row0 + 2] = v.Total + " / " + v.Target + " 탑승";
                    if (v.BungeeBest > 0f) lines[Footer] = $"번지 최고  {v.BungeeBest:0.0} m";
                    return;
                case BoardPage.BungeeTop: Top(lines, "번지 TOP 3", v.BungeeTop, "회"); break;
                case BoardPage.RangeTop: Top(lines, "사격 TOP 3", v.RangeTop, "점"); break;
                case BoardPage.Regions:
                    lines[Title] = "투입 지역 안내";
                    // The map's regions on two rows (the board fits about 14 Korean letters a row), then how the drop works.
                    int n = v.Regions != null ? v.Regions.Length : 0, half = (n + 1) / 2;
                    lines[Row0] = n > 0 ? string.Join("  ·  ", v.Regions, 0, half) : "지역 정보 수신 중";
                    lines[Row0 + 1] = n > half ? string.Join("  ·  ", v.Regions, half, n - half) : "";
                    lines[Row0 + 2] = "무작위 구역 투입  ·  차례로 봉쇄";
                    break;
                case BoardPage.Tip:
                    lines[Title] = "알아 두기";
                    var parts = (v.Tip ?? "").Split('\n');
                    int first = parts.Length == 1 ? Row0 + 1 : Row0;   // a one-line tip sits in the middle
                    for (int i = 0; i < parts.Length && i < 3; i++) lines[first + i] = parts[i];
                    break;
            }
            lines[Footer] = departure;
        }

        static void Top(string[] lines, string title, List<RecordRow> rows, string unit)
        {
            lines[Title] = title;
            if (rows == null || rows.Count == 0) { lines[Row0 + 1] = "아직 기록이 없습니다"; return; }
            for (int i = 0; i < rows.Count && i < 3; i++) lines[Row0 + i] = $"{i + 1}위   {rows[i].Name}   {rows[i].Score}{unit}";
        }
    }
}
