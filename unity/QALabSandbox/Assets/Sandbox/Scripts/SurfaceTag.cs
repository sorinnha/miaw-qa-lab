using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>The footstep surface of a floor tile: Grass, Gravel, Metal or Wood.</summary>
    public sealed class SurfaceTag : MonoBehaviour
    {
        [SerializeField] private string surface = "Grass";

        public string Surface => surface;

        public void Set(string value) => surface = value;
    }
}
