using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

public class ProceduralShapeCone : ProceduralAbstractSoRShape
{
    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Top", guiFormat = "F3"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float topDiameter = 1.25f;
    private float oldTopDiameter;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Bottom", guiFormat = "F3"),
     UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.001f)]
    public float bottomDiameter = 1.25f;
    private float oldBottomDiameter;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3"),
     UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.001f)]
    public float length = 1f;
    private float oldLength;

    private UI_FloatEdit topDiameterEdit;
    private UI_FloatEdit bottomDiameterEdit;

    public void Start()
    {
        if (!HighLogic.LoadedSceneIsEditor)
            return;

        if (pPart.lengthMin == pPart.lengthMax)
            Fields["length"].guiActiveEditor = false;
        else
        {
            UI_FloatEdit lengthEdit = (UI_FloatEdit)Fields["length"].uiControlEditor;
            lengthEdit.maxValue = pPart.lengthMax;
            lengthEdit.minValue = pPart.lengthMin;
            lengthEdit.incrementLarge = pPart.lengthLargeStep;
            lengthEdit.incrementSmall = pPart.lengthSmallStep;
        }

        if (pPart.diameterMin == pPart.diameterMax)
        {
            Fields["topDiameter"].guiActiveEditor = false;
            Fields["bottomDiameter"].guiActiveEditor = false;
        }
        else
        {
            topDiameterEdit = (UI_FloatEdit)Fields["topDiameter"].uiControlEditor;
            topDiameterEdit.maxValue = pPart.diameterMax;
            topDiameterEdit.incrementLarge = pPart.diameterLargeStep;
            topDiameterEdit.incrementSmall = pPart.diameterSmallStep;

            bottomDiameterEdit = (UI_FloatEdit)Fields["bottomDiameter"].uiControlEditor;
            bottomDiameterEdit.maxValue = pPart.diameterMax;
            bottomDiameterEdit.incrementLarge = pPart.diameterLargeStep;
            bottomDiameterEdit.incrementSmall = pPart.diameterSmallStep;

            UpdateMinDiameters();
        }
    }

    private void UpdateMinDiameters()
    {
        if (topDiameterEdit == null)
            return;

        if (bottomDiameter == topDiameter)
        {
            topDiameterEdit.minValue = 0;
            bottomDiameterEdit.minValue = 0;
        }
        if (topDiameter < bottomDiameter)
        {
            topDiameterEdit.minValue = 0;
            bottomDiameterEdit.minValue = pPart.diameterMin;
        }
        else
        {
            topDiameterEdit.minValue = pPart.diameterMin;
            bottomDiameterEdit.minValue = 0;
        }
    }

    protected override void UpdateShape(bool force)
    {
        if (!force && oldTopDiameter == topDiameter && oldBottomDiameter == bottomDiameter && oldLength == length)
            return;

        //Debug.LogWarning("Cone.UpdateShape");

        // Maxmin the volume.
        volume = (Mathf.PI * length * (topDiameter * topDiameter + topDiameter * bottomDiameter + bottomDiameter * bottomDiameter)) / 12f;

        if (HighLogic.LoadedSceneIsEditor)
        {
            if (volume > pPart.volumeMax)
                volume = pPart.volumeMax;
            else if (volume < pPart.volumeMin)
                volume = pPart.volumeMin;
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

            UpdateMinDiameters();
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
