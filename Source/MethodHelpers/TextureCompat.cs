using System;
using System.Reflection;
using UnityEngine;

namespace DebugMod.MethodHelpers
{
    internal static class TextureCompat
    {
        private static readonly MethodInfo LoadImageMethod =
            Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
                ?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) })
            ?? throw new MissingMethodException("UnityEngine.ImageConversion.LoadImage(Texture2D, byte[], bool)");

        public static bool LoadImage(Texture2D texture, byte[] data, bool markNonReadable = false)
        {
            return (bool)LoadImageMethod.Invoke(null, new object[] { texture, data, markNonReadable });
        }
    }
}
