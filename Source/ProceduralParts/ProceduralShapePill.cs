using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

public class ProceduralShapePill
    : ProceduralAbstractSoRShape
{

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Cap Dia", guiFormat = "F3", guiUnits="m"), 
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float endDiameter = 1.25f;
    private float oldEndDiameter;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Body Dia", guiFormat = "F3", guiUnits = "m"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.00f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float bodyDiameter = 1f;
    private float oldBodyDiameter;


    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Body Length", guiFormat = "F3", guiUnits = "m"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.00f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float bodyLength = 1f;
    private float oldBodyLength;

    public void Start()
    {
        if (!HighLogic.LoadedSceneIsEditor)
            return;

        UI_FloatEdit bodyLengthEdit = (UI_FloatEdit)Fields["bodyLength"].uiControlEditor;
        bodyLengthEdit.maxValue = tank.lengthMax;
        bodyLengthEdit.minValue = 0;
        bodyLengthEdit.incrementLarge = tank.lengthLargeStep;
        bodyLengthEdit.incrementSmall = tank.lengthSmallStep;

        UI_FloatEdit bodyDiameterEdit = (UI_FloatEdit)Fields["bodyDiameter"].uiControlEditor;
        bodyDiameterEdit.maxValue = tank.diameterMax;
        bodyDiameterEdit.minValue = tank.diameterMin;
        bodyDiameterEdit.incrementLarge = tank.diameterLargeStep;
        bodyDiameterEdit.incrementSmall = tank.diameterSmallStep;

        UI_FloatEdit endDiameterEdit = (UI_FloatEdit)Fields["endDiameter"].uiControlEditor;
        endDiameterEdit.maxValue = Mathf.Min(tank.diameterMax, tank.lengthMax);
        endDiameterEdit.minValue = 0;
        endDiameterEdit.incrementLarge = tank.diameterLargeStep;
        endDiameterEdit.incrementSmall = tank.diameterSmallStep;
    }


    protected override void UpdateShape(bool force)
    {

        if (!force && oldEndDiameter == endDiameter && oldBodyLength == bodyLength && oldBodyDiameter == bodyDiameter)
            return;

        if (!force)
        {
            if (bodyLength != oldBodyLength)
            {
                if (bodyLength > oldBodyLength)
                {
                    if (bodyLength + (bodyDiameter - endDiameter) > tank.lengthMax)
                        endDiameter = tank.lengthMax - bodyLength;

                    float volExcess = MaxMinVolume();
                    if (volExcess > 0)
                        bodyLength -= volExcess / (Mathf.PI * bodyDiameter * bodyDiameter * 0.25f);
                }
                else
                {
                    if (bodyLength + (bodyDiameter - endDiameter) < tank.lengthMin)
                        endDiameter = tank.lengthMin - bodyLength;

                    float volExcess = MaxMinVolume();
                    if (volExcess < 0)
                        bodyLength -= volExcess / (Mathf.PI * bodyDiameter * bodyDiameter * 0.25f);

                }
                // body length can't be smaller than the min length
            }
            else if (endDiameter != oldEndDiameter)
            {
                if (endDiameter > oldEndDiameter)
                {
                    if (endDiameter > bodyDiameter)
                        bodyDiameter = endDiameter;
                    if (bodyLength + (bodyDiameter - endDiameter) > tank.lengthMax)
                        bodyLength = tank.lengthMax - bodyDiameter;
                }
            }
            else if (bodyDiameter != oldBodyDiameter)
            {
                if (bodyDiameter < oldBodyDiameter)
                {
                    if (bodyDiameter < endDiameter)
                        endDiameter = bodyDiameter;
                }
            }
        }

        Vector2 norm = new Vector2(1, 0);

        float curveDiameter = bodyDiameter - endDiameter;
        float curveLength = Mathf.PI * curveDiameter * 0.5f;
        float totLength = curveLength + bodyLength;
        float s1 = curveLength * 0.5f / totLength;

        CirclePoints cp = CirclePoints.ForDiameter(curveDiameter, maxCircleError*4);

        LinkedList<ProfilePoint> points = new LinkedList<ProfilePoint>();
        points.AddLast(new ProfilePoint(endDiameter, -0.5f * (bodyLength + curveDiameter), 0f, new Vector2(0, -1)));

        foreach (Vector3 xzu in cp.PointsXZU(0.5f, 0.75f))
            points.AddLast(new ProfilePoint(endDiameter + curveDiameter * xzu.x, -0.5f * (bodyLength - curveDiameter * xzu.y), s1 * Mathf.InverseLerp(0.5f, 0.75f, xzu[2]), (Vector2)xzu));

        points.AddLast(new ProfilePoint(bodyDiameter, -0.5f * bodyLength, s1, new Vector2(1, 0)));
        points.AddLast(new ProfilePoint(bodyDiameter, 0.5f * bodyLength, 1f - s1, new Vector2(1, 0)));

        foreach (Vector3 xzu in cp.PointsXZU(0.75f, 1))
            points.AddLast(new ProfilePoint(endDiameter + curveDiameter * xzu.x, 0.5f * (bodyLength + curveDiameter * xzu.y), 1f - s1 * Mathf.InverseLerp(1f, 0.75f, xzu[2]), (Vector2)xzu));

        points.AddLast(new ProfilePoint(endDiameter, 0.5f * (bodyLength + curveDiameter), 1f, new Vector2(0, 1)));

        WriteMeshes(points);

        oldEndDiameter = endDiameter;
        oldBodyLength = bodyLength;
        oldBodyDiameter = bodyDiameter;
    }

    private float MaxMinVolume()
    {
        float curveRadius = bodyDiameter - endDiameter * 0.5f;


        float volume =
            // body cylinder = pi * r^2 * h = 
            Mathf.PI * bodyDiameter * bodyDiameter * 0.25f * bodyLength
            // ends cylinder = pi * r^2 * h = 
            + Mathf.PI * endDiameter * endDiameter * 0.25f * (bodyDiameter - endDiameter)
            // The volume of the end caps by:  http://mathworld.wolfram.com/PappussCentroidTheorem.html
            // 2 pi times:
            // Area of lamina:  
            //      0.5 * pi * curveRadius ^ 2 
            + Mathf.PI * Mathf.PI * curveRadius * curveRadius
            // Radius of location of centroid:
            //      Hemisphere bit:      4 * curveRadius / ( 3 * pi )
            //      + cylinder offset:   endDiameter * 0.5
              * (4f * curveRadius / (3f * Mathf.PI) + endDiameter * 0.5f);


        if (volume > tank.volumeMax)
            return volume - tank.volumeMax;
        if (volume < tank.volumeMin)
            return volume - tank.volumeMin;
        return 0;
    }
}
