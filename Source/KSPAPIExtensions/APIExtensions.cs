using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;

namespace KSPAPIExtensions
{

    public static class UnityUtils
    {
        #region transforms and geometry
        public static string ToStringAngleAxis(this Quaternion q, string format = "F3")
        {
            Vector3 axis;
            float angle;
            q.ToAngleAxis(out angle, out axis);
            return "(axis:" + axis.ToString(format) + " angle: " + angle.ToString(format) + ")";
        }

        public static Transform FindDecendant(this Transform t, string name, bool activeOnly = false)
        {
            bool found;
            return FindDecendant(t, name, activeOnly, out found);
        }

        private static Transform FindDecendant(Transform t, string name, bool activeOnly, out bool found)
        {
            if (t.name == name && (!activeOnly || t.gameObject.activeSelf))
            {
                found = true;
                return t;
            }
            found = false;
            Transform ret = null;
            if (!activeOnly || t.gameObject.activeInHierarchy)
                for (int i = 0; i < t.childCount && !found; ++i)
                    ret = FindDecendant(t.GetChild(i), name, activeOnly, out found);

            return ret;
        }

        public static string PathToDecendant(this Transform parent, Transform child)
        {
            List<string> inBetween = new List<string>();
            for (Transform track = child; track != parent; track = track.parent)
            {
                inBetween.Add(track.name);
                if (track.parent == null)
                    throw new ArgumentException("Passed transform is not a module of this part");
            }
            inBetween.Reverse();
            return string.Join("/", inBetween.ToArray());
        }
        #endregion
    }

    /// <summary>
    /// Flags to determine relationship between two parts.
    /// </summary>
    [Flags]
    public enum PartRelationship
    {
        Vessel = 0x1,
        Self = 0x2,
        Symmetry = 0x4,
        Decendent = 0x8 ,
        Child = 0x10,
        Ancestor = 0x20,
        Parent = 0x40,
        Sibling = 0x80,
        Unrelated = 0x100,
        Unknown = 0x0
    }

    [Flags]
    public enum GameSceneFilter
    {
        Loading = 1 << (int)GameScenes.LOADING,
        MainMenu = 1 << (int)GameScenes.MAINMENU,
        SpaceCenter = 1 << (int)GameScenes.SPACECENTER,
        VAB = 1 << (int)GameScenes.EDITOR,
        SPH = 1 << (int)GameScenes.SPH,
        Flight = 1 << (int)GameScenes.FLIGHT,
        TrackingStation = 1 << (int)GameScenes.TRACKSTATION,
        Settings = 1 << (int)GameScenes.SETTINGS,
        Credits = 1 << (int)GameScenes.CREDITS,

        AnyEditor = VAB | SPH, 
        AnyInitializing = 0xFFFF & ~(AnyEditor | Flight), 
        Any = 0xFFFF
    }

    public static class PartUtils
    {
        private static FieldInfo windowListField;
        public static UIPartActionWindow FindActionWindow(this Part part)
        {
            // We need to do quite a bit of piss-farting about with reflection to 
            // dig the thing out.
            UIPartActionController controller = UIPartActionController.Instance;

            if (windowListField == null)
            {
                Type cntrType = typeof(UIPartActionController);
                foreach (FieldInfo info in cntrType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    if (info.FieldType == typeof(List<UIPartActionWindow>))
                    {
                        windowListField = info;
                        goto foundField;
                    }
                }
                Debug.LogWarning("*PartUtils* Unable to find UIPartActionWindow list");
                return null;
            }
        foundField:

            foreach (UIPartActionWindow window in (List<UIPartActionWindow>)windowListField.GetValue(controller))
                if (window.part == part)
                    return window;

            return null;
        }

        public static PartRelationship RelationTo(this Part part, Part other)
        {
            if (other == null)
                return PartRelationship.Unknown;

            if (other == part)
                return PartRelationship.Self;
            if (part.localRoot != other.localRoot)
                return PartRelationship.Unrelated;
            if (part.parent == other)
                return PartRelationship.Child;
            if (other.parent == part)
                return PartRelationship.Parent;
            if (other.parent == part.parent)
                return PartRelationship.Sibling;
            for (Part tmp = part.parent; tmp != null; tmp = tmp.parent)
                if (tmp == other)
                    return PartRelationship.Decendent;
            for (Part tmp = other.parent; tmp != null; tmp = tmp.parent)
                if (tmp == part)
                    return PartRelationship.Ancestor;
            if(part.localRoot == other.localRoot)
                return PartRelationship.Vessel;
            return PartRelationship.Unrelated;
        }

        public static bool RelationTest(this Part part, Part other, PartRelationship relation)
        {
            if (relation == PartRelationship.Unknown)
                return true;
            if (part == null || other == null)
                return false;

            if (TestFlag(relation, PartRelationship.Self) && part == other)
                return true;
            if (TestFlag(relation, PartRelationship.Vessel) && part.localRoot == other.localRoot)
                return true;
            if (TestFlag(relation, PartRelationship.Unrelated) && part.localRoot != other.localRoot)
                return true;
            if (TestFlag(relation, PartRelationship.Sibling) && part.parent == other.parent)
                return true;
            if (TestFlag(relation, PartRelationship.Ancestor))
            {
                for (Part upto = other.parent; upto != null; upto = upto.parent)
                    if (upto == part)
                        return true;
            }
            else if (TestFlag(relation, PartRelationship.Parent) && part == other.parent)
                return true;

            if (TestFlag(relation, PartRelationship.Decendent))
            {
                for (Part upto = part.parent; upto != null; upto = upto.parent)
                    if (upto == other)
                        return true;
            }
            else if (TestFlag(relation, PartRelationship.Child) && part.parent == other)
                return true;

            if (TestFlag(relation, PartRelationship.Symmetry))
                foreach (Part sym in other.symmetryCounterparts)
                    if (part == sym)
                        return true;
            return false;
        }

        public static bool TestFlag(this PartRelationship e, PartRelationship flags)
        {
            return (e & flags) == flags;
        }

        public static GameSceneFilter AsFilter(this GameScenes scene)
        {
            return (GameSceneFilter)(1 << (int)scene);
        }

        public static bool IsLoaded(this GameSceneFilter filter)
        {
            return (int)(filter & HighLogic.LoadedScene.AsFilter()) != 0;
        }
    }

    public static class StringUtils
    {
        public static bool BeginsWith(this string str, string comp)
        {
            if (str == null || comp == null || str.Length == comp.Length)
                return str == comp;
            if (comp.Length > str.Length)
                return false;
            for (int i = 0; i < comp.Length; ++i)
                if (str[i] != comp[i])
                    return false;
            return true;
        }

        public static string FormatFixedDigits(this float value, int digits)
        {
            int max = 10;
            while (value >= max && digits > 0)
            {
                max *= 10;
                --digits;
            }
            return value.ToString("F" + digits);
        }
    }

    public static class MathUtils
    {

        public static bool TestClamp(ref float value, float min, float max)
        {
            if (value < min)
            {
                value = min;
                return true;
            }
            if (value > max)
            {
                value = max;
                return true;
            }
            return false;
        }

        public static float RoundTo(float value, float precision)
        {
            return Mathf.Round(value / precision) * precision;
        }

        /// <summary>
        /// Format a numeric value using SI prefexes. 
        /// 
        /// eg: 13401 -> 13.4 k
        /// 
        /// </summary>
        /// <param name="d">value to format</param>
        /// <param name="unit">unit string</param>
        /// <param name="exp">Exponennt to the existing value. eg: if value was km rather than m this would be 3.</param>
        /// <param name="sigFigs">number of signifigant figures to display</param>
        /// <returns></returns>
        public static string ToStringSI(this double value, int sigFigs = 3, int exponent = 0, string unit = null)
        {
            SIPrefix prefix = value.GetSIPrefix(exponent);
            return prefix.FormatSI(value, sigFigs, exponent, unit);
        }

        /// <summary>
        /// Format a numeric value using SI prefexes. 
        /// 
        /// eg: 13401 -> 13.4 k
        /// 
        /// </summary>
        /// <param name="d">value to format</param>
        /// <param name="unit">unit string</param>
        /// <param name="exp">Exponennt to the existing value. eg: if value was km rather than m this would be 3.</param>
        /// <param name="sigFigs">number of signifigant figures to display</param>
        /// <returns></returns>
        public static string ToStringSI(this float value, int sigFigs = 3, int exponent = 0, string unit = null)
        {
            SIPrefix prefix = value.GetSIPrefix(exponent);
            return prefix.FormatSI(value, sigFigs, exponent, unit);
        }

        public static string ToStringExt(this double value, string format)
        {
            if (format[0] == 'S' || format[0] == 's')
            {
                if (format.Length == 1)
                    return ToStringSI(value);
                int pmi = format.IndexOf('+');
                int sigFigs;
                if (pmi < 0)
                {
                    pmi = format.IndexOf('-');
                    if (pmi < 0)
                    {
                        sigFigs = int.Parse(format.Substring(1));
                        return ToStringSI(value, sigFigs);
                    }
                }
                sigFigs = int.Parse(format.Substring(1, pmi - 1));
                int exponent = int.Parse(format.Substring(pmi));
                return ToStringSI(value, sigFigs, exponent);
            }
            return value.ToString(format);
        }

        public static string ToStringExt(this float value, string format)
        {
            return ToStringExt((double)value, format);
        }

        /// <summary>
        /// Round a number to a set number of significant figures.
        /// </summary>
        /// <param name="d">number to round</param>
        /// <param name="sigFigs">number of significant figures, defaults to 3</param>
        /// <returns></returns>
        public static float RoundSigFigs(this float d, int sigFigs = 3)
        {
            
            int exponent = (int)Math.Floor(Math.Log10(Math.Abs(d))) - sigFigs;
            float div = Mathf.Pow(10, exponent);
            return Mathf.Round(d / div) * div;
        }

        /// <summary>
        /// Round a number to a set number of significant figures.
        /// </summary>
        /// <param name="d">number to round</param>
        /// <param name="sigFigs">number of significant figures, defaults to 3</param>
        /// <returns></returns>
        public static double RoundSigFigs(this double value, int sigFigs = 3)
        {
            int exponent = (int)Math.Floor(Math.Log10(Math.Abs(value))) - sigFigs;
            double div = Mathf.Pow(10, exponent);
            return Math.Round(value / div) * div;
        }

        /// <summary>
        /// Find the SI prefix for a number
        /// </summary>
        public static SIPrefix GetSIPrefix(this double value, int exponent = 0)
        {
            if (value == 0)
                return SIPrefix.None;

            int exp = (int)Math.Floor(Math.Log10(Math.Abs(value))) + exponent;

            if (exp <= 3 && exp >= -1)
                return SIPrefix.None;
            if (exp < 0)
                return (SIPrefix)((exp-2) / 3 * 3);
            return (SIPrefix)(exp / 3 * 3);
        }

        /// <summary>
        /// Find the SI prefix for a number
        /// </summary>
        public static SIPrefix GetSIPrefix(this float value, int exponent = 0)
        {
            return GetSIPrefix((double)value, exponent);
        }

        public static string PrefixString(this SIPrefix pfx)
        {
            switch (pfx)
            {
                case SIPrefix.None: return "";
                case SIPrefix.Kilo: return "k";
                case SIPrefix.Mega: return "M";
                case SIPrefix.Giga: return "G";
                case SIPrefix.Tera: return "T";
                case SIPrefix.Peta: return "P";
                case SIPrefix.Exa: return "E";
                case SIPrefix.Zotta: return "Z";
                case SIPrefix.Yotta: return "Y";
                case SIPrefix.Milli: return "m";
                case SIPrefix.Micro: return "mic";
                case SIPrefix.Nano: return "n";
                case SIPrefix.Pico: return "p";
                case SIPrefix.Femto: return "f";
                case SIPrefix.Atto: return "a";
                case SIPrefix.Zepto: return "z";
                case SIPrefix.Yocto: return "y";
                default: throw new ArgumentException("Illegal prefix", "pfx");
            }
        }

        public static float Round(this SIPrefix pfx, float value, int sigFigs = 3, int exponent = 0)
        {
            float div = Mathf.Pow(10, (int)pfx - sigFigs + exponent);
            return Mathf.Round(value / div) * div;
        }

        public static double Round(this SIPrefix pfx, double value, int sigFigs = 3, int exponent = 0)
        {
            double div = Math.Pow(10, (int)pfx - sigFigs + exponent);
            return Math.Round(value / div) * div;
        }

        public static string FormatSI(this SIPrefix pfx, double value, int sigFigs = 3, int exponent = 0, string unit = null)
        {
            return string.Format("{0} {1}{2}", pfx.GetFormatter(value, sigFigs, exponent)(value), pfx.PrefixString(), unit);
        }

        public static Func<double, string> GetFormatter(this SIPrefix pfx, double value, int sigFigs = 3, int exponent = 0)
        {
            int exp = (int)(Math.Floor(Math.Log10(Math.Abs(value)))) - (int)pfx + exponent;
            double div = Math.Pow(10, (int)pfx - exponent);

            if (exp < 0)
                return v => (v/div).ToString("F" + (sigFigs-1));
            if (exp >= sigFigs)
            {
                double mult = Math.Pow(10, exp - sigFigs + 1);
                return v => (Math.Round(v / div / mult) * mult).ToString("F0");
            }
            return v => (v/div).ToString("F" + (sigFigs - exp - 1));
        }

    }

    public enum SIPrefix
    {
        None = 0,
        Kilo = 3,
        Mega = 6,
        Giga = 9,
        Tera = 12,
        Peta = 15,
        Exa = 18,
        Zotta = 21,
        Yotta = 24,
        Milli = -3,
        Micro = -6,
        Nano = -9,
        Pico = -12,
        Femto = -15,
        Atto = -18,
        Zepto = -21,
        Yocto = -24
    }

    /// <summary>
    /// 
    /// </summary>
    public class UncheckedMesh
    {
        public readonly int nVrt;
        public readonly int nTri;

        public readonly Vector3[] verticies;
        public readonly Vector3[] normals;
        public readonly Vector4[] tangents;
        public readonly Vector2[] uv;
        public readonly int[] triangles;

        public UncheckedMesh(int nVrt, int nTri)
        {
            this.nVrt = nVrt;
            this.nTri = nTri;

            verticies = new Vector3[nVrt];
            normals = new Vector3[nVrt];
            tangents = new Vector4[nVrt];
            uv = new Vector2[nVrt];

            triangles = new int[nTri * 3];
        }

        public void WriteTo(Mesh mesh, string name = null)
        {
            mesh.Clear();
            if (name != null)
                mesh.name = name;
            mesh.vertices = verticies;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uv;
            mesh.triangles = triangles;
        }

        public Mesh AsMesh(string name = null)
        {
            Mesh mesh = new Mesh();
            WriteTo(mesh, name);
            return mesh;
        }

        public string DumpMesh()
        {
            StringBuilder sb = new StringBuilder().AppendLine();
            for (int i = 0; i < verticies.Length; ++i)
            {
                sb
                    .Append(verticies[i].ToString("F4")).Append(", ")
                    .Append(uv[i].ToString("F4")).Append(", ")
                    .Append(normals[i].ToString("F4")).Append(", ")
                    .Append(tangents[i].ToString("F4")).AppendLine();
            }
            sb.Replace("(", "").Replace(")", "");
            sb.AppendLine();

            for (int i = 0; i < triangles.Length; i += 3)
            {
                sb
                    .Append(triangles[i]).Append(',')
                    .Append(triangles[i + 1]).Append(',')
                    .Append(triangles[i + 2]).AppendLine();
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// KSPAddon with equality checking using an additional type parameter. Fixes the issue where AddonLoader prevents multiple start-once addons with the same start scene.
    /// </summary>
    public class KSPAddonFixed : KSPAddon, IEquatable<KSPAddonFixed>
    {
        private readonly Type type;

        public KSPAddonFixed(KSPAddon.Startup startup, bool once, Type type)
            : base(startup, once)
        {
            this.type = type;
        }

        public override bool Equals(object obj)
        {
            if (obj.GetType() != this.GetType()) { return false; }
            return Equals((KSPAddonFixed)obj);
        }

        public bool Equals(KSPAddonFixed other)
        {
            if (this.once != other.once) { return false; }
            if (this.startup != other.startup) { return false; }
            if (this.type != other.type) { return false; }
            return true;
        }

        public override int GetHashCode()
        {
            return this.startup.GetHashCode() ^ this.once.GetHashCode() ^ this.type.GetHashCode();
        }
    }

#if false
    /// <summary>
    /// Be aware this will not prevent a non singleton constructor
    ///   such as `T myT = new T();`
    /// To prevent that, add `protected T () {}` to your singleton class.
    /// 
    /// As a note, this is made as MonoBehaviour because we need Coroutines.
    /// </summary>
    public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T _instance;

        private static object _lock = new object();

        public static T Instance
        {
            get
            {
                if (applicationIsQuitting)
                {
                    Debug.LogWarning("[Singleton] Instance '" + typeof(T) +
                        "' already destroyed on application quit." +
                        " Won't create again - returning null.");
                    return null;
                }

                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = (T)FindObjectOfType(typeof(T));

                        if (FindObjectsOfType(typeof(T)).Length > 1)
                        {
                            Debug.LogError("[Singleton] Something went really wrong " +
                                " - there should never be more than 1 singleton!" +
                                " Reopenning the scene might fix it.");
                            return _instance;
                        }

                        if (_instance == null)
                        {
                            GameObject singleton = new GameObject();
                            _instance = singleton.AddComponent<T>();
                            singleton.name = "(singleton) " + typeof(T).ToString();

                            DontDestroyOnLoad(singleton);

                            Debug.Log("[Singleton] An instance of " + typeof(T) +
                                " is needed in the scene, so '" + singleton +
                                "' was created with DontDestroyOnLoad.");
                        }
                        else
                        {
                            Debug.Log("[Singleton] Using instance already created: " +
                                _instance.gameObject.name);
                        }
                    }

                    return _instance;
                }
            }
        }

        private static bool applicationIsQuitting = false;
        /// <summary>
        /// When Unity quits, it destroys objects in a random order.
        /// In principle, a Singleton is only destroyed when application quits.
        /// If any script calls Instance after it have been destroyed, 
        ///   it will create a buggy ghost object that will stay on the Editor scene
        ///   even after stopping playing the Application. Really bad!
        /// So, this was made to be sure we're not creating that buggy ghost object.
        /// </summary>
        public void OnDestroy()
        {
            applicationIsQuitting = true;
        }
    }
#endif

}