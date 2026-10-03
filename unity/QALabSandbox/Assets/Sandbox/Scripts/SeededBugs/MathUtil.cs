using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB14 (Combat math): <c>SafeDivide</c> logs <c>{context}: division by zero in SafeDivide</c> when
    /// its callers pass invalid data. Two callers (DamageCalculator, SpeedModel) give two different
    /// messages with the same top frame, so exact signatures split them and the merge variants join them.
    /// </summary>
    public static class MathUtil
    {
        public static float SafeDivide(float a, float b, string context)
        {
            if (b == 0f)
            {
                LabelRecorder.Trigger("SB14");
                Debug.LogError($"{context}: division by zero in SafeDivide");
                return 0f;
            }
            return a / b;
        }
    }
}
