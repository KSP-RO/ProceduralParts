using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

public class ProceduralTankShapeCylinder : AbstractSurfaceOfRevolutionShape
{

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Diameter", guiFormat = "F3"), UI_FloatEdit(minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float diameter = 1.25f;
    private float oldDiameter;

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

        UI_FloatEdit diameterEdit = (UI_FloatEdit)Fields["diameter"].uiControlEditor;
        diameterEdit.maxValue = tank.diameterMax;
        diameterEdit.minValue = tank.diameterMin;
    }


    protected override void UpdateShape(bool force)
    {
        if (!force && oldDiameter == diameter && oldLength == length)
            return;

        {
            // Maxmin the volume.
            float volume = diameter * diameter * 0.25f * Mathf.PI * length;
            if (volume > tank.volumeMax)
                volume = tank.volumeMax;
            else if (volume < tank.volumeMin)
                volume = tank.volumeMin;
            else
                goto nochange;

            if (oldDiameter != diameter)
                diameter = Mathf.Sqrt(volume / (0.25f * Mathf.PI * length));
            else
                length = volume / (diameter * diameter * 0.25f * Mathf.PI);
        }
        nochange:

        Vector2 norm = new Vector2(1, 0);

        WriteMeshes(
            new ProfilePoint(diameter, 0.5f * length, 0f, norm),
            new ProfilePoint(diameter, -0.5f * length, 1f, norm)
            );

        gameObject.SendMessage("UpdateSRBScale", diameter);

        oldDiameter = diameter;
        oldLength = length;
    }
}
