using System.IO;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // 외형 tab roster cells show Resources/Ui/RosterThumbs/<ModelId>.png. A -nographics regen once committed all 15 as
    // flat grey, so the tab showed no characters (r5.1 playtest). Each picture must have a character in it.
    public class RosterThumbsTests
    {
        [Test]
        public void EveryRosterThumb_ShowsACharacter()
        {
            foreach (var id in PlayerAppearance.ModelIds)
            {
                string path = "Assets/_Project/Resources/Ui/RosterThumbs/" + id + ".png";
                Assert.IsTrue(File.Exists(path), path);
                var tex = new Texture2D(2, 2);
                Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(path)), path + " is a PNG");
                var px = tex.GetPixels32();
                var bg = px[0]; // top-left corner: the backdrop
                int differ = 0;
                foreach (var p in px)
                    if (Mathf.Abs(p.r - bg.r) + Mathf.Abs(p.g - bg.g) + Mathf.Abs(p.b - bg.b) > 30) differ++;
                Object.DestroyImmediate(tex);
                Assert.Greater(differ, px.Length / 10, id + ": the thumbnail is a flat picture with no character in it");
            }
        }
    }
}
