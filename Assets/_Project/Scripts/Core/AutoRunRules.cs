namespace Game.Core
{
    // ADR-350: sprinting and auto-run (=) share one rule. Auto-run is always a sprint at the sprint speed; whatever stops a
    // sprint (aiming, firing, slow walk, healing, an outside hand on the body) also ends auto-run, so it never walks on its own.
    public static class AutoRunRules
    {
        public static bool CanSprint(bool hipAim, bool aimKeyDown, bool firing, bool slowWalk, bool externalControl, bool healing) =>
            hipAim && !aimKeyDown && !firing && !slowWalk && !externalControl && !healing;
    }
}
