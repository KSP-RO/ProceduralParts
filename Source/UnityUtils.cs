using System.Text;
using UnityEngine;

namespace KSPAPIExtensions
{
    public static class UnityUtils
    {
        /// <summary>
        /// Formats a quarternion as an axis and rotation angle about the axis. Useful for debugging quarternions.
        /// </summary>
        /// <param name="q">The quarternion</param>
        /// <param name="format">Number format for the constituent values</param>
        /// <returns>The formatted string</returns>
        public static string ToStringAngleAxis(this Quaternion q, string format = "F3")
        {
            Vector3 axis;
            float angle;
            q.ToAngleAxis(out angle, out axis);
            return "(axis:" + axis.ToString(format) + " angle: " + angle.ToString(format) + ")";
        }

        /// <summary>
        /// Find decendant transform / game object with the specified name.
        /// </summary>
        /// <param name="t">Parent transform</param>
        /// <param name="name">Name to search for</param>
        /// <param name="activeOnly">If true, inactive transforms are ignored</param>
        /// <returns></returns>

        public static Transform FindDecendant(this Transform t, string name, bool activeOnly = false)
        {
            if (t.name == name && (!activeOnly || t.gameObject.activeSelf))
                return t;

            if (activeOnly && !t.gameObject.activeInHierarchy)
                return null;

            for (int i = 0; i < t.childCount; ++i)
            {
                if (FindDecendant(t.GetChild(i), name, activeOnly) is Transform ret)
                    return ret;
            }

            return null;
        }
    }
	/// <summary>
	/// Holds all the bits and pieces for a mesh without the checking code.
	/// Useful when in the process of creating dynamic meshes.
	/// </summary>
	public class UncheckedMesh
	{
		public readonly int nVrt;
		public readonly int nTri;

		public readonly Vector3[] vertices;
		public readonly Vector3[] normals;
		public readonly Vector4[] tangents;
		public readonly Vector2[] uv;
		public readonly int[] triangles;

		/// <summary>
		/// Create a new unchecked mesh
		/// </summary>
		/// <param name="nVrt">Number of vertexes</param>
		/// <param name="nTri">Number of triangles</param>
		public UncheckedMesh(int nVrt, int nTri)
		{
			this.nVrt = nVrt;
			this.nTri = nTri;

			vertices = new Vector3[nVrt];
			normals = new Vector3[nVrt];
			tangents = new Vector4[nVrt];
			uv = new Vector2[nVrt];

			triangles = new int[nTri * 3];
		}

		/// <summary>
		/// Write the mesh data to the given mesh.
		/// </summary>
		/// <param name="mesh">Mesh to write into</param>
		/// <param name="name">Name for the mesh</param>
		public void WriteTo(Mesh mesh, string name = null)
		{
			mesh.Clear();
			if (name != null)
				mesh.name = name;
			mesh.vertices = vertices;
			mesh.normals = normals;
			mesh.tangents = tangents;
			mesh.uv = uv;
			mesh.triangles = triangles;
		}

		/// <summary>
		/// Create a new mesh object with the mesh data as current.
		/// </summary>
		/// <param name="name">Name of the mesh</param>
		/// <returns>The new mesh</returns>
		public Mesh AsMesh(string name = null)
		{
			Mesh mesh = new Mesh();
			WriteTo(mesh, name);
			return mesh;
		}

		/// <summary>
		/// Dump the mesh as a string, useful for debugging.
		/// </summary>
		/// <returns>Mesh as string</returns>
		public string DumpMesh()
		{
			StringBuilder sb = new StringBuilder().AppendLine();
			for (int i = 0; i < vertices.Length; ++i)
			{
				sb
					.Append(vertices[i].ToString("F4")).Append(", ")
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
