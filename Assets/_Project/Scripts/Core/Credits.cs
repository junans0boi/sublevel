namespace Game.Core
{
    // Assets whose licence asks for credit (CC BY 3.0/4.0): shown on the main menu's 크레딧 screen. Every ASSET_LEDGER row
    // that says credit is needed has an entry here (CreditsTests checks the ledger's source links against Url).
    public struct CreditEntry
    {
        public string Title, Author, Licence, Url, Use;
    }

    public static class Credits
    {
        const string CcBy = "CC BY 4.0";
        const string CcBy3 = "CC BY 3.0";

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
            new CreditEntry { Title = "Glock19_reload", Author = "Fr3yr", Licence = CcBy3 + " (수정함)", Use = "권총 재장전 소리",
                Url = "https://freesound.org/s/100804/" },
            new CreditEntry { Title = "Generic passenger car pack", Author = "comrade1280", Licence = CcBy, Use = "주차·폐차 소품",
                Url = "https://sketchfab.com/3d-models/generic-passenger-car-pack-20f9af9b8a404d5cb022ac6fe87f21f5" },
            new CreditEntry { Title = "motorcycle helmet classic", Author = "zeleniy89", Licence = CcBy, Use = "헬멧 Lv1",
                Url = "https://sketchfab.com/3d-models/motorcycle-helmet-classic-a4d4ef2f17c843828cf24d538a9edbd3" },
            new CreditEntry { Title = "MICH 2001 military helmet", Author = "HaizorWill", Licence = CcBy, Use = "헬멧 Lv2",
                Url = "https://sketchfab.com/3d-models/mich-2001-military-helmet-ad5907514a5343e3aca5ae0357b485a3" },
            new CreditEntry { Title = "Kiver-M / Military helmet", Author = "IDALGAME", Licence = CcBy, Use = "헬멧 Lv3",
                Url = "https://sketchfab.com/3d-models/kiver-m-military-helmet-1a72a5a3b01d400eb0bd8134f2fcf735" },
            new CreditEntry { Title = "Vest", Author = "Santeri Riikonen (eKross)", Licence = CcBy, Use = "조끼 Lv1",
                Url = "https://sketchfab.com/3d-models/vest-a1a17b557c464838b9e770d3d5099d54" },
            new CreditEntry { Title = "Black body armor", Author = "Dooms5555", Licence = CcBy + " (수정함)", Use = "조끼 Lv2",
                Url = "https://sketchfab.com/3d-models/black-body-armor-60379d7800794be9bb1bc0a961fd7666" },
            new CreditEntry { Title = "Combat Vest", Author = "csheffield", Licence = CcBy, Use = "조끼 Lv3",
                Url = "https://sketchfab.com/3d-models/combat-vest-ce92c014efd140deb6e918b3c59bf539" },
            new CreditEntry { Title = "Military Tactical Backpack", Author = "Liam3D (liamch)", Licence = CcBy, Use = "가방 Lv1",
                Url = "https://sketchfab.com/3d-models/military-tactical-backpack-9fa2da2c42234b58896e8d23393cac24" },
            new CreditEntry { Title = "Old Soviet Backpack", Author = "TwilightFox", Licence = CcBy, Use = "가방 Lv2",
                Url = "https://sketchfab.com/3d-models/old-soviet-backpack-bb89dfa7ec13478e85645497dfeafdcd" },
            new CreditEntry { Title = "A.L.I.C.E Backpack with LC-2 Frame Set", Author = "SomeDude (Patyot.Suttipreecha)", Licence = CcBy + " (수정함)", Use = "가방 Lv3",
                Url = "https://sketchfab.com/3d-models/alice-backpack-with-lc-2-frame-set-fbcbbb7fc57f47e3ac52e286fb324c37" },
        };

        // One line for the screen: "제목 — 작가 (라이선스) · 용도".
        public static string Line(CreditEntry e) => $"{e.Title} — {e.Author} ({e.Licence}) · {e.Use}";
    }
}
