using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;
using System.Reflection;

namespace ProceduralParts
{

    public class ProceduralShapeBezierCone : ProceduralShapeCone
    {

        private static float[][] shapePresets
            = new float[][] { 
            new float[] { 0.3f, 0.3f, 0.7f, 0.7f },
            new float[] { 0.4f, 0.001f, 1.0f, 0.6f }, 
            new float[] { 0.5f, 0.001f, 0.8f, 0.7f }, 
            new float[] { 0.4f, 0.2f, 0.8f, 0.6f },
            new float[] { 0.3f, 0.2f, 1.0f, 0.5f },
            new float[] { 0.1f, 0.001f, 0.7f, 2f/3f },
            new float[] { 1f/3f, 0.3f, 1.0f, 0.9f }
        };

        private static string[] shapeNames =
        {
            "Straight",
            "Round #1",
            "Round #2",
            "Peaked #1",
            "Peaked #2",
            "Sharp #1",
            "Sharp #2"
        };

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Curve"),
         UI_ChooseOption(scene = UI_Scene.Editor)]
        public int curveIdx = 1;
        private int oldCurveIdx;


        public override void OnStart(StartState state)
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            if (!pPart.allowCurveTweaking)
                Fields["curveIdx"].guiActiveEditor = false;
            else
            {
                UI_ChooseOption curveIdxEdit = (UI_ChooseOption)Fields["curveIdx"].uiControlEditor;
                curveIdxEdit.options = shapeNames;
            }
            base.OnStart(state);
        }

        protected override void UpdateShape(bool force)
        {
            if (!force && oldTopDiameter == topDiameter && oldBottomDiameter == bottomDiameter && oldLength == length &&
                curveIdx == oldCurveIdx)
                return;

            if (HighLogic.LoadedSceneIsEditor)
            {
                MaintainParameterRelations();

                UpdateVolumeRange();
            }
            else
            {
                volume = CalcVolume();
            }

            WriteBezier();

            oldTopDiameter = topDiameter; oldBottomDiameter = bottomDiameter; oldLength = length;
            oldCurveIdx = curveIdx;
        }

        #region Control point calculation and volume limits

        private Vector2 p0, p1, p2, p3;

        private void UpdateVolumeRange()
        {
            volume = CalcVolume();

            float vol = volume;
            float inc;
            if (volume < pPart.volumeMin)
            {
                volume = pPart.volumeMin;
                inc = 0.001f;
            }
            else if (volume > pPart.volumeMax)
            {
                volume = pPart.volumeMax;
                inc = -0.001f;
            }
            else
                return;

            if (length != oldLength || curveIdx != oldCurveIdx)
            {
                // The volume is directly proportional to the length
                length *= volume / vol;

                volume = CalcVolume();
            }
            else if (bottomDiameter != oldBottomDiameter)
            {
                IterateLimitVolume(ref bottomDiameter, vol, inc);
            }
            else if (topDiameter != oldTopDiameter)
            {
                IterateLimitVolume(ref topDiameter, vol, inc);
            }
        }

        private void IterateLimitVolume(ref float toTweak, float vol, float inc)
        {
            float oToTweak = toTweak;
            float lVol;
            float lToTweak;
            int count = 1;
            do
            {
                lVol = vol;
                lToTweak = toTweak;
                toTweak = oToTweak + count++ * inc;
                vol = CalcVolume();
            }
            while (Mathf.Abs(vol - volume) < Mathf.Abs(lVol - volume));
            toTweak = lToTweak;
        }

        private float CalcVolume()
        {
            // So we have a rotated bezier curve from bottom to top.
            // There are four control points, the bottom (p0) and the top ones (p3) are obvious
            p0 = new Vector2(bottomDiameter, -length / 2f);
            p3 = new Vector2(topDiameter, length / 2f);

            float[] shape = shapePresets[curveIdx];

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

            // The maths for the area under the bezier can be calculated using 
            // pappus's centroid theroem: http://mathworld.wolfram.com/PappussCentroidTheorem.html
            // V = 2pi * Area of lamina * x geometric centroid y axis
            // geometric centroid about y axis = Moment about y / Area of curve
            // so area under curve ends up factoring out.


#if false
            // Here's the formula for area under the curve:
            // http://tug.org/TUGboat/tb33-1/tb103jackowski.pdf

            // I checked the maths myself, it does work out
            // using green's theorem: http://mathworld.wolfram.com/GreensTheorem.html
            // area = int x y' dt, t=0..1   
            // x = B(t) = (1-t)^3 p0_x + t(1-t)^2 p1_x + t^2(1-t) p2_x + t^3 p3_x
            // y = B'(t) = (1-t)^2 (p1_y-p0_y) + t(1-t) (p2_y-p1_y) + t^2 (p3_y-p2_y)

            float area = ((p1.y-p0.y)*(10*p0.x+6*p1.x+3*p2.x+p3.x)
                         +(p2.y-p1.y)*(4*p0.x+6*p1.x+6*p2.x+4*p3.x)
                         +(p3.y-p2.y)*(1*p0.x+3*p1.x+6*p2.x+10*p3.x))/20;

            // Of course it's not required anyhow.
#endif

            // Moment about y.
            // M_y = integrate x^2 y' dt, x=0..1

            // Skipping the several pages of maths workings....
            // If you want to repeat the maths for this one, be my guest! 
            // Don't shove it through alpha - it doesn't factorize it out very well.
            // I've got a spreadsheet that does most of the heavy lifting.

            // (x1 r^3 + 3 x2 r^2 s + 3 x3 r s^2 + x4 s^3)^2 
            // (y1 r^3 + 3 y2 r^2 s + 3 y3 r s^2 + y4 s^3) 
            // (3 z1 r^2 + 6 z2 r s + 3 z3 s^2)

            // factor 1/2 taken out. The 1/4 is because the pN.x are diameters not radii.
            float M_y  = 1f/4f* ((p1.y-p0.y)*(1f/3f * p0.x*p0.x + 1f/4f * p0.x*p1.x + 1f/14f* p0.x*p2.x + 3f/28f* p1.x*p1.x+ 3f/28f * p1.x*p2.x + 1f/84f * p0.x*p3.x + 3f/70f * p2.x*p2.x + 1f/35f* p1.x*p3.x + 1f/28f* p2.x*p3.x + 1f/84f* p3.x*p3.x) +
                                 (p2.y-p1.y)*(1f/12f* p0.x*p0.x + 1f/7f * p0.x*p1.x + 1f/14f* p0.x*p2.x + 3f/28f* p1.x*p1.x+ 6f/35f * p1.x*p2.x + 2f/105f* p0.x*p3.x + 3f/28f * p2.x*p2.x + 1f/14f* p1.x*p3.x + 1f/7f * p2.x*p3.x + 1f/12f* p3.x*p3.x) +
                                 (p3.y-p2.y)*(1f/84f* p0.x*p0.x + 1f/28f* p0.x*p1.x + 1f/35f* p0.x*p2.x + 3f/70f* p1.x*p1.x+ 3f/28f * p1.x*p2.x + 1f/84f * p0.x*p3.x + 3f/28f * p2.x*p2.x + 1f/14f* p1.x*p3.x + 1f/4f * p2.x*p3.x + 1f/3f * p3.x*p3.x));

            // therefore the centroid for x:

            // Back to pappus: (factor 1/2 * 2 taken out)
            return Mathf.PI * M_y;

            // Some regexp to store:
            // Replace from spreadsheet (regexp): 
            // ([xy])_([0-4]) -> p$2.$1 
            // (p[0-3].[xy])\^2 -> $1*$1    
            // (?<=[^a-zA-Z])([0-9]+) -> $1f
        }

        private Vector3 CalcMomentOfInertiaNoDensity() 
        {
            // This is the closed form for I_y 
            // 
            // http://arxiv.org/pdf/physics/0507172.pdf
            //
            // I_y = pi/2 integral_V f(y)^4 dy    (we revolve our solid around the y axis, not x as in the paper)
            //
            // But our function is parametrically defined
            // dy = df_y/ds ds = y'ds
            // f(y) = f_x(s) = x
            // v = s=0..1
            //
            // So:
            // I_y = pi/2 integral x^4 y' ds  s=0..1
            // x^4 y' = (a r^3 + 3 b r^2 s + 3 c r s^2 + d s^3)^4 (3 e r^2 + 6 f r s + 3 g s^2)
            // where a = x_0, b=x_1, c=x_2, d=x_3, e=(y_1-y_0), f=(y_2-y_1), g=(y_3-y_2), r=(1-s), s=s  (need to simplify to get this through alpha)
            //
            // = 3 a^4 e r^14+36 a^3 b e s r^13+6 a^4 f s r^13+162 a^2 b^2 e s^2 r^12+36 a^3 c e s^2 r^12+72 a^3 b f s^2 r^12+3 a^4 g s^2 r^12+324 a b^3 e s^3 r^11+324 a^2 b c e s^3 r^11+12 a^3 d e s^3 r^11+324 a^2 b^2 f s^3 r^11+72 a^3 c f s^3 r^11+36 a^3 b g s^3 r^11+243 b^4 e s^4 r^10+162 a^2 c^2 e s^4 r^10+972 a b^2 c e s^4 r^10+108 a^2 b d e s^4 r^10+648 a b^3 f s^4 r^10+648 a^2 b c f s^4 r^10+24 a^3 d f s^4 r^10+162 a^2 b^2 g s^4 r^10+36 a^3 c g s^4 r^10+972 a b c^2 e s^5 r^9+972 b^3 c e s^5 r^9+324 a b^2 d e s^5 r^9+108 a^2 c d e s^5 r^9+486 b^4 f s^5 r^9+324 a^2 c^2 f s^5 r^9+1944 a b^2 c f s^5 r^9+216 a^2 b d f s^5 r^9+324 a b^3 g s^5 r^9+324 a^2 b c g s^5 r^9+12 a^3 d g s^5 r^9+324 a c^3 e s^6 r^8+1458 b^2 c^2 e s^6 r^8+18 a^2 d^2 e s^6 r^8+324 b^3 d e s^6 r^8+648 a b c d e s^6 r^8+1944 a b c^2 f s^6 r^8+1944 b^3 c f s^6 r^8+648 a b^2 d f s^6 r^8+216 a^2 c d f s^6 r^8+243 b^4 g s^6 r^8+162 a^2 c^2 g s^6 r^8+972 a b^2 c g s^6 r^8+108 a^2 b d g s^6 r^8+972 b c^3 e s^7 r^7+108 a b d^2 e s^7 r^7+324 a c^2 d e s^7 r^7+972 b^2 c d e s^7 r^7+648 a c^3 f s^7 r^7+2916 b^2 c^2 f s^7 r^7+36 a^2 d^2 f s^7 r^7+648 b^3 d f s^7 r^7+1296 a b c d f s^7 r^7+972 a b c^2 g s^7 r^7+972 b^3 c g s^7 r^7+324 a b^2 d g s^7 r^7+108 a^2 c d g s^7 r^7+243 c^4 e s^8 r^6+162 b^2 d^2 e s^8 r^6+108 a c d^2 e s^8 r^6+972 b c^2 d e s^8 r^6+1944 b c^3 f s^8 r^6+216 a b d^2 f s^8 r^6+648 a c^2 d f s^8 r^6+1944 b^2 c d f s^8 r^6+324 a c^3 g s^8 r^6+1458 b^2 c^2 g s^8 r^6+18 a^2 d^2 g s^8 r^6+324 b^3 d g s^8 r^6+648 a b c d g s^8 r^6+12 a d^3 e s^9 r^5+324 b c d^2 e s^9 r^5+324 c^3 d e s^9 r^5+486 c^4 f s^9 r^5+324 b^2 d^2 f s^9 r^5+216 a c d^2 f s^9 r^5+1944 b c^2 d f s^9 r^5+972 b c^3 g s^9 r^5+108 a b d^2 g s^9 r^5+324 a c^2 d g s^9 r^5+972 b^2 c d g s^9 r^5+36 b d^3 e s^10 r^4+162 c^2 d^2 e s^10 r^4+24 a d^3 f s^10 r^4+648 b c d^2 f s^10 r^4+648 c^3 d f s^10 r^4+243 c^4 g s^10 r^4+162 b^2 d^2 g s^10 r^4+108 a c d^2 g s^10 r^4+972 b c^2 d g s^10 r^4+36 c d^3 e s^11 r^3+72 b d^3 f s^11 r^3+324 c^2 d^2 f s^11 r^3+12 a d^3 g s^11 r^3+324 b c d^2 g s^11 r^3+324 c^3 d g s^11 r^3+3 d^4 e s^12 r^2+72 c d^3 f s^12 r^2+36 b d^3 g s^12 r^2+162 c^2 d^2 g s^12 r^2+6 d^4 f s^13 r+36 c d^3 g s^13 r+3 d^4 g s^14
            //
            // Now the integral (1-s)^(14-n) s^n ds s=0..1 
            // has the solutions 1/ [ 15, 210, 1365, 5460. 15015, 30030, 45045, 51480, 54045, 30030, ... ]
            // 
            // So with some factorization:
            // i_y = pi/2 (
            //   1/5 a^4 e
            // + 1/5 d^4 g 
            // + 1/35 (6 a^3 b e + a^4 f)
            // + 1/35 (6 c d^3 g + d^4 f)
            // + 1/455 (54 a^2 b^2 e + 24 a^3 b f + 12 a^3 c e + a^4 g)
            // + 1/455 (54 c^2 d^2 g + 24 c d^3 f + 12 b d^3 g + d^4 e)
            // + 1/455 (27 a^2 b^2 f + 27 a b^3 e + 27 a^2 b c e + 6 a^3 c f + 3 a^3 b g + a^3 d e)
            // + 1/455 (27 c^2 d^2 f + 27 c^3 d g + 27 b c d^2 g + 6 b d^3 f + 3 c d^3 e + a d^3 g)
            // + 1/5005 (324 a b^2 c e + 216 a b^3 f + 216 a^2 b c f + 81 b^4 e + 54 a^2 c^2 e + 54 a^2 b^2 g + 36 a^2 b d e + 12 a^3 c g + 8 a^3 d f)
            // + 1/5005 (324 b c^2 d g + 216 c^3 d f + 216 b c d^2 f + 81 c^4 g + 54 c^2 d^2 e + 54 b^2 d^2 g + 36 a c d^2 g + 12 b d^3 e + 8 a d^3 f)
            // + 1/5005 (324 a b^2 c f + 162 b^3 c e + 162 a b c^2 e + 81 b^4 f + 54 a b^3 g + 54 a^2 c^2 f + 54 a b^2 d e + 54 a^2 b c g + 36 a^2 b d f + 18 a^2 c d e + 2 a^3 d g)
            // + 1/5005 (324 b c^2 d f + 162 b c^3 g + 162 b^2 c d g + 81 c^4 f + 54 c^3 d e + 54 b^2 d^2 f + 54 a c^2 d g + 54 b c d^2 e + 36 a c d^2 f + 18 a b d^2 g + 2 a d^3 e)
            // + 1/5005 (216 b^3 c f + 216 a b c^2 f + 162 b^2 c^2 e + 108 a b^2 c g + 72 a b c d e + 72 a b^2 d f + 36 a c^3 e + 36 b^3 d e + 27 b^4 g + 24 a^2 c d f + 18 a^2 c^2 g + 12 a^2 b d g + 2 a^2 d^2 e) 
            // + 1/5005 (216 b c^3 f + 216 b^2 c d f + 162 b^2 c^2 g + 108 b c^2 d e + 72 a b c d g + 72 a c^2 d f + 36 a c^3 g + 36 b^3 d g + 27 c^4 e + 24 a b d^2 f + 18 b^2 d^2 e + 12 a c d^2 e + 2 a^2 d^2 g)
            // + 1/1430 (81 b^2 c^2 f + 36 a b c d f + 27 b c^3 e + 27 b^3 c g + 27 b^2 c d e + 27 a b c^2 g + 18 a c^3 f + 18 b^3 d f + 9 a c^2 d e + 9 a b^2 d g + 3 a b d^2 e + 3 a^2 c d g + a^2 d^2 f)
            // )
            //
            // In the above every possible term is represented once. What a monster!

            // Eggrobin shoved it into mathmatica for me and came up with this optimized calculation:

            double x0=p0.x, x1=p1.x, x2=p2.x, x3=p3.x;
            double a=(p1.y-p0.y), b=(p2.y-p1.y), c=(p3.y-p2.y);

            // local variables to reduce the number of calculations.
            double t511 = x0 * x0; double t517 = x0 * t511; double t544 = x1 * x1; double t545 = x1 * t544;
            double t556 = x2 * x2; double t557 = x2 * t556; double t562 = x3 * x3; double t566 = 4 * t562;
            double t578 = x3 * t562; double t589 = 36 * x2 * t562; double t527 = x0 * t517;
            double t580 = 9 * x2; double t582 = 2 * x3; double t583 = t580 + t582; double t554 = x1 * t545;
            double t555 = 162 * t554; double t590 = 21 * x2 * x3; double t591 = 36 * t556; double t592 = t590 + t591 + t566;
            double t450 = 33 * x2; double t478 = 4 * x3; double t573 = 594 * t544; double t570 = 6 * x2;
            double t571 = t570 + x3; double t569 = 48 * x2 * x3; double t574 = 108 * t556; double t575 = 7 * t562;
            double t587 = 72 * x3 * t556; double t588 = 63 * t557; double t594 = 8 * t578;
            double t559 = x2 * t557; double t602 = x3 * t578; double t535 = 24 * x2; double t536 = 7 * x3;
            double t538 = t535 + t536; double t625 = 72 * t557; double t564 = 16 * x2 * x3; double t565 = 21 * t556;
            double t567 = t564 + t565 + t566; double t663 = 108 * x3 * t556; double t563 = 594 * t556 * t562;

            // I_y / (ρ π/2).
            double Density2PithsMomentY =
            (b * (4 * (132 * x1 + t450 + t478) * t517 + 286 * t527 + 18 * t538 * t545 + t555 + 432 * x3 * t557 + 162 * t559
            + t563 + 27 * t544 * t567 + t511 * (t569 + 72 * x1 * t571 + t573 + t574 + t575) + 528 * x2 * t578 + 12 * x1 * (54 * x3
            * t556 + 36 * t557 + 11 * t578 + t589) + 2 * x0 * (216 * t545 + 36 * t544 * t583 + t587 + t588 + t589 + 6 * x1 * t592
            + t594) + 286 * t602) + a * (22 * (78 * x1 + 12 * x2 + x3) * t517 + 2002 * t527 + t555 + 2 * t511 * (9 * x1 * (t450
            + t478) + 2 * (9 * x2 * x3 + 27 * t556 + t562) + t573) + 36 * t545 * t583 + 9 * t544 * t592 + 3 * x1 * (t587 + t588 +
            t589 + t594) + 2 * (54 * x3 * t557 + 27 * t559 + 54 * t556 * t562 + 33 * x2 * t578 + 11 * t602) + x0 * (594 * t545 + 63 *
            x3 * t556 + 24 * x2 * t562 + 108 * t544 * t571 + 3 * x1 * (t569 + t574 + t575) + 4 * t578 + t625)) + c * (22 * t527 + 9 *
            (21 * x2 + 8 * x3) * t545 + 54 * t554 + 108 * t544 * (3 * x2 * x3 + 3 * t556 + t562) + t517 * (66 * x1 + 4 * t571)
            + t511 * (108 * t544 + t566 + 12 * x1 * t583 + t590 + t591) + 2 * (297 * x3 * t557 + 81 * t559 + t563 + 858 * x2 * t578
            + 1001 * t602) + 6 * x1 * (54 * t557 + 99 * x2 * t562 + 44 * t578 + t663) + x0 * (9 * t538 * t544 + 108 * t545 + 72 * x2
            * t562 + 9 * x1 * t567 + 22 * t578 + t625 + t663))) / 10010;

            float Iy = Mathf.PI * (float)Density2PithsMomentY / 2f;

            return new Vector3(Iy / 2f, Iy, Iy / 2f);
        }

        private Vector3 CalcCoMOffset()
        {
            // d y-bar = dV y
            // dV = pi x^2 dy = pi x^2 y' ds
            // y-bar = pi integrate x^2 y y' ds

            // This is going to turn into another monster...

            return default(Vector3);
        }


        #endregion

        #region Bezier Bits

        [KSPField]
        public bool showHull = false;

        private void WriteBezier()
        {
            if (showHull)
                WriteHull();
            else
                WriteShape();
        }

        private void WriteHull()
        {
            // Perpendicular vector
            float[] lengths = { (p1 - p0).magnitude, (p2 - p1).magnitude, (p3 - p2).magnitude };
            float sum = lengths.Sum();

            Vector2 norm = new Vector2(length, (bottomDiameter - topDiameter) / 2f);
            norm.Normalize();

            WriteMeshes(
                new ProfilePoint(p0.x, p0.y, 0f, Vector2.right),
                new ProfilePoint(p1.x, p1.y, lengths[0] / sum, Vector2.right),
                new ProfilePoint(p2.x, p2.y, (lengths[0] + lengths[1]) / sum, Vector2.right),
                new ProfilePoint(p3.x, p3.y, 1f, Vector2.right)
                );
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

                    if (Mathf.Abs(devTS) < maxCircleError)
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
                float dX = nx.Value.dia - pv.Value.dia;
                float dY = nx.Value.y - pv.Value.y;

                cumLengths[i] = sumLengths += Mathf.Sqrt(dX * dX + dY * dY);
            }

            points.First.Value.v = 0;
            nx = points.First.Next;
            for (int i = 0; i < cumLengths.Length; ++i, nx = nx.Next)
            {
                nx.Value.v = cumLengths[i] / sumLengths;
            }


            WriteMeshes(points);
        }

        private ProfilePoint CreatePoint(float t, ref int colliderTri)
        {
            // B(t) = (1-t)^3 p0 + t(1-t)^2 p1 + t^2(1-t) p2 + t^3 p3
            Vector2 Bt = B(t);

            // B'(t) = (1-t)^2 (p1-p0) + t(1-t) (p2-p1) + t^2 (p3-p2)
            Vector2 Btdt = Bdt(t);

            // normalized perpendicular to tangent (derivative)
            Vector2 norm = new Vector2(Btdt.y, -Btdt.x / 2f).normalized;

            // Count the number of triangles
            CirclePoints colliderCirc = CirclePoints.ForDiameter(Bt.x, maxCircleError * 4f, 4, 16);
            colliderTri += (colliderCirc.totVertexes + 1) * 2;

            //Debug.LogWarning(string.Format("Creating profile point t={0:F3} coord=({1:F3}, {2:F3})  normal=({3:F3}, {4:F3})", t, Bt.x, Bt.y, norm.x, norm.y));

            // We can have a maxium of 255 triangles in the collider. Will leave a bit of breathing room at the top.
            if (colliderTri <= 220)
                return new ProfilePoint(Bt.x, Bt.y, t, norm, colliderCirc: colliderCirc);
            else
                return new ProfilePoint(Bt.x, Bt.y, t, norm, inCollider: false);
        }

        private Vector2 B(float t)
        {
            return (1 - t) * (1 - t) * (1 - t) * p0 + 3 * t * (1 - t) * (1 - t) * p1 + 3 * t * t * (1 - t) * p2 + t * t * t * p3;
        }

        private Vector2 Bdt(float t)
        {
            return 3 * (1 - t) * (1 - t) * (p1 - p0) + 6 * t * (1 - t) * (p2 - p1) + 3 * t * t * (p3 - p2);
        }

        #endregion
    }
}