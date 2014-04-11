using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{
    public class ProceduralShapePill
        : ProceduralAbstractSoRShape
    {
        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Diameter", guiFormat = "S4", guiUnits = "m"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.001f)]
        public float diameter = 1.25f;
        private float oldDiameter;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "S4", guiUnits = "m"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.001f)]
        public float length = 1f;
        private float oldLength;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Fillet", guiFormat = "S4", guiUnits = "m"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.001f)]
        public float fillet = 1f;
        private float oldFillet;

        [KSPField]
        public bool useEndDiameter = false;

        private UI_FloatEdit filletEdit;

        public override void OnStart(StartState state)
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

            UI_FloatEdit diameterEdit = (UI_FloatEdit)Fields["diameter"].uiControlEditor;
            if (pPart.diameterMin == pPart.diameterMax)
                Fields["diameter"].guiActiveEditor = false;
            else
            {
                diameterEdit.maxValue = pPart.diameterMax;
                diameterEdit.minValue = useEndDiameter ? 0 : pPart.diameterMin;
                diameterEdit.incrementLarge = pPart.diameterLargeStep;
                diameterEdit.incrementSmall = pPart.diameterSmallStep;
            }

            if (!pPart.allowCurveTweaking)
            {
                Fields["fillet"].guiActiveEditor = false;
                diameterEdit.maxValue = pPart.diameterMax - fillet;
            }
            else
            {
                filletEdit = (UI_FloatEdit)Fields["fillet"].uiControlEditor;
                filletEdit.maxValue = Mathf.Min(length, useEndDiameter ? pPart.diameterMax : diameter);
                filletEdit.minValue = 0;
                filletEdit.incrementLarge = pPart.diameterLargeStep;
                filletEdit.incrementSmall = pPart.diameterSmallStep;
            }
        }

        // A few shortcuts to use in formulas.
        private const float pi = Mathf.PI;
        private static Func<float, float> sqrt = Mathf.Sqrt;
        private static Func<float, float, float> pow = Mathf.Pow;

        protected override void UpdateShape(bool force)
        {
            if (!force && oldDiameter == diameter && oldLength == length && oldFillet == fillet)
                return;

            if (HighLogic.LoadedSceneIsFlight)
            {
                volume = CalcVolume();
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                if (filletEdit == null)
                    filletEdit = (UI_FloatEdit)Fields["fillet"].uiControlEditor;

                if (length != oldLength)
                {
                    if (length < oldLength && fillet > length)
                        fillet = length;

                    float volExcess = MaxMinVolume();
                    if (volExcess != 0)
                    {
                        // Again using alpha, solve the volume equation below equation for l
                        // v = 1/24 pi (6 d^2 l+3 (pi-4) d f^2+(10-3 pi) f^3) for l
                        // l = (-3 (pi-4) pi d f^2+pi (3 pi-10) f^3+24 v)/(6 pi d^2) 
                        length = (-3f * (pi - 4f) * pi * diameter * pow(fillet, 2) + pi * (3f * pi - 10f) * pow(fillet, 3) + 24f * volume) / (6f * pi * pow(diameter, 2));
                        length = Mathf.Round(length / pPart.lengthSmallStep) * pPart.lengthSmallStep;

                        // We could iterate here with the fillet and push it back up if it's been pushed down
                        // but it's altogether too much bother. User will just have to suck it up and not be
                        // so darn agressive with short lengths. I mean, seriously... :)
                    }

                    filletEdit.maxValue = Mathf.Min(length, useEndDiameter ? pPart.diameterMax : diameter);
                }
                else if (diameter != oldDiameter)
                {
                    if (useEndDiameter)
                    {
                        if (diameter + fillet < pPart.diameterMin)
                            fillet = pPart.diameterMin - diameter;
                        else if (diameter + fillet > pPart.diameterMax)
                            fillet = pPart.diameterMax - diameter;
                    }
                    else
                    {
                        if (diameter < oldDiameter && fillet > diameter)
                            fillet = diameter;
                    }

                    float volExcess = MaxMinVolume();
                    if (volExcess != 0)
                    {
                        // Unfortunatly diameter is not as easily isolated, but its still possible.

                        // v = 1/24 pi (6 d^2 l+3 (pi-4) d f^2+(10-3 pi) f^3) for d
                        // simplify d = ((-3 pi^2 f^2+12 pi f^2) ± sqrt(3 pi) sqrt(3 pi^3 f^4-24 pi^2 f^4+48 pi f^4+24 pi^2 f^3 l-80 pi f^3 l+192 l v))/(12 pi l) 
                        // d = (-3 (pi-4) pi f^2 ± sqrt(3 pi) sqrt(3 (pi-4)^2 pi f^4+8 pi (3 pi-10) f^3 l+192 l v)) / (12 pi l)

                        float t1 = -3 * (pi - 4f) * pi * fillet * fillet;
                        float t2 = sqrt(3f * pi) * sqrt(3f * pow(pi - 4f, 2) * pi * pow(fillet, 4) + 8f * pi * (3f * pi - 10f) * pow(fillet, 3) * length + 192f * length * volume);
                        float de = (12f * pi * length);

                        // I'm pretty sure only the +ve value is required, but make the -ve possible too.
                        diameter = (t1 + t2) / de;
                        if (diameter < 0)
                            diameter = (t1 - t2) / de;

                        diameter = Mathf.Round(diameter / pPart.diameterSmallStep) * pPart.diameterSmallStep;
                    }

                    filletEdit.maxValue = Mathf.Min(length, useEndDiameter ? pPart.diameterMax : diameter);
                }
                else if (fillet != oldFillet)
                {
                    if (useEndDiameter)
                    {
                        // Keep diameter + fillet within range.
                        if (diameter + fillet < pPart.diameterMin)
                            diameter = pPart.diameterMin - fillet;
                        else if (diameter + fillet > pPart.diameterMax)
                            diameter = pPart.diameterMax - fillet;
                    }

                    // Will do an iterative process for finding the value.
                    // The equation is far too complicated plug this into alpha and you'll see what I mean:
                    // v = 1/24 pi (6 d^2 l+3 (pi-4) d f^2+(10-3 pi) f^3) for f

                    float vol = CalcVolume();
                    float inc;

                    if (vol < pPart.volumeMin)
                    {
                        volume = pPart.volumeMin;
                        inc = -pPart.diameterSmallStep;
                    }
                    else if (vol > pPart.volumeMax)
                    {
                        volume = pPart.volumeMax;
                        inc = pPart.diameterSmallStep;
                    }
                    else
                    {
                        volume = vol;
                        goto goldilocks;
                    }

                    float lVol;
                    float lFillet;
                    do
                    {
                        lVol = vol;
                        lFillet = fillet;
                        fillet += inc;
                        vol = CalcVolume();
                    }
                    while (Mathf.Abs(vol - volume) < Mathf.Abs(lVol - volume));
                    fillet = Mathf.Round(lFillet / pPart.diameterSmallStep) * pPart.diameterSmallStep;
                goldilocks: ;
                }
            }

            LinkedList<ProfilePoint> points = new LinkedList<ProfilePoint>();

            if (fillet == 0)
            {
                // Reduces down to a cylinder part.
                points.AddLast(new ProfilePoint(diameter, -0.5f * length, 0f, new Vector2(1, 0)));
                points.AddLast(new ProfilePoint(diameter, 0.5f * length, 1f, new Vector2(1, 0)));
            }
            else
            {
                float bodyLength = length - fillet;
                float endDiameter = useEndDiameter ? diameter : (diameter - fillet);
                float bodyDiameter = useEndDiameter ? (fillet + diameter) : diameter;

                float filletLength = Mathf.PI * fillet * 0.5f;
                float totLength = filletLength + bodyLength;
                float s1 = filletLength * 0.5f / totLength;

                CirclePoints cp = CirclePoints.ForDiameter(fillet, maxCircleError, minCircleVertexes);

                // We need to be careful with the number of points so we don't blow the 255 point budget for colliders
                CirclePoints collCp = CirclePoints.ForDiameter(fillet, maxCircleError, 0, 12);
                CirclePoints collEnds = CirclePoints.ForDiameter(endDiameter, maxCircleError * 4f, 4, 12);
                CirclePoints collBody = CirclePoints.ForDiameter(bodyDiameter, maxCircleError * 4f, 4, 16);

                points.AddLast(new ProfilePoint(endDiameter, -0.5f * length, 0f, new Vector2(0, -1), colliderCirc: collEnds));

                foreach (Vector3 xzu in cp.PointsXZU(0.5f, 0.75f))
                    points.AddLast(new ProfilePoint(endDiameter + fillet * xzu.x, -0.5f * (bodyLength - fillet * xzu.y), s1 * Mathf.InverseLerp(0.5f, 0.75f, xzu[2]), (Vector2)xzu, inCollider: false));
                foreach (Vector3 xzu in collCp.PointsXZU(0.5f, 0.75f))
                    points.AddLast(new ProfilePoint(endDiameter + fillet * xzu.x, -0.5f * (bodyLength - fillet * xzu.y), s1 * Mathf.InverseLerp(0.5f, 0.75f, xzu[2]), (Vector2)xzu, inRender: false, colliderCirc: collEnds));

                points.AddLast(new ProfilePoint(bodyDiameter, -0.5f * bodyLength, s1, new Vector2(1, 0), colliderCirc: collBody));
                if (fillet < length)
                    points.AddLast(new ProfilePoint(bodyDiameter, 0.5f * bodyLength, 1f - s1, new Vector2(1, 0), colliderCirc: collBody));

                foreach (Vector3 xzu in cp.PointsXZU(0.75f, 1))
                    points.AddLast(new ProfilePoint(endDiameter + fillet * xzu.x, 0.5f * (bodyLength + fillet * xzu.y), 1f - s1 * Mathf.InverseLerp(1f, 0.75f, xzu[2]), (Vector2)xzu, inCollider: false));
                foreach (Vector3 xzu in collCp.PointsXZU(0.75f, 1))
                    points.AddLast(new ProfilePoint(endDiameter + fillet * xzu.x, 0.5f * (bodyLength + fillet * xzu.y), 1f - s1 * Mathf.InverseLerp(1f, 0.75f, xzu[2]), (Vector2)xzu, inRender: false, colliderCirc: collEnds));
                points.AddLast(new ProfilePoint(endDiameter, 0.5f * length, 1f, new Vector2(0, 1), colliderCirc: collEnds));
            }

            WriteMeshes(points);

            oldDiameter = diameter;
            oldLength = length;
            oldFillet = fillet;
        }

        private float CalcVolume()
        {
            // To get formula for part volume: l = length, d = diameter, f = fillet
            // body cylinder = pi * r^2 * h 
            //               = pi (d/2)^2 (l-f)
            // ends cylinder = pi * r^2 * h 
            //               = pi ((d-f)/2)^2 f
            // volume of the filleted bits by  http://mathworld.wolfram.com/PappussCentroidTheorem.html
            //               = 2 pi * area segment * location centroid
            //  area segment = 1/2 pi r^2   (semicircle)
            //               = 1/2 pi (f/2)^2
            //  centroid     = semicircle centroid + end cyl radius
            //               = 4 * r / ( 3 * pi )  + (d-f)/2
            //               = ((2f) / (3 pi) +(d-f)/2

            // So plug this into wolfram alpha:
            // simplify v = pi (d/2)^2 (l-f) + pi ((d-f)/2)^2 f + 2 pi  1/2 pi (f/2)^2  ((2f) / (3 pi) +(d-f)/2)

            // we get: v = 1/24 pi (6 d^2 l+3 (pi-4) d f^2+(10-3 pi) f^3)

            return Mathf.PI / 24f * (6f * diameter * diameter * length + 3f * (Mathf.PI - 4) * diameter * fillet * fillet + (10f - 3f * Mathf.PI) * fillet * fillet * fillet);
        }

        private float MaxMinVolume()
        {

            volume = CalcVolume();

            if (volume > pPart.volumeMax)
            {
                float excess = volume - pPart.volumeMax;
                volume = pPart.volumeMax;
                return excess;
            }
            if (volume < pPart.volumeMin)
            {
                float excess = volume - pPart.volumeMin;
                volume = pPart.volumeMin;
                return excess;
            }
            return 0;
        }
    }
}