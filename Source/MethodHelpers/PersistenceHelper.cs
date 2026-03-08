using UnityEngine;
using Object = UnityEngine.Object;

namespace DebugMod.MethodHelpers
{
    internal static class PersistenceHelper
    {
        public static void DontDestroyOnLoadRoot(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (target is GameObject gameObject)
            {
                GameObject root = gameObject.transform.root != null ? gameObject.transform.root.gameObject : gameObject;
                Object.DontDestroyOnLoad(root);
                return;
            }

            if (target is Component component)
            {
                GameObject root = component.transform.root != null ? component.transform.root.gameObject : component.gameObject;
                Object.DontDestroyOnLoad(root);
                return;
            }

            Object.DontDestroyOnLoad(target);
        }
    }
}
