namespace Game.Core
{
    /// <summary>Pure C# marker used to prove the Core assembly compiles and is testable without UnityEngine.</summary>
    public static class Smoke
    {
        public static int Add(int a, int b) => a + b;
    }
}
