namespace QALab.Sandbox
{
    /// <summary>
    /// Seed switches for sandbox scripts. Inside <c>namespace QALab.Sandbox</c> the name <c>QALab</c>
    /// means our own root namespace, not the package's <c>MiawWorks.QALab.QALab</c> class, so the
    /// facade is called fully qualified here, once.
    /// </summary>
    public static class SandboxSeeds
    {
        /// <summary>True outside a QA Lab run (the sandbox is buggy when played normally), else -qalabSeeds decides.</summary>
        public static bool IsEnabled(string bugId) => MiawWorks.QALab.QALab.IsSeedEnabled(bugId);

        public static void RegisterPlayer(UnityEngine.Transform player, MiawWorks.QALab.IBotMover mover) =>
            MiawWorks.QALab.QALab.RegisterPlayer(player, mover);

        /// <summary>The registered player, or null (seed scripts check where it is).</summary>
        public static UnityEngine.Transform Player => MiawWorks.QALab.QALab.Player;
    }
}
