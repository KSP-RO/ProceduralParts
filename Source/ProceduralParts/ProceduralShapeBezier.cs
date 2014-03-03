using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

public class ProceduralShapeBezier : ProceduralAbstractSoRShape
{

    private static float[][] shapePresets
        = new float[][] { 
            new float[] { 0.4f, 0.0f, 1.0f, 0.6f },
            new float[] { 0.4f, 0.2f, 0.8f, 0.6f },
            new float[] { 0.5f, 0.0f, 0.8f, 0.7f },
            new float[] { 0.3f, 0.2f, 1.0f, 0.5f },
            new float[] { 0.3f, 0.3f, 0.7f, 0.7f },
            new float[] { 0.1f, 0.0f, 0.7f, 2f/3f },
            new float[] { 1f/3f, 0.3f, 1.0f, 0.9f }
        };

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.00f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float length = 1f;
    private float oldLength;

    public string curve;
    private string oldCurve;
    /*
    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Bottom", guiFormat = "F3"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float bottomDiameter = 1.25f;
    private float oldBottomDiameter;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "C1 diameter A", guiFormat = "F0", guiUnits="%"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue=float.PositiveInfinity, incrementLarge = 100f, incrementSlide = 1f)]
    public float c1DiameterA = 0;
    private float oldC1DiameterA;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "C1 diameter B", guiFormat = "F0", guiUnits = "%"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue = 100.0f, incrementSlide = 1f)]
    public float c1DiameterB = 100;
    private float oldC1DiameterB;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "C1 Vert", guiFormat = "F0", guiUnits = "%"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue = 100.0f, incrementSlide = 1f)]
    public float c1Vert = 25f;
    private float oldC1Vert;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "C2 diameter A", guiFormat = "F0", guiUnits = "%"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue = float.PositiveInfinity, incrementLarge = 100f, incrementSlide = 1f)]
    public float c2DiameterA = 0;
    private float oldC2DiameterA;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "C2 diameter B", guiFormat = "F0", guiUnits = "%"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue = 100.0f, incrementSlide = 1f)]
    public float c2DiameterB = 100;
    private float oldC2DiameterB;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "C2 Vert", guiFormat = "F0", guiUnits = "%"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue = 100.0f, incrementSlide = 1f)]
    public float c2Vert = 75f;
    private float oldC2Vert;
    */

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Top", guiFormat = "F3"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float topDiameter = 1.25f;
    private float oldTopDiameter;


    //private UI_FloatEdit c1DiameterAEdit;
    //private UI_FloatEdit c2DiameterAEdit;

    public void Start()
    {
        if (!HighLogic.LoadedSceneIsEditor)
            return;

        UI_FloatEdit lengthEdit = (UI_FloatEdit)Fields["length"].uiControlEditor;
        lengthEdit.maxValue = tank.lengthMax;
        lengthEdit.minValue = tank.lengthMin;
        lengthEdit.incrementLarge = tank.lengthLargeStep;
        lengthEdit.incrementSmall = tank.lengthSmallStep;

        UI_FloatEdit topDiameterEdit = (UI_FloatEdit)Fields["topDiameter"].uiControlEditor;
        topDiameterEdit.maxValue = tank.diameterMax;
        topDiameterEdit.minValue = tank.diameterMin;
        topDiameterEdit.incrementLarge = tank.diameterLargeStep;
        topDiameterEdit.incrementSmall = tank.diameterSmallStep;

        //c1DiameterAEdit = (UI_FloatEdit)Fields["c1DiameterA"].uiControlEditor;
        //c2DiameterAEdit = (UI_FloatEdit)Fields["c2DiameterA"].uiControlEditor;

        UI_FloatEdit bottomDiameterEdit = (UI_FloatEdit)Fields["bottomDiameter"].uiControlEditor;
        bottomDiameterEdit.maxValue = tank.diameterMax;
        bottomDiameterEdit.minValue = tank.diameterMin;
        bottomDiameterEdit.incrementLarge = tank.diameterLargeStep;
        bottomDiameterEdit.incrementSmall = tank.diameterSmallStep;
    }

    protected override void UpdateShape(bool force)
    {
        /*
        if (!force && oldTopDiameter == topDiameter && oldBottomDiameter == bottomDiameter && oldLength == length &&
            c1DiameterA == oldC1DiameterA && c1DiameterB == oldC1DiameterB && c1Vert == oldC1Vert &&
            c2DiameterA == oldC2DiameterA && c2DiameterB == oldC2DiameterB && c2Vert == oldC2Vert )
            return;

        // SO the maths here is unashamedly complicated.

        // What we have is a bezier curve with a point at the top , control points c1 and c2, and a point at the bottom.
        // The y dimension of the control points is just a percentage of the distance from the bottom to the top, where 
        // the bottom would be 0% and the top 100%. 

        // In the x direction, there's two components. 
        
        // The A component takes the mean diameter between top and bottom, and multiplies it by the A factor. This allows 
        // for curves when the top and bottom diameters are similar

        // The B component takes the absoute difference between the top and bottom, and multiplies it by the B factor.
        // This is useful for more conic shaped objects where there is a big difference between the top and bottom.

        // There are also two other constraints. The diameter of the curve at any point must not exceed the maximum allowed
        // diameter, plus the points on the curve must be convex.


        if (!force)
        {
            // Vertical bits.
            if (c1Vert != oldC1Vert)
            {
                if (c2Vert < c1Vert)
                    c2Vert = c1Vert;
            }
            else if (c2Vert != oldC2Vert)
            {
                if (c1Vert > c2Vert)
                    c1Vert = c2Vert;
            }
        
            float c1Y = length * c1Vert / 100f - length * 0.5f;
            float c2Y = length * c2Vert / 100f - length * 0.5f;



            float c1XBase = Mathf.Lerp(topDiameter, bottomDiameter, c1Vert / 100f);
            float c1XA = c1DiameterA * (topDiameter + bottomDiameter) / 200f;
            float c1XB = c1DiameterB * Mathf.Abs(topDiameter - bottomDiameter) / 100f;
            float c1X = c1XBase + c1XA + c1XB;

            float c2XBase = Mathf.Lerp(topDiameter, bottomDiameter, c2Vert / 100f);
            float c2XA = c2DiameterA * (topDiameter + bottomDiameter) / 200f;
            float c2XB = c2DiameterB * Mathf.Abs(topDiameter - bottomDiameter) / 100f;
            float c2X = c2XBase + c2XA + c2XB;


            // So the points need to form a convex hull to ensure the resulting shape is convex
            // Push things into shape so they stay that way.

            if (oldTopDiameter != topDiameter || oldBottomDiameter != bottomDiameter)
            {
                // We give precidence to the top and bottom diameters, then the A factors.
                // The B factors
            }
        }



        // Perpendicular.
        Vector2 norm = new Vector2(length, (bottomDiameter-topDiameter)/2f);
        norm.Normalize();

        WriteMeshes(
            new ProfilePoint(bottomDiameter, -0.5f * length, 0f, norm),
            new ProfilePoint(topDiameter, 0.5f * length, 1f, norm)
            );

        oldTopDiameter = topDiameter; oldBottomDiameter = bottomDiameter; oldLength = length;
        oldC1DiameterA = c1DiameterA; oldC1DiameterB = c1DiameterB; oldC1Vert = c1Vert;
        oldC2DiameterA = c2DiameterA; oldC2DiameterB = c2DiameterB; oldC2Vert = c2Vert;
        */
    }
}
