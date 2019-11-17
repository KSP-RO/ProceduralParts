using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace KSPAPIExtensions
{
    public static class PartUtils
    {
        private static FieldInfo windowListField;

        private static FieldInfo FindWindowList()
        {
            Type cntrType = typeof(UIPartActionController);
            foreach (FieldInfo info in cntrType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (info.FieldType == typeof(List<UIPartActionWindow>))
                {
                    return info;
                }
            }
            Debug.LogWarning("*PartUtils* Unable to find UIPartActionWindow list");
            return null;
        }
        /// <summary>
        /// Find the UIPartActionWindow for a part. Usually this is useful just to mark it as dirty.
        /// </summary>
        public static UIPartActionWindow FindActionWindow(this Part part)
        {
            // We need to do quite a bit of piss-farting about with reflection to 
            // dig the thing out. We could just use Object.Find, but that requires hitting a heap more objects.
            if (part is Part && UIPartActionController.Instance is UIPartActionController controller)
            {
                if (windowListField is null)
                    windowListField = FindWindowList();
                if (windowListField != null && (windowListField.GetValue(controller) is List<UIPartActionWindow> uiPartActionWindows))
                    return uiPartActionWindows.FirstOrDefault(window => window != null && window.part == part);
            }
            return null;
        }

        /// <summary>
        /// If this part is a symmetry clone of another part, this method will return the original part.
        /// </summary>
        /// <param name="part">The part to find the original of</param>
        /// <returns>The original part, or the part itself if it was the original part</returns>
        public static Part GetSymmetryCloneOriginal(this Part part)
        {
            if (!part.isClone || part.symmetryCounterparts == null || part.symmetryCounterparts.Count == 0)
                return part;

            foreach (Part other in part.symmetryCounterparts)
            {
                if (!other.isClone) return other;
            }
            return part;
        }

        public static bool IsSurfaceAttached(this Part part) =>
            part.srfAttachNode != null && part.srfAttachNode.attachedPart == part.parent;
    }
}
