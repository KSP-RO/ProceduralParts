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
            new float[] { 0.1f, 0.0f, 0.7f, 0.667f },
            new float[] { 1f/3f, 0.3f, 1.0f, 0.9f }
        };

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.00f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float length = 1f;
    private float oldLength;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Top", guiFormat = "F3"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float topDiameter = 1.25f;
    private float oldTopDiameter;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "C1 dia abs", guiFormat = "F3", guiUnits="m"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 0.25f, incrementSlide = 0.001f)]
    public float c1DiameterAbs = 0;
    private float oldC1DiameterAbs;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "C1 dia rel", guiFormat = "F0", guiUnits = "%"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue = 100.0f, incrementSlide = 1f)]
    public float c1DiameterRel = 100;
    private float oldC1DiameterRel;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "C1 Vert", guiFormat = "F0", guiUnits = "%"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue = 100.0f, incrementSlide = 1f)]
    public float c1Vert = 0f;
    private float oldC1Vert;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "C2 dia abs", guiFormat = "F3", guiUnits = "m"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 0.25f, incrementSlide = 0.001f)]
    public float c2DiameterAbs = 0;
    private float oldC2DiameterAbs;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "C2 dia rel", guiFormat = "F0", guiUnits = "%"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue = 100.0f, incrementSlide = 1f)]
    public float c2DiameterRel = 100;
    private float oldC2DiameterRel;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "C2 Vert", guiFormat = "F0", guiUnits = "%"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue = 100.0f, incrementSlide = 1f)]
    public float c2Vert = 0f;
    private float oldC2Vert;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Bottom", guiFormat = "F3"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float bottomDiameter = 1.25f;
    private float oldBottomDiameter;


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

        UI_FloatEdit c1DiameterAbsEdit = (UI_FloatEdit)Fields["c1DiameterAbs"].uiControlEditor;
        c1DiameterAbsEdit.maxValue = tank.diameterMax;
        c1DiameterAbsEdit.incrementLarge = tank.diameterSmallStep;

        UI_FloatEdit c2DiameterAbsEdit = (UI_FloatEdit)Fields["c2DiameterAbs"].uiControlEditor;
        c2DiameterAbsEdit.maxValue = tank.diameterMax;
        c2DiameterAbsEdit.incrementLarge = tank.diameterSmallStep;

        UI_FloatEdit bottomDiameterEdit = (UI_FloatEdit)Fields["bottomDiameter"].uiControlEditor;
        bottomDiameterEdit.maxValue = tank.diameterMax;
        bottomDiameterEdit.minValue = tank.diameterMin;
        bottomDiameterEdit.incrementLarge = tank.diameterLargeStep;
        bottomDiameterEdit.incrementSmall = tank.diameterSmallStep;
    }

    protected override void UpdateShape(bool force)
    {
        if (!force && oldTopDiameter == topDiameter && oldBottomDiameter == bottomDiameter && oldLength == length)
            return;

        {
            // Maxmin the volume.
            float volume = (Mathf.PI * length * (topDiameter * topDiameter + topDiameter * bottomDiameter + bottomDiameter * bottomDiameter)) / 12f;
            if (volume > tank.volumeMax)
                volume = tank.volumeMax;
            else if (volume < tank.volumeMin)
                volume = tank.volumeMin;
            else
                goto nochange;

            if (oldLength != length)
                length = volume * 12f / (Mathf.PI * (topDiameter * topDiameter + topDiameter * bottomDiameter + bottomDiameter * bottomDiameter));
            else if (oldTopDiameter != topDiameter) 
            {
                // this becomes solving the quadratic on topDiameter
                float a = length * Mathf.PI;
                float b = length * Mathf.PI * bottomDiameter;
                float c = length * Mathf.PI * bottomDiameter * bottomDiameter - volume * 12f;

                float det = Mathf.Sqrt(b * b - 4 * a * c);
                topDiameter = (det - b) / ( 2f * a );
            }
            else
            {
                // this becomes solving the quadratic on bottomDiameter
                float a = length * Mathf.PI;
                float b = length * Mathf.PI * topDiameter;
                float c = length * Mathf.PI * topDiameter * topDiameter - volume * 12f;

                float det = Mathf.Sqrt(b * b - 4 * a * c);
                bottomDiameter = (det - b) / (2f * a);
            }
        }
        nochange:

        // Perpendicular.
        Vector2 norm = new Vector2(length, (bottomDiameter-topDiameter)/2f);
        norm.Normalize();

        WriteMeshes(
            new ProfilePoint(topDiameter, 0.5f * length, 0f, norm),
            new ProfilePoint(bottomDiameter, -0.5f * length, 1f, norm)
            );

        oldTopDiameter = topDiameter;
        oldBottomDiameter = bottomDiameter;
        oldLength = length;
    }
}
