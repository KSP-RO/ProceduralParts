using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralParts
{
    public class ProceduralShapePill : ProceduralAbstractSoRShape
    {
        private static readonly string ModTag = "[ProceduralShapePill]";

        #region Config parameters

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Diameter", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float diameter = 1.25f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Length", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float length = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Fillet", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float fillet = 1f;

        #endregion

        #region Initialization

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsEditor)
            {
                UpdateTechConstraints();
                Fields[nameof(diameter)].uiControlEditor.onFieldChanged = ClampFillet;
                Fields[nameof(diameter)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;

                Fields[nameof(length)].uiControlEditor.onFieldChanged = ClampFillet;
                Fields[nameof(length)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;

                Fields[nameof(fillet)].uiControlEditor.onFieldChanged = ClampFillet;
                Fields[nameof(fillet)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;

                Fields[nameof(diameter)].uiControlEditor.onSymmetryFieldChanged =
                Fields[nameof(length)].uiControlEditor.onSymmetryFieldChanged =
                Fields[nameof(fillet)].uiControlEditor.onSymmetryFieldChanged = ClampFillet;
            }
        }

        public override void UpdateTechConstraints()
        {
            Fields[nameof(length)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit lengthEdit = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
            lengthEdit.incrementLarge = PPart.lengthLargeStep;
            lengthEdit.incrementSmall = PPart.lengthSmallStep;

            Fields[nameof(diameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit diameterEdit = Fields[nameof(diameter)].uiControlEditor as UI_FloatEdit;
            diameterEdit.incrementLarge = PPart.diameterLargeStep;
            diameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(fillet)].guiActiveEditor = PPart.allowCurveTweaking;
            UI_FloatEdit filletEdit = Fields[nameof(fillet)].uiControlEditor as UI_FloatEdit;
            filletEdit.incrementLarge = PPart.diameterLargeStep;
            filletEdit.incrementSmall = PPart.diameterSmallStep;

            AdjustDimensionBounds();
            length = Mathf.Clamp(length, lengthEdit.minValue, lengthEdit.maxValue);
            diameter = Mathf.Clamp(diameter, diameterEdit.minValue, diameterEdit.maxValue);
            fillet = Mathf.Clamp(fillet, filletEdit.minValue, filletEdit.maxValue);
        }

        #endregion

        #region Update handlers

        // A few shortcuts to use in formulas.
        private const float Pi = Mathf.PI;
        private static readonly Func<float, float> sqrt = Mathf.Sqrt;
        private static readonly Func<float, float, float> pow = Mathf.Pow;

        private void ClampFillet(BaseField f, object obj)
        {
            if (fillet > Mathf.Min(diameter, length))
            {
                fillet = Mathf.Min(diameter, length);
                MonoUtilities.RefreshPartContextWindow(part);
            }
        }

        public override void AdjustDimensionBounds()
        {
            // v = 1/24 pi (6 d^2 l+3 (pi-4) d f^2+(10-3 pi) f^3) for d
            // simplify d = ((-3 pi^2 f^2+12 pi f^2) ± sqrt(3 pi) sqrt(3 pi^3 f^4-24 pi^2 f^4+48 pi f^4+24 pi^2 f^3 l-80 pi f^3 l+192 l v))/(12 pi l) 
            // d = (-3 (pi-4) pi f^2 ± sqrt(3 pi) sqrt(3 (pi-4)^2 pi f^4+8 pi (3 pi-10) f^3 l+192 l v)) / (12 pi l)
            // l = (-3 (pi-4) pi d f^2+pi (3 pi-10) f^3+24 v)/(6 pi d^2) 

            float maxDiameter = PPart.diameterMax;
            float maxLength = PPart.lengthMax;
            float maxFillet = Mathf.Min(diameter, length);

            float minDiameter = PPart.diameterMin;
            float minLength = PPart.lengthMin;
            float minFillet = 0;

            float t1 = -3 * (Mathf.PI - 4f) * Mathf.PI * fillet * fillet;
            float de = 12f * Mathf.PI * length;
            if (PPart.volumeMax < float.PositiveInfinity)
            {
                float t2 = sqrt(3f * Pi) * sqrt(3f * pow(Pi - 4f, 2) * Pi * pow(fillet, 4) + 8f * Pi * (3f * Pi - 10f) * pow(fillet, 3) * length + 192f * length * PPart.volumeMax);
                float x = (t1 + t2) > 0 ? (t1 + t2) / de : (t1 - t2) / de;
                maxDiameter = !float.IsNaN(x) ? x : maxDiameter;
                maxLength = ((-3f * (Pi - 4f) * Pi * diameter * pow(fillet, 2)) + (Pi * ((3f * Pi) - 10f) * pow(fillet, 3)) + (24f * PPart.volumeMax)) / (6f * Pi * pow(diameter, 2));
                IterateVolumeLimits(length, diameter, ref minFillet, PPart.volumeMax, IteratorIncrement);
            }

            if (PPart.volumeMin > 0)
            {
                float t2min = sqrt(3f * Pi) * sqrt(3f * pow(Pi - 4f, 2) * Pi * pow(fillet, 4) + 8f * Pi * (3f * Pi - 10f) * pow(fillet, 3) * length + 192f * length * PPart.volumeMin);
                float x = (t1 + t2min) > 0 ? (t1 + t2min) / de : (t1 - t2min) / de;
                minDiameter = !float.IsNaN(x) ? x : minDiameter;
                minLength = ((-3f * (Pi - 4f) * Pi * diameter * pow(fillet, 2)) + (Pi * ((3f * Pi) - 10f) * pow(fillet, 3)) + (24f * PPart.volumeMin)) / (6f * Pi * pow(diameter, 2));
                IterateVolumeLimits(length, diameter, ref maxFillet, PPart.volumeMin, IteratorIncrement);
            }

            maxLength = Mathf.Clamp(maxLength, PPart.lengthMin, PPart.lengthMax);
            maxDiameter = Mathf.Clamp(maxDiameter, PPart.diameterMin, PPart.diameterMax);

            minLength = Mathf.Clamp(minLength, PPart.lengthMin, PPart.lengthMax - PPart.lengthSmallStep);
            minDiameter = Mathf.Clamp(minDiameter, PPart.diameterMin, PPart.diameterMax - PPart.diameterSmallStep);

            (Fields[nameof(diameter)].uiControlEditor as UI_FloatEdit).maxValue = maxDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).maxValue = maxLength;
            (Fields[nameof(fillet)].uiControlEditor as UI_FloatEdit).maxValue = maxFillet;

            (Fields[nameof(diameter)].uiControlEditor as UI_FloatEdit).minValue = minDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).minValue = minLength;
            (Fields[nameof(fillet)].uiControlEditor as UI_FloatEdit).minValue = minFillet;
        }

        // Variant from ProceduralShapeBezierCone.IterateVolumeLimits()
        private void IterateVolumeLimits(float length, float diameter, ref float fillet, float target, float inc, int scale = 6)
        {
            // Increasing fillet decreases volume
            // Seek the fillet setting that gives the requested volume, or hits its limit
            if (inc <= 0) return;
            fillet = 0;
            float maxFillet = Mathf.Min(length, diameter);
            while (scale-- >= 0)
            {
                float curInc = inc * Mathf.Pow(10, scale);
                while (CalculateVolume(length, diameter, fillet + curInc) > target && fillet + curInc < maxFillet)
                {
                    fillet += curInc;
                }
            }
        }

        internal override void UpdateShape(bool forceUpdate=true)
        {
            part.CoMOffset = CoMOffset;
            Volume = CalculateVolume();
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
                float endDiameter = diameter - fillet;
                float bodyDiameter = diameter;

                float filletLength = Mathf.PI * fillet * 0.5f;
                float totLength = filletLength + bodyLength;
                float s1 = filletLength * 0.5f / totLength;

                CirclePoints cp = CirclePoints.ForDiameter(fillet, MaxCircleError, MinCircleVertexes);

                // We need to be careful with the number of points so we don't blow the 255 point budget for colliders
                CirclePoints collCp = CirclePoints.ForDiameter(fillet, MaxCircleError, 0, 12);
                CirclePoints collEnds = CirclePoints.ForDiameter(endDiameter, MaxCircleError * 4f, 4, 12);
                CirclePoints collBody = CirclePoints.ForDiameter(bodyDiameter, MaxCircleError * 4f, 4, 16);

                points.AddLast(new ProfilePoint(endDiameter, -0.5f * length, 0f, new Vector2(0, -1), colliderCirc: collEnds));

                foreach (Vector3 xzu in cp.PointsXZU(0.5f, 0.75f))
                    points.AddLast(new ProfilePoint(endDiameter + fillet * xzu.x, -0.5f * (bodyLength - fillet * xzu.y), s1 * Mathf.InverseLerp(0.5f, 0.75f, xzu[2]), xzu, inCollider: false));
                foreach (Vector3 xzu in collCp.PointsXZU(0.5f, 0.75f))
                    points.AddLast(new ProfilePoint(endDiameter + fillet * xzu.x, -0.5f * (bodyLength - fillet * xzu.y), s1 * Mathf.InverseLerp(0.5f, 0.75f, xzu[2]), xzu, inRender: false, colliderCirc: collEnds));

                points.AddLast(new ProfilePoint(bodyDiameter, -0.5f * bodyLength, s1, new Vector2(1, 0), colliderCirc: collBody));
                if (fillet < length)
                    points.AddLast(new ProfilePoint(bodyDiameter, 0.5f * bodyLength, 1f - s1, new Vector2(1, 0), colliderCirc: collBody));

                foreach (Vector3 xzu in cp.PointsXZU(0.75f, 1))
                    points.AddLast(new ProfilePoint(endDiameter + fillet * xzu.x, 0.5f * (bodyLength + fillet * xzu.y), 1f - s1 * Mathf.InverseLerp(1f, 0.75f, xzu[2]), xzu, inCollider: false));
                foreach (Vector3 xzu in collCp.PointsXZU(0.75f, 1))
                    points.AddLast(new ProfilePoint(endDiameter + fillet * xzu.x, 0.5f * (bodyLength + fillet * xzu.y), 1f - s1 * Mathf.InverseLerp(1f, 0.75f, xzu[2]), xzu, inRender: false, colliderCirc: collEnds));
                points.AddLast(new ProfilePoint(endDiameter, 0.5f * length, 1f, new Vector2(0, 1), colliderCirc: collEnds));
            }

            WriteMeshes(points);
        }

        public override float CalculateVolume() => CalculateVolume(length, diameter, fillet);
        public virtual float CalculateVolume(float length, float diameter, float fillet)
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

        public override bool SeekVolume(float targetVolume, int dir=0)
        {
            BaseField field = Fields[nameof(length)];
            float orig = (float) field.GetValue(this);
            float maxLength = (field.uiControlEditor as UI_FloatEdit).maxValue;
            float minLength = (field.uiControlEditor as UI_FloatEdit).minValue;
            float precision = (field.uiControlEditor as UI_FloatEdit).incrementSlide;

            // Solve length directly, taken from AdjustDimensionBounds
            float targetLength = (-3f * (Pi - 4f) * Pi * diameter * pow(fillet, 2) + Pi * (3f * Pi - 10f) * pow(fillet, 3) + 24f * targetVolume) / (6f * Pi * pow(diameter, 2));
            targetLength = RoundToDirection(targetLength / precision, dir) * precision;
            float clampedTargetLength = Mathf.Clamp(targetLength, minLength, maxLength);
            bool closeEnough = Mathf.Abs((clampedTargetLength / targetLength) - 1) < 0.01;

            field.SetValue(targetLength, this);
            OnShapeDimensionChanged(field, orig);
            MonoUtilities.RefreshPartContextWindow(part);
            return closeEnough;
        }

        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", diameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", fillet, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }

        public override void TranslateAttachmentsAndNodes(BaseField f, object obj)
        {
            if (f.name == nameof(diameter) && obj is float oldDiameter)
            {
                HandleDiameterChange((float)f.GetValue(this), oldDiameter);
            }
            else if (f.name == nameof(fillet))
            {
                //HandleDiameterChange(f, obj);
            }
            if (f.name == nameof(length) && obj is float oldLen)
            {
                HandleLengthChange((float)f.GetValue(this), oldLen);
            }
        }

        internal override void InitializeAttachmentNodes() => InitializeAttachmentNodes(length, diameter);

        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r /= (diameter / 2);
            coords.y /= length;
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r *= (diameter / 2);
            coords.y *= length;
        }
        #endregion
    }
}