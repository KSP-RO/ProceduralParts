using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;


public class StretchyConicTank : StretchyTanks
{
  [KSPField(isPersistant = true)]
  public float topFactor = 1f;


  void dumpObj(Transform t)
  {
    if (t==null) { print("null object"); return; }

    print("object "+t.name);
    if (t.parent!=null) print("  parent "+t.parent.name);
    if (t.GetComponent<MeshFilter>()) print("  mesh");
    if (t.collider) print("  collider");

    for (int i=0; i<t.childCount; ++i)
      dumpObj(t.GetChild(i));
  }


  public override void updateScale()
  {
    print("***");
    dumpObj(transform);

    base.updateScale();
  }
}
