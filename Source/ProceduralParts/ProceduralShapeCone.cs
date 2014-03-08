using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

public class ProceduralShapeCone : ProceduralAbstractSoRShape
{
    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Bottom", guiFormat = "F3"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float bottomDiameter = 1.25f;
    private float oldBottomDiameter;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Top", guiFormat = "F3"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float topDiameter = 1.25f;
    private float oldTopDiameter;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.00f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float length = 1f;
    private float oldLength;

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

        //Debug.LogWarning("Cone.UpdateShape");

        // Maxmin the volume.
        tankVolume = (Mathf.PI * length * (topDiameter * topDiameter + topDiameter * bottomDiameter + bottomDiameter * bottomDiameter)) / 12f;

        if (HighLogic.LoadedSceneIsEditor)
        {
            if (tankVolume > tank.volumeMax)
                tankVolume = tank.volumeMax;
            else if (tankVolume < tank.volumeMin)
                tankVolume = tank.volumeMin;
            else
                goto nochange;

            if (oldLength != length)
                length = tankVolume * 12f / (Mathf.PI * (topDiameter * topDiameter + topDiameter * bottomDiameter + bottomDiameter * bottomDiameter));
            else if (oldTopDiameter != topDiameter) 
            {
                // this becomes solving the quadratic on topDiameter
                float a = length * Mathf.PI;
                float b = length * Mathf.PI * bottomDiameter;
                float c = length * Mathf.PI * bottomDiameter * bottomDiameter - tankVolume * 12f;

                float det = Mathf.Sqrt(b * b - 4 * a * c);
                topDiameter = (det - b) / ( 2f * a );
            }
            else
            {
                // this becomes solving the quadratic on bottomDiameter
                float a = length * Mathf.PI;
                float b = length * Mathf.PI * topDiameter;
                float c = length * Mathf.PI * topDiameter * topDiameter - tankVolume * 12f;

                float det = Mathf.Sqrt(b * b - 4 * a * c);
                bottomDiameter = (det - b) / (2f * a);
            }
        }
        nochange:

        // Perpendicular.
        Vector2 norm = new Vector2(length, (bottomDiameter-topDiameter)/2f);
        norm.Normalize();

        WriteMeshes(
            new ProfilePoint(bottomDiameter, -0.5f * length, 0f, norm),
            new ProfilePoint(topDiameter, 0.5f * length, 1f, norm)
            );

        oldTopDiameter = topDiameter;
        oldBottomDiameter = bottomDiameter;
        oldLength = length;
    }
}
