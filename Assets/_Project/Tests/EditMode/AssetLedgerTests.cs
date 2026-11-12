using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class AssetLedgerTests
    {
        const string Ledger =
            "| ID | 분류 | 이름 | 프로젝트 경로 |\n" +
            "|---|---|---|---|\n" +
            "| (예시) A-0001 | 텍스처 | Concrete | Assets/_Project/Art/Materials/Example |\n" +
            "| A-0002 | 텍스처 | Metal 01 | Assets/_Project/Art/Materials/Metal01/ |\n" +
            "| W-0001 | 무기 | Gun pack | `Assets/_Project/Art/Weapons` |\n";

        [Test]
        public void ListedFolder_IsNotReported()
        {
            var missing = AssetLedger.FindUnlisted(Ledger, new[] { "Assets/_Project/Art/Materials/Metal01" });
            CollectionAssert.IsEmpty(missing);
        }

        [Test]
        public void FolderUnderListedBundle_IsNotReported()
        {
            var missing = AssetLedger.FindUnlisted(Ledger, new[] { "Assets/_Project/Art/Weapons/Pistol" });
            CollectionAssert.IsEmpty(missing);
        }

        [Test]
        public void UnlistedFolder_IsReported()
        {
            var missing = AssetLedger.FindUnlisted(Ledger, new[] { "Assets/_Project/Audio/Sfx/Shot" });
            CollectionAssert.AreEqual(new[] { "Assets/_Project/Audio/Sfx/Shot" }, missing);
        }

        [Test]
        public void ExampleRow_DoesNotCount()
        {
            var missing = AssetLedger.FindUnlisted(Ledger, new[] { "Assets/_Project/Art/Materials/Example" });
            Assert.AreEqual(1, missing.Count);
        }

        [Test]
        public void RowBroaderThanACategory_CoversNothing()
        {
            var ledger = "| A-0009 | x | y | Assets/_Project/Art |\n| (예시) | x | y | Assets/_Project/Art/Materials |\n";
            var missing = AssetLedger.FindUnlisted(ledger, new[] { "Assets/_Project/Art/Materials/Metal01" });
            Assert.AreEqual(1, missing.Count);
        }

        [Test]
        public void SimilarPrefix_IsNotCovered()
        {
            var missing = AssetLedger.FindUnlisted(Ledger, new[] { "Assets/_Project/Art/Materials/Metal012" });
            Assert.AreEqual(1, missing.Count);
        }
    }
}
