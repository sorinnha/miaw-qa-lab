using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Implemented by the game's player so the bot (M4) can drive it, and registered with
    /// <see cref="QALab.RegisterPlayer"/>. In M1 the registration only gives events a position.
    /// </summary>
    public interface IBotMover
    {
        /// <summary>Called every frame while moving towards <paramref name="worldTarget"/>.</summary>
        void MoveTowards(Vector3 worldTarget);

        void Stop();

        /// <summary>Interact with the nearest interactable, if any; returns its name.</summary>
        bool TryInteract(out string objectName);

        void Respawn();
    }
}
