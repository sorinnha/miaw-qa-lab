// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
namespace MiawWorks.QALab
{
    /// <summary>
    /// What any thread may know about the main thread: scene, frame and player position.
    /// <c>MainThreadCache</c> fills it every frame; tests use a fake. Reads never touch Unity APIs.
    /// </summary>
    public interface IMainThreadState
    {
        string Scene { get; }
        long Frame { get; }
        /// <summary>The player position [x, y, z], or null when no player is registered.</summary>
        float[] Position { get; }
    }
}
