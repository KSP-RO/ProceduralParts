using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

public class StretchyCylinderTank : AbstractStretchyTank
{

    [KSPField]
    public string tankModelName = "stretchyTank";

    [KSPField]
    public string sidesName = "sides";

    [KSPField]
    public string endsName = "ends";

    [KSPField]
    public string collisionName = "collisionMesh";

    private Material sidesMaterial;
    private Material endsMaterial;

    private Mesh sidesMesh;
    private Mesh endsMesh;
    private Mesh colliderMesh;

    private MeshCollider colliderObj;

    #region callbacks and initialization

    public override void OnStart(PartModule.StartState state)
    {
        try {
            Transform tankModel = part.FindModelTransform(tankModelName);

            Transform sides = part.FindModelTransform(sidesName);
            Transform ends = part.FindModelTransform(endsName);
            Transform colliderTr = part.FindModelTransform(collisionName);

            sidesMaterial = sides.renderer.material;
            endsMaterial = ends.renderer.material;

            // Instantiate meshes. The mesh method unshares any shared meshes.
            sidesMesh = sides.GetComponent<MeshFilter>().mesh;
            endsMesh = ends.GetComponent<MeshFilter>().mesh;
            colliderObj = colliderTr.GetComponent<MeshCollider>();
            colliderMesh = colliderObj.sharedMesh = new Mesh();

            base.OnStart(state);

            updateVolume();
        }
        catch (Exception ex)
        {
            print("OnStart exception: " + ex);
        }
    }

    private bool skipNextUpdate = false;
    private bool updateException = false;

    public override void Update()
    {
        if (skipNextUpdate || updateException)
        {
            skipNextUpdate = false;
            //collider.enabled = true;
            return;
        } 

        try { 
            base.Update();

            if(HighLogic.LoadedSceneIsEditor)
                updateVolume();
        }
        catch (Exception ex)
        {
            print("Update exception: " + ex);
            updateException = true;
        }
    }

    private float tankULength;
    private float tankVLength;

    public override void GetMaterialsAndScale(out Material sidesMaterial, out Material endsMaterial, out Vector2 sideScale)
    {
        sidesMaterial = this.sidesMaterial;
        endsMaterial = this.endsMaterial;
        sideScale = new Vector2(tankULength, tankVLength);
    }

    public override object addTankAttachment(TransformPositionFollower attach)
    {

        // TODO: needs doing
        //attach.SetParent(tankModel.transform);
        return attach;
    }

    public override void removeTankAttachment(object data)
    {
        // TODO: needs doing.
        //((TransformPositionFollower)data).SetParent(null);
    }

    #endregion

    #region Diameter and Length

    [KSPField]
    public float diameterCourseSteps = 1.25f;
    [KSPField]
    public float diameterFineSteps = 0.05f;


    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Diameter", guiFormat = "F3"), UI_FloatEdit(minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float diameter = 1.25f;
    private float oldDiameter;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3"), UI_FloatEdit(minValue = 0.2f, maxValue = 10.0f, incrementLarge = 1, incrementSmall = 0.1f, incrementSlide = 0.001f)]
    public float length = 1.0f;
    private float oldLength;

    [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Bottom", guiFormat = "F3"), UI_FloatEdit(minValue = 0.25f, maxValue = 10.0f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
    public float bottomDiameter = 1.25f;
    private float oldShapeParam1;

    public const float maxError = 0.005f;
    public const float maxDiameterChange = 0.25f;

    private void updateVolume()
    {
        if (diameter == oldDiameter && length == oldLength && bottomDiameter == oldShapeParam1)
            return;

        Vector2 tangent = new Vector2(length, bottomDiameter - diameter);
        tangent.Normalize();

        List<ProfilePoint> points = new List<ProfilePoint>();
        points.Add(new ProfilePoint(diameter, 0.5f*length, 0f, tangent));

        float dDiameter = bottomDiameter - diameter;
        int subdiv = (int)Mathf.Floor(dDiameter / maxDiameterChange);
        print("Subdiv = " + subdiv);
        for (int i = 1; i < subdiv; ++i)
        {
            float iV = (float)i / (float)subdiv;
            float iDiameter = diameter + dDiameter * iV;
            float iY = length * 0.5f - length * iV;
            points.Add(new ProfilePoint(iDiameter, iY, iV, tangent));
        }
        points.Add(new ProfilePoint(bottomDiameter, -0.5f * length, 1f, tangent));

        WriteMeshes(points);

        CombineInstance[] combine = new CombineInstance[2];
        combine[0] = new CombineInstance();
        combine[0].mesh = sidesMesh;
        combine[1] = new CombineInstance();
        combine[1].mesh = endsMesh;

        colliderMesh.Clear();
        colliderMesh.name = endsMesh.name + " " + sidesMesh.name;
        colliderMesh.CombineMeshes(combine, true, false);

        // If we don't do this, the collider doesn't work properly.
        colliderObj.enabled = false;
        colliderObj.enabled = true;

        if (isSRB)
            srbBell.localScale = new Vector3(bottomDiameter * 0.8f, bottomDiameter * 0.8f, bottomDiameter * 0.8f);

        oldDiameter = diameter;
        oldLength = length;
        oldShapeParam1 = bottomDiameter;
        
        // We need to skip the next update so attached TransformPositionFollowers can settle into position.
        skipNextUpdate = true;

        foreach (Part sym in part.symmetryCounterparts)
        {
            StretchyCylinderTank counterpart = sym.Modules.OfType<StretchyCylinderTank>().FirstOrDefault();
            counterpart.diameter = diameter;
            counterpart.length = length;
        }

    }

    public class ProfilePoint
    {
        public readonly float dia;
        public readonly float y;
        public readonly float v;

        // the normal as a 2 component unit vector (dia, y)
        // eg: for cylinders this would be (1,0), for endcaps (0,+/-1)
        public readonly Vector2 norm;

        public readonly CirclePoints circ;

        public ProfilePoint(float dia, float y, float v, Vector2 norm, CirclePoints circ = null)
        {
            this.dia = dia;
            this.y = y;
            this.v = v;
            this.norm = norm;
            this.circ = circ ?? CirclePoints.ForDiameter(dia);
        }
    }

    private void WriteMeshes(params ProfilePoint [] pts)
    {
        _WriteMeshes(pts);
    }

    private void WriteMeshes<T>(T pts)
        where T : IList<ProfilePoint>, ICollection<ProfilePoint>
    {
        _WriteMeshes(pts);
    }

    private void _WriteMeshes<T>(T pts)
        where T : IList<ProfilePoint>, ICollection<ProfilePoint> 
    {
        if (pts == null || pts.Count < 2)
            return;

        // Set the tank stats
        tankVolume = 0;
        tankULength = 0;
        tankVLength = 0;

        int nVrt = 0;
        int nTri = 0;

        ProfilePoint first = pts[0];
        ProfilePoint last = pts[pts.Count-1];

        int i = 0;
        foreach (ProfilePoint pt in pts)
        {
            nVrt += pt.circ.totVertexes + 1;

            // one for above, one for below
            nTri += 2 * pt.circ.totVertexes;

            ++i;
            last = pt;
        }
        // Have double counted for the first and last circles.
        nTri -= first.circ.totVertexes + last.circ.totVertexes;

        UncheckedMesh m = new UncheckedMesh(nVrt, nTri);

        ProfilePoint prev = null;
        i = 0;
        int vOff = 0, prevVOff = 0;
        int tOff = 0;
        float sumDiameters = 0;
        print("Begin ULength");
        foreach (ProfilePoint pt in pts)
        {
            pt.circ.WriteVertexes(diameter: pt.dia, y: pt.y, v: pt.v, norm: pt.norm, off: vOff, m:m);
            if (prev != null)
            {
                CirclePoints.WriteTriangles(prev.circ, prevVOff, pt.circ, vOff, m.triangles, tOff * 3);
                tOff += prev.circ.totVertexes + pt.circ.totVertexes;

                // Work out the area of the truncated cone

                // integral_y1^y2 pi R(y)^2 dy   where R(y) = ((r2-r1)(y-y1))/(r2-r1) + r1   Integrate circles along a line
                // integral_y1^y2 pi ( ((r2-r1)(y-y1))/(r2-r1) + r1) ^2 dy                Substituted in formula.
                // == -1/3 pi (y1-y2) (r1^2+r1*r2+r2^2)                                   Do the calculus
                // == -1/3 pi (y1-y2) (d1^2/4+d1*d2/4+d2^2/4)                             r = d/2
                // == -1/12 pi (y1-y2) (d1^2+d1*d2+d2^2)                                  Take out the factor
                tankVolume += (Mathf.PI * (prev.y - pt.y) * (prev.dia * prev.dia + prev.dia * pt.dia + pt.dia * pt.dia)) / 12f;

                float dy = (prev.y - pt.y);
                float dr = (prev.dia - pt.dia) * 0.5f;
                print("dy=" + dy + " dr=" + dr + " len=" + Mathf.Sqrt(dy * dy + dr * dr).ToString("F3"));
                tankVLength += Mathf.Sqrt(dy * dy + dr * dr);
                
                // average diameter weighted by dy
                sumDiameters += (pt.dia + prev.dia) * dy;
            }

            ++i;
            prev = pt;
            prevVOff = vOff;
            vOff += pt.circ.totVertexes + 1;
        }

        // Use the average diameter across segments to set the ULength
        tankULength = Mathf.PI * sumDiameters / (first.y - last.y);

        print("ULength=" + tankULength + " VLength=" + tankVLength);

        m.WriteTo(sidesMesh);        

        // The endcaps.
        nVrt = first.circ.totVertexes + last.circ.totVertexes;
        nTri = first.circ.totVertexes - 2 + last.circ.totVertexes - 2;
        m = new UncheckedMesh(nVrt, nTri);

        first.circ.WriteEndcap(first.dia, first.y, true, 0, 0, m);
        last.circ.WriteEndcap(last.dia, last.y, false, first.circ.totVertexes, (first.circ.totVertexes - 2) * 3, m);

        m.WriteTo(endsMesh);
    }

    #endregion

}

#region Circle Points

public class CirclePoints
{
    public static CirclePoints ForDiameter(float diameter)
    {
        // I'm not sure this lock is strictly required, but will do it anyhow.
        lock (circlePoints)
        {
            int idx = circlePoints.FindIndex(v => (v.maxError * diameter) < StretchyCylinderTank.maxError);
            switch (idx)
            {
                case 0:
                    return circlePoints[0];
                case -1:
                    CirclePoints ret;
                    do
                    {
                        ret = new CirclePoints(circlePoints.Count);
                        circlePoints.Add(ret);
                    } while ((ret.maxError * diameter) > StretchyCylinderTank.maxError);
                    return ret;
                default:
                    return circlePoints[idx - 1];
            }
        }

    }

    private static List<CirclePoints> circlePoints = new List<CirclePoints>();


    public readonly int subdivCount;
    public readonly int totVertexes;
    public readonly float maxError;

    private static float maxError0 = Mathf.Sqrt(2) * (Mathf.Sin(Mathf.PI / 4.0f) - 0.5f) * 0.5f;

    private float[] uCoords;
    private float[] xCoords;
    private float[] zCoords;

    private bool complete = false;

    public CirclePoints(int subdivCount)
    {
        this.subdivCount = subdivCount;
        this.totVertexes = (1 + subdivCount) * 4;

        if (subdivCount == 0)
        {
            uCoords = new float[] { 0.0f };
            xCoords = new float[] { 0.0f };
            zCoords = new float[] { 1.0f };

            maxError = maxError0;
            complete = true;
        }
        else
        {
            // calculate the max error.
            uCoords = new float[] { 0.0f, (float)1 / (float)(subdivCount + 1) / 4.0f };
            float theta = uCoords[1] * Mathf.PI * 2.0f;
            xCoords = new float[] { 0.0f, Mathf.Sin(theta) };
            zCoords = new float[] { 1.0f, Mathf.Cos(theta) };

            float dX = Mathf.Sin(theta / 2.0f) - xCoords[1] / 2.0f;
            float dY = Mathf.Cos(theta / 2.0f) - zCoords[1] / 2.0f - 0.5f;

            maxError = Mathf.Sqrt(dX * dX + dY * dY) * 0.5f;

            complete = subdivCount == 1;
        }
    }

    private void Complete()
    {
        if (complete)
            return;

        int totalCoords = subdivCount + 1;

        float[] oldUCoords = uCoords;
        float[] oldXCoords = xCoords;
        float[] oldYCoords = zCoords;
        uCoords = new float[totalCoords];
        xCoords = new float[totalCoords];
        zCoords = new float[totalCoords];
        Array.Copy(oldUCoords, uCoords, 2);
        Array.Copy(oldXCoords, xCoords, 2);
        Array.Copy(oldYCoords, zCoords, 2);

        float denom = (float)(4 * (subdivCount + 1));
        for (int i = 2; i <= subdivCount; ++i)
        {
            uCoords[i] = (float)i / denom;
            float theta = uCoords[i] * Mathf.PI * 2.0f;
            xCoords[i] = Mathf.Sin(theta);
            zCoords[i] = Mathf.Cos(theta);
        }

        complete = true;
    }

    /// <summary>
    /// writes this.totVerticies + 1 uv, verticies, and tangents and this.totVerticies triangles to the passed arrays for a single endcap.
    /// Callers will need to fill the normals. This will be { 0, 1, 0 } for top endcap, and { 0, -1, 0 } for bottom.
    /// </summary>
    /// <param name="dia">diameter of circle</param>
    /// <param name="y">y dimension for points</param>
    /// <param name="vOff">offset into uv, verticies, and normal arrays to begin at</param>
    /// <param name="uv">uv array, data will be written</param>
    /// <param name="verticies">verticies array</param>
    /// <param name="tangents">tangents array</param>
    /// <param name="to">offset into triangles array</param>
    /// <param name="triangles"></param>
    public void WriteEndcap(float dia, float y, bool up, int vOff, int to, UncheckedMesh m)
    {
        Complete();

        for (int i = 0; i <= subdivCount; ++i)
        {
            int o0 = vOff + i;
            m.uv[o0] = new Vector2((-xCoords[i] + 1f) * 0.5f, (zCoords[i] + 1f) * 0.5f);
            m.verticies[o0] = new Vector3(xCoords[i] * dia * 0.5f, y, zCoords[i] * dia * 0.5f);

            int o1 = vOff + i + subdivCount + 1;
            m.uv[o1] = new Vector2((-zCoords[i] + 1f) * 0.5f, (-xCoords[i] + 1f) * 0.5f);
            m.verticies[o1] = new Vector3(zCoords[i] * dia * 0.5f, y, -xCoords[i] * dia * 0.5f);

            int o2 = vOff + i + 2 * (subdivCount + 1);
            m.uv[o2] = new Vector2((xCoords[i] + 1f) * 0.5f, (-zCoords[i] + 1f) * 0.5f);
            m.verticies[o2] = new Vector3(-xCoords[i] * dia * 0.5f, y, -zCoords[i] * dia * 0.5f);

            int o3 = vOff + i + 3 * (subdivCount + 1);
            m.uv[o3] = new Vector2((zCoords[i] + 1f) * 0.5f, (xCoords[i] + 1f) * 0.5f);
            m.verticies[o3] = new Vector3(-zCoords[i] * dia * 0.5f, y, xCoords[i] * dia * 0.5f);

            m.tangents[o0] = m.tangents[o1] = m.tangents[o2] = m.tangents[o3] = new Vector4(-1, 0, 0, up ? 1 : -1);
            m.normals[o0] = m.normals[o1] = m.normals[o2] = m.normals[o3] = new Vector3(0, up ? 1 : -1, 0);
        }

        for (int i = 1; i < totVertexes - 1; ++i)
        {
            m.triangles[to++] = vOff;
            m.triangles[to++] = vOff + i + (up ? 0 : 1);
            m.triangles[to++] = vOff + i + (up ? 1 : 0);
        }
    }

    /// <summary>
    /// Write vertexes for the circle.
    /// </summary>
    /// <param name="diameter">diameter of the circle</param>
    /// <param name="y">y coordinate</param>
    /// <param name="norm">unit normal vector along the generator curve for increasing y. The y param becomes the y of the normal, the x multiplies the normals to the circle</param>
    /// <param name="v">v coordinate for UV</param>
    /// <param name="off">offset into following arrays</param>
    /// <param name="uv">UVs to copy into</param>
    /// <param name="verticies">vertexes</param>
    /// <param name="normals">normals</param>
    /// <param name="tangents">tangents</param>
    public void WriteVertexes(float diameter, float y, float v, Vector2 norm, int off, UncheckedMesh m)
    {
        Complete();
        for (int i = 0; i <= subdivCount; ++i)
        {
            int o0 = off + i;
            m.uv[o0] = new Vector2(uCoords[i], v);
            m.verticies[o0] = new Vector3(xCoords[i] * 0.5f * diameter, y, zCoords[i] * 0.5f * diameter);
            m.normals[o0] = new Vector3(xCoords[i] * norm.x, norm.y, zCoords[i] * norm.x);
            m.tangents[o0] = new Vector4(zCoords[i], 0, -xCoords[i], 1.0f);
            //MonoBehaviour.print("Vertex #" + i + " off=" + o0 + " u=" + uv[o0][0] + " coords=" + verticies[o0]);

            int o1 = off + i + subdivCount + 1;
            m.uv[o1] = new Vector2(uCoords[i] + 0.25f, v);
            m.verticies[o1] = new Vector3(zCoords[i] * 0.5f * diameter, y, -xCoords[i] * 0.5f * diameter);
            m.normals[o1] = new Vector3(zCoords[i] * norm.x, norm.y, -xCoords[i] * norm.x);
            m.tangents[o1] = new Vector4(-xCoords[i], 0, -zCoords[i], 1.0f);

            int o2 = off + i + 2 * (subdivCount + 1);
            m.uv[o2] = new Vector2(uCoords[i] + 0.50f, v);
            m.verticies[o2] = new Vector3(-xCoords[i] * 0.5f * diameter, y, -zCoords[i] * 0.5f * diameter);
            m.normals[o2] = new Vector3(-xCoords[i] * norm.x, norm.y, -zCoords[i] * norm.x);
            m.tangents[o2] = new Vector4(-zCoords[i], 0, xCoords[i], 1.0f);

            int o3 = off + i + 3 * (subdivCount + 1);
            m.uv[o3] = new Vector2(uCoords[i] + 0.75f, v);
            m.verticies[o3] = new Vector3(-zCoords[i] * 0.5f * diameter, y, xCoords[i] * 0.5f * diameter);
            m.normals[o3] = new Vector3(-zCoords[i] * norm.x, norm.y, xCoords[i] * norm.x);
            m.tangents[o3] = new Vector4(xCoords[i], 0, zCoords[i], 1.0f);
        }

        // write the wrapping vertex. This is identical to the first one except for u coord = 1
        int lp = off + totVertexes;
        m.uv[lp] = new Vector2(1.0f, v);
        m.verticies[lp] = m.verticies[off];
        m.normals[lp] = m.normals[off];
        m.tangents[lp] = m.tangents[off];
    }

    /// <summary>
    /// Creates a.vertexes + b.vertexes triangles to cover the surface between circle a and b.
    /// </summary>
    /// <param name="a">the first circle points</param>
    /// <param name="ao">offset into vertex array for a points</param>
    /// <param name="b">the second circle points</param>
    /// <param name="bo">offset into vertex array for b points</param>
    /// <param name="triangles">triangles array for output</param>
    /// <param name="to">offset into triangles array. This must be a multiple of 3</param>
    public static void WriteTriangles(CirclePoints a, int ao, CirclePoints b, int bo, int[] triangles, int to)
    {
        bool flip = false;
        if (a.totVertexes < b.totVertexes)
        {
            Swap(ref a, ref b);
            Swap(ref ao, ref bo);
            //MonoBehaviour.print("Flipping");
            flip = true;
        }

        int bq = b.subdivCount + 1;
        int aq = a.subdivCount + 1;

        int bi = 0;
        bool bcomplete = false;
        for (int ai = 0; ai < aq; ++ai)
        {
            //MonoBehaviour.print("A-tri #" + ai + " tOff=" + to);
            triangles[to++] = ao + ai;
            triangles[to++] = flip ? (ao + ai + 1) : (bo + bi);
            triangles[to++] = flip ? (bo + bi) : (ao + ai + 1);
            //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

            triangles[to++] = ao + ai + aq;
            triangles[to++] = flip ? (ao + ai + 1 + aq) : (bo + bi + bq);
            triangles[to++] = flip ? (bo + bi + bq) : (ao + ai + 1 + aq);
            //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

            triangles[to++] = ao + ai + 2 * aq;
            triangles[to++] = flip ? (ao + ai + 1 + 2 * aq) : (bo + bi + 2 * bq);
            triangles[to++] = flip ? (bo + bi + 2 * bq) : (ao + ai + 1 + 2 * aq);
            //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

            triangles[to++] = ao + ai + 3 * aq;
            triangles[to++] = flip ? (ao + ai + 1 + 3 * aq) : (bo + bi + 3 * bq);
            triangles[to++] = flip ? (bo + bi + 3 * bq) : (ao + ai + 1 + 3 * aq);
            //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

            if (bcomplete)
                continue;

            float nxau = (ai + 1 < aq) ? a.uCoords[ai + 1] : 0.25f;
            float nxbu = (bi + 1 < bq) ? b.uCoords[bi + 1] : 0.25f;
            //MonoBehaviour.print("Offsets: nxau=" + nxau + " nxbu=" + nxbu + " (nxau-au)=" + (nxau - a.uCoords[ai]) + " (nxbu-nxau)=" + (nxbu - nxau));

            if ((nxau - a.uCoords[ai]) > (nxbu - nxau))
            {
                //MonoBehaviour.print("B-tri #" + bi + " tOff=" + to);
                triangles[to++] = bo + bi;
                triangles[to++] = flip ? (ao + ai + 1) : (bo + bi + 1);
                triangles[to++] = flip ? (bo + bi + 1) : (ao + ai + 1);
                //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                triangles[to++] = bo + bi + bq;
                triangles[to++] = flip ? (ao + ai + 1 + aq) : (bo + bi + 1 + bq);
                triangles[to++] = flip ? (bo + bi + 1 + bq) : (ao + ai + 1 + aq);
                //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                triangles[to++] = bo + bi + 2 * bq;
                triangles[to++] = flip ? (ao + ai + 1 + 2 * aq) : (bo + bi + 1 + 2 * bq);
                triangles[to++] = flip ? (bo + bi + 1 + 2 * bq) : (ao + ai + 1 + 2 * aq);
                //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                triangles[to++] = bo + bi + 3 * bq;
                triangles[to++] = flip ? (ao + ai + 1 + 3 * aq) : (bo + bi + 1 + 3 * bq);
                triangles[to++] = flip ? (bo + bi + 1 + 3 * bq) : (ao + ai + 1 + 3 * aq);
                //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                bcomplete = (++bi == bq);                    
            }
        }
    }

    private static void Swap<T>(ref T one, ref T two)
    {
        T tmp = one;
        one = two;
        two = tmp;
    }

}
#endregion

