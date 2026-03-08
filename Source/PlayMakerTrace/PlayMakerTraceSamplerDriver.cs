using System;
using UnityEngine;

namespace DebugMod.PlayMakerTrace
{
    internal sealed class PlayMakerTraceSamplerDriver : MonoBehaviour
    {
        internal Action<float>? Tick;

        private void Update()
        {
            Tick?.Invoke(Time.unscaledTime);
        }
    }
}
