using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{
    class ProceduralShapeHollowTruss : ProceduralAbstractShape
    {
        private const string ModTag = "[ProceduralShapeHollowTruss]";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Top D", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float topDiameter = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Bottom D", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float bottomDiameter = 2f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Length", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float length = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Rod D", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float rodDiameter = 0.125f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Rods", guiUnits = "#", guiFormat = "F0", groupName = ProceduralPart.PAWGroupName), 
            UI_FloatRange(minValue = 3, maxValue = 30, stepIncrement = 1, scene = UI_Scene.Editor)]
        public float nbRods = 12;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Tilt Angle", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "°", useSI = true)]
        public float tiltAngle = 10f;

        private float maxError = 0.01f;

        public int numSides => (int)Math.Max(Mathf.PI / Mathf.Acos(1 - maxError / bottomDiameter), 12);

        public float MajorRadius => (bottomDiameter + topDiameter) / 4;
        public float MinorRadius => (bottomDiameter - topDiameter) / 4;

        [KSPField]
        public string TopNodeName = "top";

        [KSPField]
        public string BottomNodeName = "bottom";

        private float CornerCenterCornerAngle => 2 * Mathf.PI / numSides;
        private float NormSideLength => Mathf.Tan(CornerCenterCornerAngle / 2);

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsEditor)
            {
                UpdateTechConstraints();
                Fields[nameof(topDiameter)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(bottomDiameter)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(length)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(rodDiameter)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(nbRods)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(tiltAngle)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;

                // Fields[nameof(bottomDiameter)].uiControlEditor.onSymmetryFieldChanged =
                // Fields[nameof(topDiameter)].uiControlEditor.onSymmetryFieldChanged =
                // Fields[nameof(length)].uiControlEditor.onSymmetryFieldChanged =
                // Fields[nameof(rodDiameter)].uiControlEditor.onSymmetryFieldChanged = ClampFillet;
            }
        }

        public override void AdjustDimensionBounds()
        {
            float maxOuterDiameter = PPart.diameterMax;
            float maxInnerDiameter = PPart.diameterMax;
            float minOuterDiameter = PPart.diameterMin;
            float minInnerDiameter = PPart.diameterMin;
            float maxFillet = PPart.diameterMax / 2;

            // Vary the outer diameter to stay within min and max volume, given inner diameter
            if (PPart.volumeMax < float.PositiveInfinity)
            {
                var majorRadMax = PPart.volumeMax / (Mathf.PI * MinorRadius * MinorRadius * 2 * Mathf.PI);
                var minorRadMax = Mathf.Sqrt(PPart.volumeMax / (Mathf.PI * MajorRadius * 2 * Mathf.PI));

                //MajorRadius => (outerDiameter + innerDiameter) / 2
                //MinorRadius => (outerDiameter - innerDiameter) / 2;
                maxOuterDiameter = majorRadMax * 2 - topDiameter;
                maxInnerDiameter = -(minorRadMax * 2 - bottomDiameter);
            }

            maxOuterDiameter = Mathf.Clamp(maxOuterDiameter, PPart.diameterMin, PPart.diameterMax);
            maxInnerDiameter = Mathf.Clamp(maxInnerDiameter, PPart.diameterMin, PPart.diameterMax);
            // maxInnerDiameter = Mathf.Clamp(maxInnerDiameter, PPart.diameterMin, bottomDiameter - PPart.diameterSmallStep);

            // minOuterDiameter = Mathf.Clamp(minOuterDiameter, topDiameter + PPart.diameterSmallStep, maxOuterDiameter);

            maxFillet = Mathf.Clamp(maxFillet, 0, length);
            maxFillet = Mathf.Clamp(maxFillet, 0, (bottomDiameter - topDiameter) / 2f);

            (Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxOuterDiameter;
            (Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit).minValue = minOuterDiameter;
            (Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxInnerDiameter;
            (Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit).minValue = minInnerDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).minValue = PPart.lengthMin;
            (Fields[nameof(rodDiameter)].uiControlEditor as UI_FloatEdit).minValue = PPart.diameterMin;
            // (Fields[nameof(rodDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxFillet;

        }

        public override float CalculateVolume()
        {
            return Mathf.PI * rodDiameter * rodDiameter * length * nbRods;
        }

        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r /= (bottomDiameter / 2);
            coords.y /= length;
        }

        public override bool SeekVolume(float targetVolume, int dir) => SeekVolume(targetVolume, Fields[nameof(length)], dir);

        public override void TranslateAttachmentsAndNodes(BaseField f, object obj)
        {
            if (f.name == nameof(length) && obj is float oldLength)
            {
                HandleLengthChange(length, oldLength);
            }
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r *= (bottomDiameter / 2);
            coords.y *= length;
        }

        public override void UpdateTechConstraints()
        {
            Fields[nameof(topDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit innerDiameterEdit = Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit;
            innerDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            innerDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(bottomDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit outerDiameterEdit = Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit;
            outerDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            outerDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(length)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit lengthEdit = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
            lengthEdit.incrementLarge = PPart.lengthLargeStep;
            lengthEdit.incrementSmall = PPart.lengthSmallStep;

            Fields[nameof(rodDiameter)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit filletEdit = Fields[nameof(rodDiameter)].uiControlEditor as UI_FloatEdit;
            filletEdit.incrementLarge = PPart.lengthLargeStep;
            filletEdit.incrementSmall = PPart.lengthSmallStep;

            UI_FloatEdit angleEdit = Fields[nameof(tiltAngle)].uiControlEditor as UI_FloatEdit;
            angleEdit.incrementLarge = PPart.lengthLargeStep;
            angleEdit.incrementSmall = PPart.lengthSmallStep;

            AdjustDimensionBounds();
            topDiameter = Mathf.Clamp(topDiameter, innerDiameterEdit.minValue, innerDiameterEdit.maxValue);
            bottomDiameter = Mathf.Clamp(bottomDiameter, outerDiameterEdit.minValue, outerDiameterEdit.maxValue);
            length = Mathf.Clamp(length, lengthEdit.minValue, lengthEdit.maxValue);
            rodDiameter = Mathf.Clamp(rodDiameter, filletEdit.minValue, filletEdit.maxValue);
        }
        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", topDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", bottomDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }

        internal override void InitializeAttachmentNodes() => InitializeAttachmentNodes(length, bottomDiameter);

        internal override void UpdateShape(bool force = true)
        {
            part.CoMOffset = CoMOffset;
            Volume = CalculateVolume();
            GenerateMeshes(bottomDiameter / 2, topDiameter / 2, length, rodDiameter / 2, (int)nbRods);

            GenerateColliders();
            // WriteMeshes in AbstractSoRShape typically does UpdateNodeSize, UpdateProps, RaiseModelAndColliderChanged
            UpdateNodeSize(TopNodeName);
            UpdateNodeSize(BottomNodeName);
            PPart.UpdateProps();
            RaiseModelAndColliderChanged();
        }

        private void UpdateNodeSize(string nodeName)
        {
            if (part.attachNodes.Find(n => n.id == nodeName) is AttachNode node)
            {
                node.size = Math.Min((int)(topDiameter / PPart.diameterLargeStep), 3);
                node.breakingTorque = node.breakingForce = Mathf.Max(50 * node.size * node.size, 50);
                RaiseChangeAttachNodeSize(node, topDiameter, Mathf.PI * topDiameter * topDiameter * 0.25f + Mathf.PI * length * (topDiameter+bottomDiameter));
            }
        }

        private void GenerateColliders()
        {
            //gameObject.GetComponentsInChildren<SphereCollider>().FirstOrDefault(c => c.name.Equals("Central_Sphere_Collider"))?.gameObject.DestroyGameObject();

            PPart.ClearColliderHolder();
            // The first corner is at angle=0.
            // We want to start the capsules in between the corners.
            float offset = (360f / numSides) / 2;
            Vector3 refPoint = new Vector3(MajorRadius, 0, 0);

            for (int i=0; i<numSides; i++)
            {
                var go = new GameObject($"Mesh_Collider_{i}");
                var coll = go.AddComponent<MeshCollider>();
                go.transform.SetParent(PPart.ColliderHolder.transform, false);
                coll.convex = true;
                coll.sharedMesh = GenerateColliderMesh();
                var prevCornerOrient = Quaternion.AngleAxis(360f * i / numSides, Vector3.up);
                var prevCornerPos = prevCornerOrient * refPoint;
                var nextCornerOrient = Quaternion.AngleAxis(360f * (i+1) / numSides, Vector3.up);
                var nextCornerPos = nextCornerOrient * refPoint;
                var orientation = Quaternion.AngleAxis(90 + offset + (360f * i / numSides), Vector3.up);
                go.transform.localRotation *= orientation;
                go.transform.localPosition = (prevCornerPos + nextCornerPos) / 2;
            }
        }

        private Mesh GenerateColliderMesh()
        {
            float maxColliderError = 0.1f;
            int pointspercorner = (int)Math.Min(Math.Max(Mathf.PI / Mathf.Acos(1 - maxColliderError / Mathf.Max(rodDiameter, maxColliderError)), 1), 30);
            Mesh colliderMesh = new Mesh();
            Vector3[] vertices = new Vector3[2*pointspercorner*4];
            int[] triangles = new int[(pointspercorner*4+1)*3*2+2*3*(pointspercorner*4-2)];
            GenerateColliderVertices(vertices, MajorRadius, MinorRadius, length, rodDiameter / 2, pointspercorner);
            colliderMesh.vertices = vertices;
            GenerateColliderTriangles(triangles, pointspercorner);
            colliderMesh.triangles = triangles;
            return colliderMesh;
        }

        private void GenerateColliderTriangles(int[] triangles, int pointsPerCorner)
        {
            #region Triangles
            int pointsInProfile = pointsPerCorner*4;
            int i = 0;
            for (int segment = 0; segment < pointsInProfile-1; segment++)
            {
                triangles[i++] = segment*2 + 0;
                triangles[i++] = segment*2 + 1;
                triangles[i++] = segment*2 + 2;

                triangles[i++] = segment*2 + 1;
                triangles[i++] = segment*2 + 3;
                triangles[i++] = segment*2 + 2;
            }
            triangles[i++] = 1;
            triangles[i++] = 0;
            triangles[i++] = pointsInProfile*2 - 2;

            triangles[i++] = 1;
            triangles[i++] = pointsInProfile*2 - 2;
            triangles[i++] = pointsInProfile*2 - 1;

            for (int sidePoint = 0; sidePoint < pointsInProfile-2; sidePoint++)
            {
                triangles[i++] = 0;
                triangles[i++] = 2*sidePoint+2;
                triangles[i++] = 2*sidePoint+4;

                triangles[i++] = 1;
                triangles[i++] = 2*sidePoint+5;
                triangles[i++] = 2*sidePoint+3;
            }
            #endregion
        }

        private void GenerateColliderVertices(Vector3[] vertices, float revolutionRadius, float majorFeatureRadius, float height, float filletRadius, int pointsPerCorner)
        {
            #region Vertices
            float _pihalf = Mathf.PI/2;
            float width = revolutionRadius*NormSideLength;

            for (int corner = 0; corner < 4; corner++)
            {
                float offsetX = ((corner == 0 | corner == 1) ? 1:-1)*(majorFeatureRadius-filletRadius);
                float offsetY = ((corner == 0 | corner == 3) ? 1:-1)*(height/2-filletRadius);
                for (int profilePoint = 0; profilePoint < pointsPerCorner; profilePoint++)
                {
                    float xPos = (Mathf.Sin(((float)profilePoint/(pointsPerCorner-1)+corner)*_pihalf)*filletRadius+offsetX);
                    Vector3 outVector = Vector3.forward*xPos;
                    Vector3 upVector = Vector3.up*(Mathf.Cos(((float)profilePoint/(pointsPerCorner-1)+corner)*_pihalf)*filletRadius+offsetY);
                    Vector3 sideVector = Vector3.right*width*(1+xPos/revolutionRadius);

                    vertices[2*profilePoint+2*(pointsPerCorner)*corner] = outVector + upVector + sideVector;
                    vertices[2*profilePoint+2*(pointsPerCorner)*corner+1] = outVector + upVector - sideVector;
                }
            }
            #endregion
        }

        private void GenerateMeshes(float bottomRadius, float topRadius, float height, float rodRadius, int nbRods)
        {
            float angle = tiltAngle;
            float maxMeshBendError = 0.01f;
            int nbRodSides = (int)Math.Max(Mathf.PI / Mathf.Acos(1 - maxMeshBendError / Mathf.Max(rodDiameter, maxMeshBendError)), 2) * 2;
            int vertPerRod = (nbRodSides + 0) * 2; // +1 if duplicate vertices at first angle is needed
            int nVert = vertPerRod * nbRods; // x2 for rods going each way
            int triPerRod = (nbRodSides) * 3 * 2;
            int nTri = triPerRod * nbRods; // x2 for rods going each way
            UncheckedMesh sideMesh = new UncheckedMesh(nVert, nTri);
            GenerateAllSideVertices(sideMesh, bottomRadius, topRadius, rodRadius, height, angle, nbRods, nbRodSides, 0);
            // GenerateAllSideVertices(sideMesh, bottomRadius, topRadius, rodRadius, height, angle, nbRods, nbRodSides, nVert / 2);
            GenerateAllSideTriangles(sideMesh, nbRods, nbRodSides, 0, 0);
            // GenerateAllSideTriangles(sideMesh, nbRods, nbRodSides, nVert / 2, nTri / 2);

            var tankULength = numSides * NormSideLength * (bottomRadius+topRadius) * 4;
            var tankVLength = length;

            RaiseChangeTextureScale("sides", PPart.legacyTextureHandler.SidesMaterial, new Vector2(tankULength, tankVLength));
            WriteToAppropriateMesh(sideMesh, PPart.SidesIconMesh, SidesMesh);

            int nVertCap = 2 * nbRodSides * nbRods * 2;
            int nTriCap = 2 * 3 * nbRodSides * nbRods * 2;
            UncheckedMesh capMesh = new UncheckedMesh(nVertCap, nTriCap);
            GenerateAllCapVertices(capMesh, bottomRadius, topRadius, rodRadius, height, angle, nbRods, nbRodSides, 0);
            // GenerateAllCapVertices(capMesh, bottomRadius, topRadius, rodRadius, height, 20f, nbRods, nbRodSides, nVertCap / 2);
            GenerateAllCapTriangles(capMesh, nbRods, nbRodSides, 0, 0);
            // GenerateAllCapTriangles(capMesh, nbRods, nbRodSides, nVertCap / 2, nTriCap / 2);
            WriteToAppropriateMesh(capMesh, PPart.EndsIconMesh, EndsMesh);
        }

        private void GenerateAllCapTriangles(UncheckedMesh capMesh, int nbRods, int nbRodSides, int vertOffset, int triOffset)
        {
            for (int rodNumber = 0; rodNumber < nbRods; rodNumber++)
            {
                int vertexOffset = 2 * rodNumber * (nbRodSides);
                int triangleOffset = 3 * 2 * rodNumber * (nbRodSides - 2);
                GenerateCapTriangles(capMesh, nbRodSides, vertexOffset + vertOffset, triangleOffset + triOffset);
            }
        }

        private void GenerateAllCapVertices(UncheckedMesh capMesh, float bottomRadius, float topRadius, float rodRadius, float height, float tiltAngle, int nbRods, int nbRodSides, int offset)
        {
            for (int rodNumber = 0; rodNumber < nbRods; rodNumber++)
            {
                int indexOffset = 2 * rodNumber * (nbRodSides + 0);
                float t1 = (float)rodNumber / nbRods * 2 * Mathf.PI;
                GenerateCapVertices(capMesh, t1, bottomRadius, topRadius, height, rodRadius, tiltAngle, nbRodSides, indexOffset + offset);
            }
        }

        private void GenerateAllSideTriangles(UncheckedMesh sideMesh, float nbRods, int nbRodSides, int vertOffset, int triOffset)
        {
            for (int rodNumber = 0; rodNumber < nbRods; rodNumber++)
            {
                int vertexOffset = 2 * rodNumber * (nbRodSides);
                int triangleOffset = 3*2 * rodNumber * nbRodSides;
                GenerateSideTriangles(sideMesh, nbRodSides, 2, vertexOffset + vertOffset, triangleOffset + triOffset);
            }
        }

        private void GenerateAllSideVertices(UncheckedMesh sideMesh, float bottomRadius, float topRadius, float rodRadius, float height, float tiltAngle, float nbRods, int nbRodSides, int offset)
        {
            for (int rodNumber = 0; rodNumber < nbRods; rodNumber++)
            {
                int indexOffset = 2 * rodNumber * (nbRodSides + 0);
                float t1 = (float)rodNumber / nbRods * 2 * Mathf.PI;
                GenerateSideVertices(sideMesh, t1, bottomRadius, topRadius, height, rodRadius, tiltAngle, nbRodSides, indexOffset + offset);
            }
        }

        private void GenerateCapTriangles(UncheckedMesh mesh, int nbSides, int vertexOffset, int triangleOffset)
        {
            int i = 0;
            for (int side = 0; side < nbSides - 2; side++)
            {
                // 0 2 4 6
                // 024 046
                mesh.triangles[triangleOffset + i++] = vertexOffset;
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 2;
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 4;

                mesh.triangles[triangleOffset + i++] = vertexOffset + 1;
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 5;
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 3;
            }
        }

        private void GenerateCapVertices(UncheckedMesh mesh, float angle, float bottomRadius, float topRadius, float height, float rodRadius, float tiltAngle, int nbSides, int offset)
        {
            Vector3 rodPos = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (bottomRadius + topRadius) / 2;
            float topBottomAngle = Mathf.Atan((bottomRadius - topRadius) / height);

            Vector3 rotNormal = Vector3.Cross(rodPos, Vector3.up);
            Quaternion rotation = Quaternion.AngleAxis(topBottomAngle * Mathf.Rad2Deg, rotNormal) * Quaternion.AngleAxis(tiltAngle, rodPos);
            for (int side = 0; side < nbSides; side++)
            {
                int currSide = side == nbSides ? 0 : side;

                float t1 = ((float)currSide / nbSides + 0.25f) * 2f * Mathf.PI + angle;
                Vector3 r1 = new Vector3(Mathf.Cos(t1) * rodRadius, 0f, Mathf.Sin(t1) * rodRadius);

                for (int profilePoint = 0; profilePoint < 2; profilePoint++)
                {
                    Vector3 yVector = Vector3.up * (profilePoint - 0.5f) * height / (Mathf.Cos(tiltAngle * Mathf.Deg2Rad) * Mathf.Cos(topBottomAngle));
                    mesh.vertices[offset + 2 * side + profilePoint] = rodPos + rotation * (r1 + yVector);
                    Vector3 normal = new Vector3(0f, (profilePoint - 0.5f) * 2, 0f);
                    mesh.normals[offset + 2 * side + profilePoint] = normal;
                    mesh.tangents[offset + 2 * side + profilePoint] = new Vector4(-Mathf.Sin(t1), 0, Mathf.Cos(t1), 1f);
                    mesh.uv[offset + 2 * side + profilePoint] = new Vector2(Mathf.Cos(t1) * (profilePoint - 0.5f) * 2, Mathf.Sin(t1)) / 2 + new Vector2(0.5f, 0.5f);
                }
            }
        }

        // Generate a single rod
        private void GenerateSideVertices(UncheckedMesh mesh, float angle, float bottomRadius, float topRadius, float height, float rodRadius, float tiltAngle, int nbSides, int offset)
        {
            Vector3 rodPos = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (bottomRadius + topRadius) / 2;
            float topBottomAngle = Mathf.Atan((bottomRadius - topRadius) / height);

            Vector3 rotNormal = Vector3.Cross(rodPos, Vector3.up);
            Quaternion rotation = Quaternion.AngleAxis(topBottomAngle * Mathf.Rad2Deg, rotNormal) * Quaternion.AngleAxis(tiltAngle, rodPos);
            for (int side = 0; side < nbSides; side++)
            {
                int currSide = side == nbSides ? 0 : side;

                float t1 = ((float)currSide / nbSides + 0.25f) * 2f * Mathf.PI + angle;
                Vector3 r1 = new Vector3(Mathf.Cos(t1) * rodRadius, 0f, Mathf.Sin(t1) * rodRadius);

                for (int profilePoint = 0; profilePoint < 2; profilePoint++)
                {
                    Vector3 yVector = Vector3.up * (profilePoint - 0.5f) * height / (Mathf.Cos(tiltAngle * Mathf.Deg2Rad) * Mathf.Cos(topBottomAngle));
                    mesh.vertices[offset+2*side+profilePoint] = rodPos + rotation * (r1 + yVector);
                    Vector3 normalInPlane = new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1));
                    Vector3 normal = (normalInPlane).normalized;
                    mesh.normals[offset+2*side+profilePoint] = normal;
                    mesh.tangents[offset+2*side+profilePoint] = new Vector4(-Mathf.Sin(t1), 0, Mathf.Cos(t1), 1f);
                    mesh.uv[offset+2*side+profilePoint] = new Vector2((float)side / (nbSides-1), profilePoint);
                }
            }
        }

        private void GenerateSideTriangles(UncheckedMesh mesh, int nbSides, int pointsInProfile, int vertexOffset, int triangleOffset)
        {
            int i = 0;
            for (int side = 0; side < nbSides; side++)
            {
                int current = side * pointsInProfile;
                int next = (side < (nbSides - 1) ? (side + 1) * pointsInProfile : 0);

                mesh.triangles[triangleOffset + i++] = vertexOffset + current;
                mesh.triangles[triangleOffset + i++] = vertexOffset + next + 1;
                mesh.triangles[triangleOffset + i++] = vertexOffset + next;

                mesh.triangles[triangleOffset + i++] = vertexOffset + current + 1;
                mesh.triangles[triangleOffset + i++] = vertexOffset + next + 1;
                mesh.triangles[triangleOffset + i++] = vertexOffset + current;
            }
        }

        private static void WriteToAppropriateMesh(UncheckedMesh mesh, Mesh iconMesh, Mesh normalMesh)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                mesh.WriteTo(iconMesh);
            }
            else
            {
                mesh.WriteTo(normalMesh);
            }
        }
    }
}
