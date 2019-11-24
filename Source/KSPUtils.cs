// ReSharper disable once CheckNamespace
namespace KSPAPIExtensions
{
    public static class PartUtils
    {
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
