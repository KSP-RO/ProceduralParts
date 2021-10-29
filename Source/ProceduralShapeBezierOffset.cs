using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProceduralParts
{
    public class ProceduralShapeBezierOffset : ProceduralAbstractShape
    {
        private const string ModTag = "[ProceduralShapeBezierOffset]";
        public override Vector3 CoMOffset => CoMOffset_internal();
        private const string CustomShapeName = "Custom";

        #region Config parameters

        internal class ShapePreset
        {
            [Persistent] public string name;
            [Persistent] public string displayName;
            [Persistent] public Vector4 points;
        }

        internal static readonly Dictionary<string, ShapePreset> shapePresets = new Dictionary<string, ShapePreset>();
        private ShapePreset selectedPreset;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Curve", groupName = ProceduralPart.PAWGroupName),
         UI_ChooseOption(scene = UI_Scene.Editor)]
        public string selectedShape;

        [KSPField(isPersistant = true)]
        public Vector4 shapePoints;

        [KSPField(guiName = "Curve.x", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName)]
        [UI_FloatEdit(incrementSlide = 0.01f, sigFigs = 2, maxValue = 1, minValue = 0)]
        public float curve_x;

        [KSPField(guiName = "Curve.y", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName)]
        [UI_FloatEdit(incrementSlide = 0.01f, sigFigs = 2, maxValue = 1, minValue = 0)]
        public float curve_y;

        [KSPField(guiName = "Curve.z", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName)]
        [UI_FloatEdit(incrementSlide = 0.01f, sigFigs = 2, maxValue = 1, minValue = 0)]
        public float curve_z;

        [KSPField(guiName = "Curve.w", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName)]
        [UI_FloatEdit(incrementSlide = 0.01f, sigFigs = 2, maxValue = 1, minValue = 0)]
        public float curve_w;

        #endregion

        #region Initialization

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            InitializeSelectedShape();
        }

        public override void OnStart(StartState state)
        {
            InitializeSelectedShape();
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsEditor)
            {
                UI_ChooseOption opt = Fields[nameof(selectedShape)].uiControlEditor as UI_ChooseOption;
                opt.options = shapePresets.Values.Select(x => x.name).ToArray();
                opt.display = shapePresets.Values.Select(x => x.displayName).ToArray();
                opt.onSymmetryFieldChanged += OnShapeSelectionChanged;
                opt.onFieldChanged += OnShapeSelectionChanged;

                Fields[nameof(curve_x)].uiControlEditor.onFieldChanged += OnCurveChanged;
                Fields[nameof(curve_y)].uiControlEditor.onFieldChanged += OnCurveChanged;
                Fields[nameof(curve_z)].uiControlEditor.onFieldChanged += OnCurveChanged;
                Fields[nameof(curve_w)].uiControlEditor.onFieldChanged += OnCurveChanged;

                SetFieldVisibility();
            }
        }

        private void InitializeSelectedShape()
        {
            InitializePresets();
            if (string.IsNullOrEmpty(selectedShape) || !shapePresets.ContainsKey(selectedShape))
            {
                Debug.Log($"{ModTag} InitializeSelectedShape() Shape {selectedShape} not available, defaulting to {shapePresets.Keys.First()}");
                selectedShape = shapePresets.Keys.First();
            }
            selectedPreset = shapePresets[selectedShape];
            if (!selectedPreset.name.Equals(CustomShapeName))
                shapePoints = selectedPreset.points;
            else
                (curve_x, curve_y, curve_z, curve_w) = (shapePoints.x, shapePoints.y, shapePoints.z, shapePoints.w);
        }

        private void InitializePresets()
        {
            if (shapePresets.Count == 0 &&
                GameDatabase.Instance.GetConfigNode("ProceduralParts/ProceduralParts/ProceduralPartsSettings") is ConfigNode settings)
            {
                foreach (var shapeNode in settings.GetNodes("Shape"))
                {
                    var shape = new ShapePreset();
                    if (ConfigNode.LoadObjectFromConfig(shape, shapeNode))
                        shapePresets.Add(shape.name, shape);
                }
                var s = new ShapePreset
                {
                    name = CustomShapeName,
                    displayName = CustomShapeName,
                    points = Vector4.zero
                };
                shapePresets.Add(s.name, s);
            }
        }

        public override void UpdateTechConstraints()
        {
            InitializeSelectedShape();
        }

        #endregion

        #region Update handlers

        private void OnCurveChanged(BaseField f, object obj)
        {
            shapePoints = new Vector4(curve_x, curve_y, curve_z, curve_w);
            OnShapeChanged(f, obj);
        }

        public void OnShapeSelectionChanged(BaseField f, object obj)
        {
            selectedPreset = shapePresets[selectedShape];
            shapePoints = selectedPreset.points;
            SetFieldVisibility();
            OnShapeChanged(f, obj, true);
        }

        public void OnShapeChanged(BaseField f, object obj, bool forceRefresh = false)
        {
            float v = CalculateVolume();
            if (v > PPart.volumeMax || v < PPart.volumeMin)
            {
                UI_FloatEdit edt = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
                length = edt.minValue;
                AdjustDimensionBounds();
                length = Mathf.Clamp(length, edt.minValue, edt.maxValue);
            }
            OnShapeDimensionChanged(f, obj);
            if (forceRefresh)
                MonoUtilities.RefreshPartContextWindow(part);
        }

        private void SetFieldVisibility()
        {
            Fields[nameof(selectedShape)].guiActiveEditor = PPart.allowCurveTweaking;
            bool showPointEditor = selectedPreset.name == CustomShapeName && PPart.allowCurveTweaking;
            Fields[nameof(curve_x)].guiActiveEditor = showPointEditor;
            Fields[nameof(curve_y)].guiActiveEditor = showPointEditor;
            Fields[nameof(curve_z)].guiActiveEditor = showPointEditor;
            Fields[nameof(curve_w)].guiActiveEditor = showPointEditor;
        }

        internal override void UpdateShape(bool force = true)
        {
            Volume = CalculateVolume();
            SetControlPoints(length, topDiameter, bottomDiameter);
            // Ensure correct control points if something called AdjustDimensionBounds or CalculateVolume when handling volume change event
            WriteBezier();
            part.CoMOffset = CoMOffset;
        }

        public override void AdjustDimensionBounds()
        {
            float maxLength = PPart.lengthMax;
            float maxBottomDiameter = PPart.diameterMax;
            float maxTopDiameter = PPart.diameterMax;
            float minLength = PPart.lengthMin;
            float minTopDiameter = (coneTopMode == ConeEndMode.CanZero && coneBottomMode != ConeEndMode.Constant) ? 0 : PPart.diameterMin;
            float minBottomDiameter = (coneBottomMode == ConeEndMode.CanZero && coneTopMode != ConeEndMode.Constant) ? 0 : PPart.diameterMin;

            float minBottomDiameterOrig = minBottomDiameter;
            float minTopDiameterOrig = minTopDiameter;

            if (PPart.volumeMax < float.PositiveInfinity)
            {
                maxBottomDiameter = bottomDiameter;
                maxTopDiameter = topDiameter;
                maxLength = length;
                IterateVolumeLimits(ref maxTopDiameter, ref maxBottomDiameter, ref maxLength, PPart.volumeMax, IteratorIncrement);
            }
            if (PPart.volumeMin > 0)
            {
                minBottomDiameter = bottomDiameter;
                minTopDiameter = topDiameter;
                minLength = length;
                IterateVolumeLimits(ref minTopDiameter, ref minBottomDiameter, ref minLength, PPart.volumeMin, IteratorIncrement);
            }

            maxLength = Mathf.Clamp(maxLength, PPart.lengthMin, PPart.lengthMax);
            maxTopDiameter = Mathf.Clamp(maxTopDiameter, PPart.diameterMin, PPart.diameterMax);
            maxBottomDiameter = Mathf.Clamp(maxBottomDiameter, PPart.diameterMin, PPart.diameterMax);
            minLength = Mathf.Clamp(minLength, PPart.lengthMin, PPart.lengthMax - PPart.lengthSmallStep);
            minTopDiameter = Mathf.Clamp(minTopDiameter, minTopDiameterOrig, PPart.diameterMax - PPart.diameterSmallStep);
            minBottomDiameter = Mathf.Clamp(minBottomDiameter, minBottomDiameterOrig, PPart.diameterMax - PPart.diameterSmallStep);

            (Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxTopDiameter;
            (Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxBottomDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).maxValue = maxLength;
            (Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit).minValue = minTopDiameter;
            (Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit).minValue = minBottomDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).minValue = minLength;
        }

        // IteratorIncrement is approximately 1/1000.
        // For any sizable volume limit, simple iteration will be very slow.
        // Instead, search by variable increment.  Minimal harm in setting scale default high, because
        // early passes will terminate on first loop.
        private void IterateVolumeLimits(ref float top, ref float bottom, ref float len, float target, float inc, int scale=6)
        {
            if (inc <= 0) return;

            float originalTop = top, originalBottom = bottom, originalLen = len;
            top = bottom = len = 0;
            while (scale-- >= 0)
            {
                float curInc = inc * Mathf.Pow(10, scale);
                while (CalculateVolume(len + curInc, originalTop, originalBottom) < target && len + curInc < PPart.lengthMax)
                {
                    len += curInc;
                }
                while (CalculateVolume(originalLen, top + curInc, originalBottom) < target && top + curInc < PPart.diameterMax)
                {
                    top += curInc;
                }
                while (CalculateVolume(originalLen, originalTop, bottom + curInc) < target && bottom + curInc < PPart.diameterMax)
                {
                    bottom += curInc;
                }
            }
        }

        public override float CalculateVolume(float length, float topDiameter, float bottomDiameter)
        {
            SetControlPoints(length, topDiameter, bottomDiameter);
            // The maths for the area under the bezier can be calculated using 
            // pappus's centroid theroem: http://mathworld.wolfram.com/PappussCentroidTheorem.html
            // V = 2pi * Area of lamina * x geometric centroid y axis
            // geometric centroid about y axis = Moment about y / Area of curve
            // so area under curve ends up factoring out.

            // factor 1/2 taken out. The 1/4 is because the pN.x are diameters not radii.
            // ReSharper disable once InconsistentNaming
            float M_y  = 1f/4f* ((p1.y-p0.y)*(1f/3f * p0.x*p0.x + 1f/4f * p0.x*p1.x + 1f/14f* p0.x*p2.x + 3f/28f* p1.x*p1.x+ 3f/28f * p1.x*p2.x + 1f/84f * p0.x*p3.x + 3f/70f * p2.x*p2.x + 1f/35f* p1.x*p3.x + 1f/28f* p2.x*p3.x + 1f/84f* p3.x*p3.x) +
                                 (p2.y-p1.y)*(1f/12f* p0.x*p0.x + 1f/7f * p0.x*p1.x + 1f/14f* p0.x*p2.x + 3f/28f* p1.x*p1.x+ 6f/35f * p1.x*p2.x + 2f/105f* p0.x*p3.x + 3f/28f * p2.x*p2.x + 1f/14f* p1.x*p3.x + 1f/7f * p2.x*p3.x + 1f/12f* p3.x*p3.x) +
                                 (p3.y-p2.y)*(1f/84f* p0.x*p0.x + 1f/28f* p0.x*p1.x + 1f/35f* p0.x*p2.x + 3f/70f* p1.x*p1.x+ 3f/28f * p1.x*p2.x + 1f/84f * p0.x*p3.x + 3f/28f * p2.x*p2.x + 1f/14f* p1.x*p3.x + 1f/4f * p2.x*p3.x + 1f/3f * p3.x*p3.x));

            return Mathf.PI * M_y;
        }

        public override bool SeekVolume(float targetVolume, int dir=0) => SeekVolume(targetVolume, Fields[nameof(length)], dir);

        private Vector3 CoMOffset_internal()
        {
            //CoM integral over x(t)^2*y'(t):
            // int_0->1:y(t)*x(t)^2*y'(t) dt / int_0->1:x(t)^2*y'(t) dt
            //where x(t), x'(t), y(t) are from the Bezier function

            //Function below derived using symbolic math in Matlab
            float x0 = p0.x, x1 = p1.x, x2 = p2.x, x3 = p3.x, y0 = p0.y, y1 = p1.y, y2 = p2.y, y3 = p3.y;
            float num = (2310f*y0*y0 - 1260f*y0*y1 - 252f*y0*y2 - 28f*y0*y3 - 378f*y1*y1 - 252f*y1*y2 - 42f*y1*y3 - 63f*y2*y2 - 30f*y2*y3 - 5f*y3*y3)*x0*x0 +
                        (1260f*x1*y0*y0 - 252f*x1*y1*y1 + 252f*x2*y0*y0 - 162f*x1*y2*y2 + 28f*x3*y0*y0 - 30f*x1*y3*y3 - 90f*x2*y2*y2 + 18f*x3*y1*y1 - 42f*x2*y3*y3 - 18f*x3*y2*y2 - 28f*x3*y3*y3 - 168f*x1*y0*y2 +
                        168f*x2*y0*y1 - 42f*x1*y0*y3 - 378f*x1*y1*y2 + 42f*x3*y0*y1 - 108f*x1*y1*y3 - 12f*x2*y0*y3 - 108f*x2*y1*y2 + 12f*x3*y0*y2 - 120f*x1*y2*y3 - 60f*x2*y1*y3 - 108f*x2*y2*y3 - 12f*x3*y1*y3 - 42f*x3*y2*y3)*x0 +
                        378f*x1*x1*y0*y0 + 252f*x1*x1*y0*y1 - 18f*x1*x1*y0*y3 - 162f*x1*x1*y1*y2 - 90f*x1*x1*y1*y3 - 135f*x1*x1*y2*y2 - 162f*x1*x1*y2*y3 - 63f*x1*x1*y3*y3 + 252f*x1*x2*y0*y0 + 378f*x1*x2*y0*y1 + 108f*x1*x2*y0*y2 +
                        162f*x1*x2*y1*y1 - 108f*x1*x2*y1*y3 - 162f*x1*x2*y2*y2 - 378f*x1*x2*y2*y3 - 252f*x1*x2*y3*y3 + 42f*x1*x3*y0*y0 + 108f*x1*x3*y0*y1 + 60f*x1*x3*y0*y2 + 12f*x1*x3*y0*y3 + 90f*x1*x3*y1*y1 + 108f*x1*x3*y1*y2 -
                        168f*x1*x3*y2*y3 - 252f*x1*x3*y3*y3 + 63f*x2*x2*y0*y0 + 162f*x2*x2*y0*y1 + 90f*x2*x2*y0*y2 + 18f*x2*x2*y0*y3 + 135f*x2*x2*y1*y1 + 162f*x2*x2*y1*y2 - 252f*x2*x2*y2*y3 - 378f*x2*x2*y3*y3 + 30f*x2*x3*y0*y0 +
                        120f*x2*x3*y0*y1 + 108f*x2*x3*y0*y2 + 42f*x2*x3*y0*y3 + 162f*x2*x3*y1*y1 + 378f*x2*x3*y1*y2 + 168f*x2*x3*y1*y3 + 252f*x2*x3*y2*y2 - 1260f*x2*x3*y3*y3 + 5f*x3*x3*y0*y0 + 30f*x3*x3*y0*y1 + 42f*x3*x3*y0*y2 +
                        28f*x3*x3*y0*y3 + 63f*x3*x3*y1*y1 + 252f*x3*x3*y1*y2 + 252f*x3*x3*y1*y3 + 378f*x3*x3*y2*y2 + 1260f*x3*x3*y2*y3 - 2310f*x3*x3*y3*y3;

            float den = (3080f*y0 - 2310f*y1 - 660f*y2 - 110f*y3)*x0*x0 +
                        (2310f*x1*y0 - 990f*x1*y1 + 660f*x2*y0 - 990f*x1*y2 + 110f*x3*y0 - 330f*x1*y3 - 396f*x2*y2 + 66f*x3*y1 - 264f*x2*y3 - 66f*x3*y2 - 110f*x3*y3)*x0 +
                        990f*x1*x1*y0 + 396f*x2*x2*y0 - 594f*x1*x1*y2 + 594f*x2*x2*y1 + 110f*x3*x3*y0 - 396f*x1*x1*y3 + 660f*x3*x3*y1 - 990f*x2*x2*y3 + 2310f*x3*x3*y2 -
                        3080f*x3*x3*y3 + 990f*x1*x2*y0 + 594f*x1*x2*y1 + 264f*x1*x3*y0 - 594f*x1*x2*y2 + 396f*x1*x3*y1 + 330f*x2*x3*y0 - 990f*x1*x2*y3 + 990f*x2*x3*y1 - 660f*x1*x3*y3 + 990f*x2*x3*y2 - 2310f*x2*x3*y3;

            return new Vector3(0, num/den, 0);
        }

        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            CalculateVolume();  // Update the bezier curve control nodes
            // The Bezier curve control points are NOT normalized.
            //  p0 = new Vector2(bottomDiameter, -length / 2f);
            //  p3 = new Vector2(topDiameter, length / 2f);
            // But we do need to normalize the length and shift such that 0 <= t <= 1
            float t = (coords.y / length) + (1f / 2);
            Vector2 Bt = B(t);
            Debug.Log($"{ModTag} Normalized {coords.y} to {t} (0=bottom, 1=top), B(t)={Bt} as (diameter, length). TopDiameter:{topDiameter} BotDiameter:{bottomDiameter} Len: {length}");
            // For a given normalized length (t), B(t).x is the diameter of the surface cross-section at that length, and B(t).y is .. what?

            coords.y /= length;
            coords.r /= (Bt.x / 2);
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            CalculateVolume();  // Update the bezier curve control nodes
            float t = (coords.y + (1f / 2));    // Shift coords.y from [-0.5..0.5] to [0..1]
            Vector2 Bt = B(t);
            Debug.Log($"{ModTag} From normalized {coords.y}, shape unnormalized bottom {bottomDiameter} top {topDiameter} length {length},  B({t}) yields [{Bt.x:F3}, {Bt.y:F3}] as (diameter, length)");
            // B(t).x is the diameter of the curve at t.  /2 for radius.

            coords.y *= length;
            coords.r *= Bt.x / 2;
        }

        #endregion

        #region Control point calculation
        private Vector2 p0, p1, p2, p3;

        private void SetControlPoints(float length, float topDiameter, float bottomDiameter)
        {
            // So we have a rotated bezier curve from bottom to top.
            // There are four control points, the bottom (p0) and the top ones (p3) are obvious
            p0 = new Vector2(bottomDiameter, -length / 2f);
            p3 = new Vector2(topDiameter, length / 2f);
            float[] shape = { shapePoints.x, shapePoints.y, shapePoints.z, shapePoints.w };

            // Pretty obvious below what the shape points mean
            if (bottomDiameter < topDiameter)
            {
                p1 = new Vector2(Mathf.Lerp(p0.x, p3.x, shape[0]), Mathf.Lerp(p0.y, p3.y, shape[1]));
                p2 = new Vector2(Mathf.Lerp(p0.x, p3.x, shape[2]), Mathf.Lerp(p0.y, p3.y, shape[3]));
            }
            else
            {
                p2 = new Vector2(Mathf.Lerp(p3.x, p0.x, shape[0]), Mathf.Lerp(p3.y, p0.y, shape[1]));
                p1 = new Vector2(Mathf.Lerp(p3.x, p0.x, shape[2]), Mathf.Lerp(p3.y, p0.y, shape[3]));
            }
        }


        private Vector3 CalcCoMOffset() => default;    // Nope.  Just nope.

        #endregion

        #region Bezier Bits

        [KSPField]
        public bool showHull = false;

        private void WriteBezier()
        {
            WriteShape();
        }

        private void WriteShape()
        {
            LinkedList<ProfilePoint> points = new LinkedList<ProfilePoint>();

            int colliderTri = 0;

            points.AddLast(CreatePoint(0, ref colliderTri));
            points.AddLast(CreatePoint(1, ref colliderTri));

            colliderTri /= 2;

            Queue<LinkedListNode<ProfilePoint>> process = new Queue<LinkedListNode<ProfilePoint>>();
            process.Enqueue(points.First);

            while (process.Count > 0)
            {
                LinkedListNode<ProfilePoint> node = process.Dequeue();
                ProfilePoint pM = node.Value;
                // ReSharper disable once PossibleNullReferenceException
                ProfilePoint pN = node.Next.Value;

                float tM = pM.v;
                float tN = pN.v;

                // So we want to find the point where the curve is maximally distant from the line between pM and pN

                // First we need the normal to the line:
                Vector2 norm = new Vector2(-pN.y + pM.y, pN.dia - pM.dia);

                // The deviation is:
                // Dev = B(t) . norm - B(m) . norm    (where m = t at point M)

                // We want to know the maxima, so take the derivative and solve for = 0
                // Dev' = B'(t) . norm
                //      = 3(1-t)^2 ((p1.x-p0.x) norm.x + (p1.y-p0.y) norm.y) + 6t(1-t) ((p2.x-p1.x) norm.x + (p2.y-p1.y) norm.y) + 3t^2 ((p3.x-p2.x) norm.x + (p3.y-p2.y) norm.y) = 0

                // This is a quadratic, which we can solve directly.

                float a = ((p1.x - p0.x) * norm.x + (p1.y - p0.y) * norm.y);
                float b = ((p2.x - p1.x) * norm.x + (p2.y - p1.y) * norm.y);
                float c = ((p3.x - p2.x) * norm.x + (p3.y - p2.y) * norm.y);

                // solve a (1-t)^2+2 b (t (1-t))+c t^2 = 0

                // t = (-/+ sqrt(b^2-a c)-a+b)/(-a+2 b-c)   for  a-2 b+c!=0
                // t = (2 b-c)/(2 (b-c))                    for  a = 2 b-c and b-c!=0

                List<float> ts = new List<float>(2);
                //Debug.LogWarning(string.Format("t={0:F3}..{1:F3} perp=({2:F3}, {3:F3}) a={4:F3} b={5:F3} c={6:F3}", tM, tN, norm.x, norm.y, a, b, c));

                if (Math.Abs(a - 2 * b + c) < 1e-6f)
                {
                    if (Math.Abs(b - c) < 1e-6f)
                    {
                        // This is the straight line case, no need to subdivide
                        continue;
                    }
                    float t1 = (2f * b - c) / (2f * (b - c));
                    //Debug.LogWarning(string.Format("t={0:F3}..{1:F3} -> {2:F3}", tM, tN, t1));

                    ts.Add(t1);
                }
                else
                {
                    float sqrt = Mathf.Sqrt(b * b - a * c);

                    float t1 = (sqrt - a + b) / (-a + 2 * b - c);
                    float t2 = (-sqrt - a + b) / (-a + 2 * b - c);
                    //Debug.LogWarning(string.Format("t={0:F3}..{1:F3} -> {2:F3} {3:F3} ", tM, tN, t1, t2));


                    ts.Add(t1);
                    ts.Add(t2);

                    ts.Sort();
                }


                for (int i = 0; i < ts.Count; ++i)
                {
                    if (ts[i] < tM || ts[i] > tN)
                        ts.RemoveAt(i--);
                }

                if (ts.Count == 0)
                    throw new InvalidProgramException("There should be a point maximally distant from the line or the maths is really wrong.");

                norm = norm.normalized;
                float devM = pM.dia * norm.x + pM.y * norm.y;

                for (int i = 0; i < ts.Count; ++i)
                {
                    // The difference from the line
                    float devTS = Vector2.Dot(B(ts[i]), norm) - devM;

                    if (Mathf.Abs(devTS) < MaxCircleError)
                        ts.RemoveAt(i--);
                }

                switch (ts.Count)
                {
                    case 0:
                        break;
                    case 1:
                        LinkedListNode<ProfilePoint> next = node.List.AddAfter(node, CreatePoint(ts[0], ref colliderTri));
                        process.Enqueue(node);
                        process.Enqueue(next);
                        break;
                    case 2:
                        LinkedListNode<ProfilePoint> next0 = node.List.AddAfter(node, CreatePoint(ts[0], ref colliderTri));
                        LinkedListNode<ProfilePoint> next1 = node.List.AddAfter(next0, CreatePoint(ts[1], ref colliderTri));

                        process.Enqueue(node);
                        process.Enqueue(next0);
                        process.Enqueue(next1);
                        break;
                }
            }


            // Need to figure out the v coords.
            float sumLengths = 0;
            float[] cumLengths = new float[points.Count - 1];

            LinkedListNode<ProfilePoint> pv = points.First;
            LinkedListNode<ProfilePoint> nx = pv.Next;
            for (int i = 0; i < cumLengths.Length; ++i, pv = nx, nx = nx.Next)
            {
                // ReSharper disable once PossibleNullReferenceException
                float dX = nx.Value.dia - pv.Value.dia;
                float dY = nx.Value.y - pv.Value.y;

                cumLengths[i] = sumLengths += Mathf.Sqrt(dX * dX + dY * dY);
            }

            points.First.Value.v = 0;
            nx = points.First.Next;
            for (int i = 0; i < cumLengths.Length; ++i, nx = nx.Next)
            {
                // ReSharper disable once PossibleNullReferenceException
                nx.Value.v = cumLengths[i] / sumLengths;
            }


            WriteMeshes(points);
        }

        private ProfilePoint CreatePoint(float t, ref int colliderTri)
        {
            // ReSharper disable once InconsistentNaming
            // B(t) = (1-t)^3 p0 + t(1-t)^2 p1 + t^2(1-t) p2 + t^3 p3
            Vector2 Bt = B(t);

            // ReSharper disable once InconsistentNaming
            // B'(t) = (1-t)^2 (p1-p0) + t(1-t) (p2-p1) + t^2 (p3-p2)
            Vector2 Btdt = Bdt(t);

            // normalized perpendicular to tangent (derivative)
            Vector2 norm = new Vector2(Btdt.y, -Btdt.x / 2f).normalized;

            // Count the number of triangles
            CirclePoints colliderCirc = CirclePoints.ForDiameter(Bt.x, MaxCircleError * 4f, 4, 16);
            colliderTri += (colliderCirc.totVertexes + 1) * 2;

            //Debug.LogWarning(string.Format("Creating profile point t={0:F3} coord=({1:F3}, {2:F3})  normal=({3:F3}, {4:F3})", t, Bt.x, Bt.y, norm.x, norm.y));

            // We can have a maxium of 255 triangles in the collider. Will leave a bit of breathing room at the top.
            return colliderTri <= 220 ? 
                new ProfilePoint(Bt.x, Bt.y, t, norm, colliderCirc: colliderCirc) : 
                new ProfilePoint(Bt.x, Bt.y, t, norm, inCollider: false);
        }

        private Vector2 B(float t)
        {
            return (1 - t) * (1 - t) * (1 - t) * p0 + 3 * t * (1 - t) * (1 - t) * p1 + 3 * t * t * (1 - t) * p2 + t * t * t * p3;
        }

        private Vector2 Bdt(float t)
        {
            return 3 * (1 - t) * (1 - t) * (p1 - p0) + 6 * t * (1 - t) * (p2 - p1) + 3 * t * t * (p3 - p2);
        }

        private float BSolverGetT(float p0, float p1, float p2, float p3, float value)
        {
            // a*t^3+b*t^2+c*t+d
            float a = 3*p1 - p0 - 3*p2 + p3;
            float b = 3*p0 - 6*p1 + 3*p2;
            float c = 3*p1 - 3*p0;
            float d = p0-value;

            return t;
        }

        #endregion

    }
}
