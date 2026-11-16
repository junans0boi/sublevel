using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #249 T8 facade: windows in a rhythm, bands and cornice, balconies, an entrance canopy (awning and blank sign on a shop), roof tank and antenna.
    public class BlockFacadeTests
    {
        static BuildingPlan Make(BuildingKind kind, int seed = 3)
        {
            Buildings.FootprintLimits(kind, out float w0, out float d0, out float w1, out float d1);
            return Buildings.Generate(kind, (w0 + w1) / 2f, (d0 + d1) / 2f, seed);
        }

        [TestCase(BuildingKind.Shop)] [TestCase(BuildingKind.Villa)] [TestCase(BuildingKind.Apartment)]
        public void Every_block_has_floor_bands_a_cornice_a_parapet_cap_and_a_roof_tank(BuildingKind kind)
        {
            var plan = Make(kind);
            Assert.AreEqual(plan.Storeys, plan.Pieces.Count(p => p.Name.StartsWith("BandFront")), "a band on the front at every floor and the cornice");
            Assert.IsTrue(plan.Pieces.Any(p => p.Name == "CapFront"));
            Assert.IsTrue(plan.Pieces.Any(p => p.Name == "TankBody"));
            Assert.IsTrue(plan.Pieces.Any(p => p.Name == "AntennaPole"));
        }

        [Test]
        public void A_shop_has_an_awning_and_a_blank_sign_panel_and_no_balconies()
        {
            var plan = Make(BuildingKind.Shop);
            Assert.IsTrue(plan.Pieces.Any(p => p.Name == "AwningFront"));
            Assert.IsTrue(plan.Pieces.Any(p => p.Name == "SignPanel"));
            Assert.IsFalse(plan.Pieces.Any(p => p.Name.StartsWith("Balcony")));
        }

        [TestCase(BuildingKind.Villa)] [TestCase(BuildingKind.Apartment)]
        public void Villas_and_apartments_have_balconies_and_an_entrance_canopy(BuildingKind kind)
        {
            var plan = Make(kind);
            Assert.GreaterOrEqual(plan.Pieces.Count(p => p.Name.StartsWith("Balcony") && !p.Name.StartsWith("BalconyRail")), 2);
            Assert.IsTrue(plan.Pieces.Any(p => p.Name == "CanopyFront"));
        }

        [Test]
        public void Windows_sit_in_a_rhythm_the_same_on_every_floor()
        {
            var plan = Make(BuildingKind.Apartment);
            var frontAt = plan.Windows.Where(w => w.Storey == 1 && System.Math.Abs(w.Z) > plan.Depth / 2f - 0.3f).Select(w => System.Math.Round(w.X, 1)).OrderBy(x => x).ToArray();
            var upperAt = plan.Windows.Where(w => w.Storey == 2 && System.Math.Abs(w.Z) > plan.Depth / 2f - 0.3f).Select(w => System.Math.Round(w.X, 1)).OrderBy(x => x).ToArray();
            Assert.Greater(frontAt.Length, 3);
            CollectionAssert.AreEqual(frontAt, upperAt, "bays line up from floor to floor");
        }

        [TestCase(BuildingKind.Shop)] [TestCase(BuildingKind.Villa)] [TestCase(BuildingKind.Apartment)]
        public void Attachments_stand_out_at_most_the_rule_allows_and_the_pieces_stay_in_budget(BuildingKind kind)
        {
            var plan = Make(kind);
            float hw = plan.Width / 2f, hd = plan.Depth / 2f;
            foreach (var p in plan.Pieces.Where(p => p.Name.StartsWith("Balcony") || p.Name.StartsWith("Canopy") || p.Name.StartsWith("Awning") || p.Name.StartsWith("Sign")))
                Assert.LessOrEqual(System.Math.Max(System.Math.Max(p.MaxX - hw, -hw - p.MinX), System.Math.Max(p.MaxZ - hd, -hd - p.MinZ)), BuildingRules.MaxAttachment + 0.01f, p.Name);
            Assert.LessOrEqual(plan.Pieces.Count, BuildingRules.MaxPieces(kind));
        }
    }
}
