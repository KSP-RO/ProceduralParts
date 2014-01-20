using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;

public static class TransformExtensions
{
    public static void DumpTree(this Transform t, DumpTreeOption options = DumpTreeOption.Active)
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

        if (((options & DumpTreeOption.Materials) != 0) && t.renderer != null)
        {
            foreach (Material m in t.renderer.sharedMaterials)
                MonoBehaviour.print(space + "+ mat:" + m.name);
        }
        if (((options & DumpTreeOption.Components) != 0))
        {
            foreach (Component c in t.gameObject.GetComponents<Component>())
            {
                MonoBehaviour.print(space + "+ component:" + c.GetType());
            }
        }
        if (((options & DumpTreeOption.Mesh) != 0))
        {
            MeshFilter filter = t.gameObject.GetComponent<MeshFilter>();
            if (filter != null)
                MonoBehaviour.print(space + "+ mesh:" + ((filter.sharedMesh==null)?"*null*":(filter.sharedMesh.name + " verts:" + filter.sharedMesh.vertexCount)));
        }


        for (int i = 0; i < t.childCount; ++i)
            DumpTree(t.GetChild(i), options, level + 1);
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
            if(value == null)
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
            MonoBehaviour.print("Different fields: " + field.FieldType.Name + " " + field.Name + (Equals(thisValue, thatValue)?"(compute equal)":(" " + thisValue + " != " + thatValue)));
        }
    }
}

[Flags]
public enum DumpTreeOption
{
    Active = 0x1,
    Materials = 0x2,
    Components = 0x4,
    TransformPosition = 0x8,
    TransformRotation = 0x10,
    Mesh = 0x20,
}