using System;

namespace Game.Core
{
    // Fitting the posed body to a held edge (#312, ADR-542), no IK: the model is only moved back off the wall and up or
    // down so the hands reach the top. Measured in the character's own plane: `intoWall` is how far a point is past the
    // wall face (positive = inside it), `underTop` how far under the top of it.
    public static class LedgeFit
    {
        // How far the model must go back for a point of the body to keep `clearance` off the wall. A point over the top
        // is on it, not in the wall.
        public static float BackFor(float intoWall, float underTop, float clearance) =>
            underTop <= 0f ? 0f : Math.Max(0f, intoWall + clearance);

        // How far the model goes up (negative: down) so a shoulder `across` m from the hold horizontally and `rise` m
        // under it (negative: above it) reaches it with `reach` m of arm. Out of reach sideways it comes level with it.
        public static float Lift(float across, float rise, float reach, float maxUp, float maxDown)
        {
            float v = across >= reach ? 0f : (float)Math.Sqrt(reach * reach - across * across);
            float lift = rise > v ? rise - v : rise < -v ? rise + v : 0f;
            return Math.Min(maxUp, Math.Max(-maxDown, lift));
        }
    }
}
