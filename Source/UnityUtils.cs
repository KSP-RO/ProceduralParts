using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// ReSharper disable once CheckNamespace
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
			if (activeOnly && !t.gameObject.activeInHierarchy) 
				return null;

			for (int i = 0; i < t.childCount; ++i)
			{
				Transform ret = FindDecendant(t.GetChild(i), name, activeOnly, out found);
				if (found)
					return ret;
			}

			return null;
		}

		/// <summary>
		/// Returns the path between the parent and the child, with names of decendents in sequence separated
		/// by '/'.  The returned string could be used with parent.Find to get the child.
		/// </summary>
		/// <param name="parent">Parent transform</param>
		/// <param name="child">Child transform</param>
		/// <returns>The path between</returns>
		/// <exception cref="ArgumentException">If the parent is not an ancestor of the child.</exception>
		public static string PathToDecendant(this Transform parent, Transform child)
		{
			List<string> inBetween = new List<string>();
			for (Transform track = child; track != parent; track = track.parent)
			{
				inBetween.Add(track.name);
				if (track.parent == null)
					throw new ArgumentException("Parent transform is not ancestor of child", "parent");
			}
			inBetween.Reverse();
			return string.Join("/", inBetween.ToArray());
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

		// ReSharper disable once StaticFieldInGenericType
		private static readonly object Lock = new object();

		public static T Instance
		{
			get
			{
				if (applicationIsQuitting)
				{
					Debug.LogWarning("[Singleton] Instance '" + typeof(T) +
					                 "' already destroyed on application quit." +
					                 " Won'type create again - returning null.");
					return null;
				}

				lock (Lock)
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
							singleton.name = "(singleton) " + typeof(T);

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

		// ReSharper disable once StaticFieldInGenericType
		private static bool applicationIsQuitting;

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

}
