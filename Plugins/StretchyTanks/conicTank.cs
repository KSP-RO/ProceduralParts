using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;


public class StretchyConicTank : StretchyTanks
{
  [KSPField(isPersistant = true)] public float topFactor = 1f;

  [KSPField] public int circleSegments=24;
  [KSPField] public int heightSegments=10;


  // void dumpObj(Transform t)
  // {
  //   if (t==null) { print("null object"); return; }

  //   print("object "+t.name);
  //   if (t.parent!=null) print("  parent "+t.parent.name);
  //   if (t.GetComponent<MeshFilter>()) print("  mesh");
  //   if (t.collider) print("  collider");

  //   for (int i=0; i<t.childCount; ++i)
  //     dumpObj(t.GetChild(i));
  // }


  public override void rescaleModel()
  {
    // get mesh
    var tr=transform.GetChild(0).GetChild(0).GetChild(0);

    var mf=tr.GetComponent<MeshFilter>();
    if (!mf) { Debug.LogError("[StretchyConicTank] no model to reshape", part); return; }

    var m=mf.mesh;
    if (!m) { Debug.LogError("[StretchyConicTank] no mesh to reshape", part); return; }

    // prepare for building geometry
    if (circleSegments<3) circleSegments=3;
    if (heightSegments<1) heightSegments=1;

    int sideVerts=circleSegments*(heightSegments+1);
    int sideFaces=circleSegments*heightSegments*2;

    var dirs=new Vector3[circleSegments];
    for (int i=0; i<circleSegments; ++i)
    {
      float a=Mathf.PI*2*i/circleSegments;
      dirs[i]=new Vector3(Mathf.Cos(a), -Mathf.Sin(a), 0);
    }

    float baseRad=radialFactor*1.25f;
    float  topRad=   topFactor*1.25f;

    var shape=new Vector3[heightSegments+1];
    for (int i=0; i<=heightSegments; ++i)
    {
      float v=(float)i/heightSegments;
      float y=(v-0.5f)*1.875f;
      float r=Mathf.Lerp(baseRad, topRad, v);
      shape[i]=new Vector3(r, y, v);
    }

    // build side surface mesh
    m.Clear();

    var verts=new Vector3[sideVerts];
    var uv=new Vector2[sideVerts];
    var norm=new Vector3[sideVerts];
    var tang=new Vector4[sideVerts];

    for (int i=0, vi=0; i<=heightSegments; ++i)
    {
      var p=shape[i];

      Vector2 n;
      if (i==0) n=shape[1]-shape[0];
      else if (i==shape.Length-1) n=shape[i]-shape[i-1];
      else n=shape[i+1]-shape[i-1];
      n.Set(n.y, -n.x);
      n.Normalize();

      for (int j=0; j<circleSegments; ++j, ++vi)
      {
        var d=dirs[j];
        verts[vi]=d*p.x+Vector3.forward*p.y;
        norm [vi]=d*n.x+Vector3.forward*n.y;
        tang[vi].Set(-d.y, d.x, 0, 1);
        uv[vi].Set((float)j/circleSegments, p.z);
      }
    }

    // set vertex data to mesh
    m.vertices=verts;
    m.uv=uv;
    m.normals=norm;
    m.tangents=tang;

    m.uv2=null;
    m.colors32=null;

    var tri=new int[sideFaces*3];

    for (int i=0, vi=0, ti=0; i<heightSegments; ++i)
      for (int j=0; j<circleSegments; ++j, ++vi)
      {
        int nv=vi+1;
        if (j==circleSegments-1) nv-=circleSegments;

        tri[ti++]=vi;
        tri[ti++]=nv+circleSegments;
        tri[ti++]=nv;

        tri[ti++]=vi;
        tri[ti++]=vi+circleSegments;
        tri[ti++]=nv+circleSegments;
      }

    m.triangles=tri;

    if (!HighLogic.LoadedSceneIsEditor) m.Optimize();

    //== cap mesh
  }
}
