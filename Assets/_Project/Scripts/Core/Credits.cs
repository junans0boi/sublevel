namespace Game.Core
{
    // Assets whose licence asks for credit (CC BY 4.0): shown on the main menu's 크레딧 screen. Every ASSET_LEDGER row
    // that says credit is needed has an entry here (CreditsTests checks the ledger's source links against Url).
    public struct CreditEntry
    {
        public string Title, Author, Licence, Url, Use;
    }

    public static class Credits
    {
        const string CcBy = "CC BY 4.0";

        public static readonly CreditEntry[] Entries =
        {
            new CreditEntry { Title = "Fairheaven LT '80 - Low Poly Model", Author = "DanielZhabotinsky", Licence = CcBy, Use = "차량(세단)",
                Url = "https://sketchfab.com/3d-models/fairheaven-lt-80-low-poly-model-e2678da920cc4be68dbc193727919ffb" },
            new CreditEntry { Title = "Fairheaven SW '84 - Low Poly Model", Author = "DanielZhabotinsky", Licence = CcBy, Use = "차량(왜건)",
                Url = "https://sketchfab.com/3d-models/fairheaven-sw-84-low-poly-model-aa0becb6e854422596cac6b21bf79787" },
            new CreditEntry { Title = "Lightbody Lifted '85 - Low Poly Model", Author = "DanielZhabotinsky", Licence = CcBy, Use = "차량(픽업)",
                Url = "https://sketchfab.com/3d-models/lightbody-lifted-85-low-poly-model-67beae18c3d24be68f9c8f0ec382d8e3" },
            new CreditEntry { Title = "Shvan '92 - Low Poly Model", Author = "DanielZhabotinsky", Licence = CcBy, Use = "차량(밴)",
                Url = "https://sketchfab.com/3d-models/shvan-92-low-poly-model-09d718c9cf72401b8534d265a06a803f" },
            new CreditEntry { Title = "Fictional supercar - V12 Goblin", Author = "ollitei", Licence = CcBy + " (수정함)", Use = "차량(스포츠카)",
                Url = "https://sketchfab.com/3d-models/fictional-supercar-v12-goblin-0a20e49ad5774d778567cb5c3f345786" },
            new CreditEntry { Title = "Mid poly dirt bike", Author = "danilloribeiro", Licence = CcBy, Use = "오토바이(오프로드)",
                Url = "https://sketchfab.com/3d-models/mid-poly-dirt-bike-3009c80ec6cc47fcaad27cf96f2a0822" },
            new CreditEntry { Title = "Motorcycle", Author = "animanyarty", Licence = CcBy, Use = "오토바이(로드)",
                Url = "https://sketchfab.com/3d-models/motorcycle-38404e2077ca4b209cd2f1db30541b94" },
            new CreditEntry { Title = "Generic passenger car pack", Author = "comrade1280", Licence = CcBy, Use = "주차·폐차 소품",
                Url = "https://sketchfab.com/3d-models/generic-passenger-car-pack-20f9af9b8a404d5cb022ac6fe87f21f5" },
        };

        // One line for the screen: "제목 — 작가 (라이선스) · 용도".
        public static string Line(CreditEntry e) => $"{e.Title} — {e.Author} ({e.Licence}) · {e.Use}";
    }
}
