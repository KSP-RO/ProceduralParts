using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;


//ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ//


struct BezierSlope
{
  Vector2 p1, p2;

  public BezierSlope(Vector4 v)
  {
    p1=new Vector2(v.x, v.y);
    p2=new Vector2(v.z, v.w);
  }

  public Vector2 interp(float t)
  {
    Vector2 a=Vector2.Lerp(Vector2.zero, p1, t);
    Vector2 b=Vector2.Lerp(p1, p2, t);
    Vector2 c=Vector2.Lerp(p2, Vector2.one, t);

    Vector2 d=Vector2.Lerp(a, b, t);
    Vector2 e=Vector2.Lerp(b, c, t);

    return Vector2.Lerp(d, e, t);
  }
}


//ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ//


public class StretchyConicTank : StretchyTanks
{
  [KSPField(isPersistant=true)] public float topFactor=0.5f;
  [KSPField(isPersistant=true)] public Vector4 coneShape=new Vector4(0.3f, 0.3f, 0.7f, 0.7f);

  [KSPField] public int circleSegments=24;
  [KSPField] public int heightSegments=9;

  [KSPField] public string topRadKey="y";
  [KSPField] public string shapeKey="b";


  public override string GetInfo()
  {
    return base.GetInfo()+
      "\n* Hold '"+topRadKey+"' and move mouse to change top width."+
      "\n* Press '"+shapeKey+"' to change its cone shape.";
  }


  public override void OnMouseOver()
  {
    base.OnMouseOver();

    if (HighLogic.LoadedSceneIsEditor)
    {
      EditorLogic editor = EditorLogic.fetch;

      if (Input.GetKey(topRadKey) && editor.editorScreen!=EditorLogic.EditorScreen.Actions)
      {
        float initialValue=topFactor;
        topFactor+=(Input.GetAxis("Mouse X")+Input.GetAxis("Mouse Y")) * 0.075f;
        topFactor=Mathf.Max(topFactor, 0.075f);
        topFactor=Mathf.Min(topFactor, 7.5f);
        if (initialValue!=radialFactor)
        {
          triggerUpdate=true;
          rescaled=true;
        }
      }

      if (Input.GetKeyDown(shapeKey) && editor.editorScreen!=EditorLogic.EditorScreen.Actions)
      {
        var shapes=new List<Vector4>();
        foreach (var sl in GameDatabase.Instance.GetConfigNodes("STRETCHYTANKCONESHAPES"))
          for (int i=0; i<sl.values.Count; ++i)
            if (sl.values[i].name=="shape")
              shapes.Add(ConfigNode.ParseVector4(sl.values[i].value));

        if (shapes.Count==0) shapes.Add(coneShape);

        int idx=0;
        for (; idx<shapes.Count; ++idx) if (shapes[idx]==coneShape) break;

        if (++idx>=shapes.Count) idx=0;
        coneShape=shapes[idx];

        triggerUpdate=true;
      }
    }
  }


  public override void rescaleModel()
  {
    // get side mesh
    var tr=transform.GetChild(0).GetChild(0).GetChild(0);

    var mf=tr.GetComponent<MeshFilter>();
    if (!mf) { Debug.LogError("[StretchyConicTank] no model to reshape", part); return; }

    var m=mf.mesh;
    if (!m) { Debug.LogError("[StretchyConicTank] no mesh to reshape", part); return; }

    // prepare for building geometry
    if (circleSegments<3) circleSegments=3;
    if (heightSegments<1) heightSegments=1;

    int sideVerts=(circleSegments+1)*(heightSegments+1);
    int sideFaces=circleSegments*heightSegments*2;

    int capVerts=circleSegments*2;
    int capFaces=(circleSegments-1)*2;

    var dirs=new Vector3[circleSegments+1];
    for (int i=0; i<=circleSegments; ++i)
    {
      float a=Mathf.PI*2*i/circleSegments;
      dirs[i]=new Vector3(Mathf.Cos(a), -Mathf.Sin(a), 0);
    }

    float baseRad=radialFactor*1.25f;
    float  topRad=   topFactor*1.25f;
    float height=stretchFactor*1.875f;

    var slope=new BezierSlope(coneShape);

    var shape=new Vector3[heightSegments+1];
    for (int i=0; i<=heightSegments; ++i)
    {
      float v=(float)i/heightSegments;

      Vector2 p;
      if (baseRad<=topRad)
        p=slope.interp(v);
      else
      {
        p=slope.interp(1-v);
        p.y=1-p.y;
      }

      float y=(p.y-0.5f)*height;
      float r=Mathf.Abs(baseRad-topRad)*p.x+Mathf.Min(baseRad, topRad);
      shape[i]=new Vector3(r, y, p.y);
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

      for (int j=0; j<=circleSegments; ++j, ++vi)
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

    for (int i=0, vi=0, ti=0; i<heightSegments; ++i, ++vi)
      for (int j=0; j<circleSegments; ++j, ++vi)
      {
        int nv=vi+1;

        tri[ti++]=vi;
        tri[ti++]=nv+circleSegments+1;
        tri[ti++]=nv;

        tri[ti++]=vi;
        tri[ti++]=vi+circleSegments+1;
        tri[ti++]=nv+circleSegments+1;
      }

    m.triangles=tri;

    if (!HighLogic.LoadedSceneIsEditor) m.Optimize();

    var collider=tr.GetComponent<MeshCollider>();
    collider.sharedMesh=null;
    collider.sharedMesh=m;

    // get cap mesh
    tr=tr.GetChild(0);

    mf=tr.GetComponent<MeshFilter>();
    if (!mf) { Debug.LogError("[StretchyConicTank] no model to reshape", part); return; }

    m=mf.mesh;
    if (!m) { Debug.LogError("[StretchyConicTank] no mesh to reshape", part); return; }

    // build cap mesh
    m.Clear();

    verts=new Vector3[capVerts];
    uv=new Vector2[capVerts];
    norm=new Vector3[capVerts];
    tang=new Vector4[capVerts];

    const float capMapScale=0.47f;

    for (int i=0; i<circleSegments; ++i)
    {
      var d=dirs[i];

      verts[i]=d*baseRad-Vector3.forward*height*0.5f;
      norm [i]=-Vector3.forward;
      tang[i].Set(-1, 0, 0, 1);
      uv[i].Set(-d.x*capMapScale+0.5f, d.y*capMapScale+0.5f);

      verts[i+circleSegments]=d*topRad+Vector3.forward*height*0.5f;
      norm [i+circleSegments]=Vector3.forward;
      tang[i+circleSegments].Set(1, 0, 0, 1);
      uv[i+circleSegments].Set(d.x*capMapScale+0.5f, d.y*capMapScale+0.5f);
    }

    // set vertex data to mesh
    m.vertices=verts;
    m.uv=uv;
    m.normals=norm;
    m.tangents=tang;

    m.uv2=null;
    m.colors32=null;

    tri=new int[capFaces*3];

    for (int i=1, ti=0; i<circleSegments; ++i, ti+=3)
    {
      int nv=i+1; if (nv==circleSegments) nv=0;

      tri[ti  ]=0;
      tri[ti+1]=i;
      tri[ti+2]=nv;

      tri[ti  +(circleSegments-1)*3]=circleSegments;
      tri[ti+1+(circleSegments-1)*3]=circleSegments+nv;
      tri[ti+2+(circleSegments-1)*3]=circleSegments+i;
    }

    m.triangles=tri;

    if (!HighLogic.LoadedSceneIsEditor) m.Optimize();
  }
}
