using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;

namespace KSPAPIExtensions
{

    public static class APIExtensions
    {
        #region Debugging dumpage

        [Flags]
        public enum DumpTreeOption
        {
            None = 0x0,
            Default = 0x0,

            Active = 0x1,
            TransformPosition = 0x2,
            TransformRotation = 0x4,

            // Optons that require iterating through components should have the 0x10 bit set.
            Components = 0x30,
            Materials = 0x50,
            Mesh = 0x90,

            Rigidbody = 0x110,
        }

        public static string DumpTree(this Transform t, DumpTreeOption options = DumpTreeOption.Default)
        {
            StringBuilder sb = new StringBuilder();
            DumpTree(t, options, 0, sb);
            return sb.ToString();
        }

        private static void DumpTree(Transform t, DumpTreeOption options, int level, StringBuilder sb)
        {
            string space = "";
            for (int i = 0; i < level; ++i)
                space += '-';
            sb.AppendLine(space + t.name);
            if ((options & DumpTreeOption.Active) == DumpTreeOption.Active)
            {
                sb.AppendLine(space + "+ activeSelf=" + t.gameObject.activeSelf + " activeInHeirachy=" + t.gameObject.activeInHierarchy);
            }
            if ((options & DumpTreeOption.TransformPosition) == DumpTreeOption.TransformPosition)
            {
                sb.AppendLine(space + "+ position: " + t.position + " localPosition: " + t.localPosition);
            }
            if ((options & DumpTreeOption.TransformRotation) == DumpTreeOption.TransformRotation)
            {
                sb.AppendLine(space + "+ rotation: " + t.rotation + " localRotation: " + t.localRotation);
            }

            if ((((int)options & 0x10) == 0))
                goto skipComponents;

            foreach (Component c in t.gameObject.GetComponents<Component>())
            {

                if (!typeof(Transform).IsInstanceOfType(c) && ((options & DumpTreeOption.Components) == DumpTreeOption.Components))
                    sb.AppendLine(space + "+ component:" + c.GetType());

                if (typeof(Renderer).IsInstanceOfType(c) && (options & DumpTreeOption.Materials) == DumpTreeOption.Materials)
                    foreach (Material m in t.renderer.sharedMaterials)
                        sb.AppendLine(space + "++ mat:" + m.name);

                if (typeof(MeshFilter).IsInstanceOfType(c) && (options & DumpTreeOption.Mesh) == DumpTreeOption.Mesh)
                {
                    MeshFilter filter = (MeshFilter)c;
                    if (filter != null)
                        sb.AppendLine(space + "++ mesh:" + ((filter.sharedMesh == null) ? "*null*" : (filter.sharedMesh.name + " verts:" + filter.sharedMesh.vertexCount)));
                }

                if (typeof(MeshCollider).IsInstanceOfType(c) && (options & DumpTreeOption.Mesh) == DumpTreeOption.Mesh)
                {
                    MeshCollider collider = (MeshCollider)c;
                    if (collider != null)
                        sb.AppendLine(space + "++ mesh:" + ((collider.sharedMesh == null) ? "*null*" : (collider.sharedMesh.name + " verts:" + collider.sharedMesh.vertexCount)));
                }

                if (typeof(Rigidbody).IsInstanceOfType(c) && (options & DumpTreeOption.Rigidbody) == DumpTreeOption.Rigidbody) 
                {
                    Rigidbody body = (Rigidbody)c;
                    sb.AppendLine(space + "++ Mass:" + body.mass.ToString("F3"));
                    sb.AppendLine(space + "++ CoM:" + body.centerOfMass.ToString("F3"));
                }
                if (typeof(Joint).IsInstanceOfType(c) && (options & DumpTreeOption.Rigidbody) == DumpTreeOption.Rigidbody)
                {
                    Joint joint = (Joint)c;
                    sb.AppendLine(space + "++ anchor:" + joint.anchor.ToString("F3"));

                    sb.AppendLine(space + "++ connectedBody: " + (joint.connectedBody != null));
                    if (joint.connectedBody != null)
                        DumpTree(joint.connectedBody.transform, options, level+1, sb);
                }
            }

        skipComponents:
            for (int i = 0; i < t.childCount; ++i)
                DumpTree(t.GetChild(i), options, level + 1, sb);
        }

        public static string DumpMesh(this Mesh mesh)
        {
            Vector3[] verticies = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Vector2[] uv = mesh.uv;

            StringBuilder sb = new StringBuilder().AppendLine();
            for (int i = 0; i < mesh.vertexCount; ++i)
            {
                sb
                    .Append(verticies[i].ToString("F4")).Append(", ")
                    .Append(uv[i].ToString("F4")).Append(", ")
                    .Append(normals[i].ToString("F4")).Append(", ")
                    .Append(tangents[i].ToString("F4")).AppendLine();
            }
            sb.Replace("(", "").Replace(")", "");
            sb.AppendLine();

            for (int i = 0; i < mesh.triangles.Length; i += 3)
            {
                sb
                    .Append(mesh.triangles[i]).Append(',')
                    .Append(mesh.triangles[i + 1]).Append(',')
                    .Append(mesh.triangles[i + 2]).AppendLine();
            }

           return sb.ToString();
        }

        public static string DumpObjectFields(this object obj, BindingFlags flags = BindingFlags.Default)
        {
            StringBuilder sb = new StringBuilder();
            Type type = obj.GetType();

            foreach (FieldInfo field in type.GetFields(flags))
            {
                object value = field.GetValue(obj);
                if (value == null)
                    sb.AppendLine(field.FieldType.Name + " " + field.Name + "is null");
                else
                    sb.AppendLine(field.FieldType.Name + " " + field.Name + " = " + value);
            }
            return sb.ToString();
        }

        public static string DumpNotEqualFields<T>(this T obj, T that, BindingFlags flags = BindingFlags.Default)
        {
            StringBuilder sb = new StringBuilder();
            if (ReferenceEquals(obj, that))
                sb.AppendLine("Same object");

            Type type = typeof(T);

            foreach (FieldInfo field in type.GetFields(flags))
            {
                object thisValue = field.GetValue(obj);
                object thatValue = field.GetValue(that);

                if (field.FieldType.IsPrimitive && Equals(thisValue, thatValue))
                    continue;
                if (ReferenceEquals(thisValue, thatValue))
                    continue;
                sb.AppendLine("Different fields: " + field.FieldType.Name + " " + field.Name + (Equals(thisValue, thatValue) ? "(compute equal)" : (" " + thisValue + " != " + thatValue)));
            }
            return sb.ToString();
        }
        #endregion

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
                    throw new ArgumentException("Passed transform is not a child of this part");
            }
            inBetween.Reverse();
            return string.Join("/", inBetween.ToArray());
        }
        #endregion

        #region Message passing 

        /// <summary>
        /// Invoke a method on the part and all enabled modules attached to the part. This is similar in scope to
        /// the SendMessage method on GameObject.
        /// </summary>
        /// <param name="part">the part</param>
        /// <param name="messageName">Name of the method to invoke</param>
        /// <param name="args">parameters</param>
        public static void SendPartMessage(this Part part, string messageName, params object[] args)
        {
            if(part.enabled)
                SendPartMessageInternal(part, messageName, args);
            foreach(PartModule module in part.Modules)
                if(module.enabled && module.isEnabled)
                    SendPartMessageInternal(module, messageName, args);
        }

        private static void SendPartMessageInternal(object target, string message, object[] args)
        {
            Type t = target.GetType();
            foreach(MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == message)
                {
                    // Just invoke it and deal with the consequences, rather than stuff around trying to match parameters
                    // MethodInfo does all the parameter checking anyhow.
                    try
                    {
                        m.Invoke(target, args);
                    }
                    catch (ArgumentException) { }
                    catch (TargetParameterCountException) { }
                }
        }

        #endregion
    }

    public static class UtilityExtensions
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