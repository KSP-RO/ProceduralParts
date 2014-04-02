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
    /// Be aware this will not prevent a non singleton constructor
    ///   such as `T myT = new T();`
    /// To prevent that, add `protected T () {}` to your singleton class.
    /// 
    /// As a note, this is made as MonoBehaviour because we need Coroutines.
    /// </summary>
    public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
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
        Editor = 1 << (int)GameScenes.EDITOR | 1 << (int)GameScenes.SPH,
        VAB = 1 << (int)GameScenes.EDITOR,
        SPH = 1 << (int)GameScenes.SPH,
        Flight = 1 << (int)GameScenes.FLIGHT,
        TrackingStation = 1 << (int)GameScenes.TRACKSTATION,
        Settings = 1 << (int)GameScenes.SETTINGS,
        Credits = 1 << (int)GameScenes.CREDITS, 
        All = 0xFFFF
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

}