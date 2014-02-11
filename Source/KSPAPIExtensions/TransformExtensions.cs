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
        public static void DumpTree(this Transform t, DumpTreeOption options = DumpTreeOption.Default)
        {
            DumpTree(t, options, 0);
        }

        private static void DumpTree(Transform t, DumpTreeOption options, int level)
        {
            string space = "";
            for (int i = 0; i < level; ++i)
                space += '-';
            MonoBehaviour.print(space + t.name);
            if ((options & DumpTreeOption.Active) != 0)
            {
                MonoBehaviour.print(space + "+ activeSelf=" + t.gameObject.activeSelf + " activeInHeirachy=" + t.gameObject.activeInHierarchy);
            }
            if ((options & DumpTreeOption.TransformPosition) != 0)
            {
                MonoBehaviour.print(space + "+ position: " + t.position + " localPosition: " + t.localPosition);
            }
            if ((options & DumpTreeOption.TransformRotation) != 0)
            {
                MonoBehaviour.print(space + "+ rotation: " + t.rotation + " localRotation: " + t.localRotation);
            }

            if ((((int)options & 0x10) == 0))
                goto skipComponents;

            foreach (Component c in t.gameObject.GetComponents<Component>())
            {

                if (!typeof(Transform).IsInstanceOfType(c) && ((options & DumpTreeOption.Components) != 0))
                    MonoBehaviour.print(space + "+ component:" + c.GetType());

                if (typeof(Renderer).IsInstanceOfType(c) && (options & DumpTreeOption.Materials) != 0)
                    foreach (Material m in t.renderer.sharedMaterials)
                        MonoBehaviour.print(space + "+ mat:" + m.name);

                if (typeof(MeshFilter).IsInstanceOfType(c) && (options & DumpTreeOption.Mesh) != 0)
                {
                    MeshFilter filter = (MeshFilter)c;
                    if (filter != null)
                        MonoBehaviour.print(space + "+ mesh:" + ((filter.sharedMesh == null) ? "*null*" : (filter.sharedMesh.name + " verts:" + filter.sharedMesh.vertexCount)));
                }

                if (typeof(MeshCollider).IsInstanceOfType(c) && (options & DumpTreeOption.Mesh) != 0)
                {
                    MeshCollider collider = (MeshCollider)c;
                    if (collider != null)
                        MonoBehaviour.print(space + "+ mesh:" + ((collider.sharedMesh == null) ? "*null*" : (collider.sharedMesh.name + " verts:" + collider.sharedMesh.vertexCount)));
                }
            }

        skipComponents:
            for (int i = 0; i < t.childCount; ++i)
                DumpTree(t.GetChild(i), options, level + 1);
        }

        public static void DumpMesh(this Mesh mesh)
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

            MonoBehaviour.print(sb);
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

        public static void DumpObjectFields(this object obj, BindingFlags flags = BindingFlags.Default)
        {
            Type type = obj.GetType();

            foreach (FieldInfo field in type.GetFields(flags))
            {
                object value = field.GetValue(obj);
                if (value == null)
                    MonoBehaviour.print(field.FieldType.Name + " " + field.Name + "is null");
                else
                    MonoBehaviour.print(field.FieldType.Name + " " + field.Name + " = " + value);
            }
        }

        public static void DumpNotEqualFields<T>(this T obj, T that, BindingFlags flags = BindingFlags.Default)
        {
            if (ReferenceEquals(obj, that))
                MonoBehaviour.print("Same object");

            Type type = typeof(T);

            foreach (FieldInfo field in type.GetFields(flags))
            {
                object thisValue = field.GetValue(obj);
                object thatValue = field.GetValue(that);

                if (field.FieldType.IsPrimitive && Equals(thisValue, thatValue))
                    continue;
                if (ReferenceEquals(thisValue, thatValue))
                    continue;
                MonoBehaviour.print("Different fields: " + field.FieldType.Name + " " + field.Name + (Equals(thisValue, thatValue) ? "(compute equal)" : (" " + thisValue + " != " + thatValue)));
            }
        }

        public static string ToStringAngleAxis(this Quaternion q)
        {
            Vector3 axis;
            float angle;
            q.ToAngleAxis(out angle, out axis);
            return "(axis:" + axis.ToString("F3") + " angle: " + angle.ToString("F3") + ")";
        }
    }

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

        public void DumpMesh()
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

            MonoBehaviour.print(sb);
        }
    }

}