using UnityEngine;

namespace MiawWorks.QALab.Tests
{
    /// <summary>A stand-in game script for ProjectScannerTests: one object field to leave empty or break.</summary>
    public sealed class ScannerProbe : MonoBehaviour
    {
        [SerializeField] private Transform target;

        public Transform Target
        {
            get => target;
            set => target = value;
        }
    }
}
