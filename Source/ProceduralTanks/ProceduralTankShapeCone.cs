using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

public class ProceduralTankShapeCone : AbstractSurfaceOfRevolutionShape
{

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Top", guiFormat = "F3"), UI_FloatEdit(minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float topDiameter = 1.25f;
    private float oldTopDiameter;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Bottom", guiFormat = "F3"), UI_FloatEdit(minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float bottomDiameter = 1.25f;
    private float oldBottomDiameter;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3"), UI_FloatEdit(minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float length = 1f;
    private float oldLength;

    public void Start()
    {
        if (!HighLogic.LoadedSceneIsEditor)
            return;

        UI_FloatEdit lengthEdit = (UI_FloatEdit)Fields["length"].uiControlEditor;
        lengthEdit.maxValue = tank.lengthMax;
        lengthEdit.minValue = tank.lengthMin;

        UI_FloatEdit topDiameterEdit = (UI_FloatEdit)Fields["topDiameter"].uiControlEditor;
        topDiameterEdit.maxValue = tank.diameterMax;
        topDiameterEdit.minValue = tank.diameterMin;

        UI_FloatEdit bottomDiameterEdit = (UI_FloatEdit)Fields["bottomDiameter"].uiControlEditor;
        bottomDiameterEdit.maxValue = tank.diameterMax;
        bottomDiameterEdit.minValue = tank.diameterMin;
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

        gameObject.SendMessage("UpdateSRBScale", bottomDiameter);

        oldTopDiameter = topDiameter;
        oldBottomDiameter = bottomDiameter;
        oldLength = length;
    }
}
