using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-483: an island match held about ten scopes (optics rolled only at 'X' points). With the optic share every scope kind lies on the island,
    // and a scope fitted to a gun narrows the aim field of view.
    public class ScopeLootTests
    {
        static T Data<T>(string path) => JsonUtility.FromJson<T>(Resources.Load<TextAsset>("GameData/" + path).text);

        // The role mix of the baked World scene (LootSpawn_<role> names, r5.1): 423 W, 238 A, 168 H, 275 G, 158 X.
        static char[] IslandRoles() =>
            new string('W', 423).Concat(new string('A', 238)).Concat(new string('H', 168)).Concat(new string('G', 275)).Concat(new string('X', 158)).ToArray();

        [TestCase(1)] [TestCase(42)] [TestCase(2026)]
        public void Every_scope_kind_lies_on_the_island(int seed)
        {
            var table = Data<LootTable>("loot");
            var optics = Data<OpticRules>("optics");
            var plan = table.PlanByRole(IslandRoles(), seed);
            int weapons = plan.Count(e => e.Kind == LootKind.Weapon);
            LootGuarantee.ApplyShare(plan, table, 'O', LootKind.Optic, LootGuarantee.OpticShare, seed);
            Assert.AreEqual(weapons, plan.Count(e => e.Kind == LootKind.Weapon), "weapons are never turned");
            int scopes = 0;
            foreach (var def in optics.Optics.Where(d => d.Kind == OpticKind.Scope))
            {
                int n = plan.Count(e => e.Kind == LootKind.Optic && e.Optic == def.Id);
                Assert.Greater(n, 0, $"{def.Name} (id {def.Id}) seed {seed}");
                scopes += n;
            }
            Assert.GreaterOrEqual(scopes, 40, "scopes on an 80 player island");
        }

        [Test]
        public void A_fitted_scope_narrows_the_aim_fov()
        {
            var keep = OpticRules.Current;
            try
            {
                OpticRules.Current = Data<OpticRules>("optics");
                var rules = Data<GameRules>("rules");
                float sights = rules.Aim.Sights.Fov;
                foreach (var def in OpticRules.Current.Optics.Where(d => d.Kind == OpticKind.Scope))
                {
                    Assert.IsTrue(OpticRules.Current.Weapons.Any(w => Optics.CanAttach(w.WeaponId, def.Id)), $"{def.Name} fits some gun");
                    int packed = Attachments.With(0, AttachSlot.Optic, def.Id);
                    float fov = Optics.FovFor(sights, Optics.ZoomOf(Attachments.Get(packed, AttachSlot.Optic)));
                    Assert.Less(fov, sights * 0.6f, def.Name);
                }
            }
            finally { OpticRules.Current = keep; }
        }
    }
}
