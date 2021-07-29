using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{
    class ProceduralShapeHollowPill : ProceduralAbstractShape
    {
        private const string ModTag = "[ProceduralShapeHollowPill]";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Inner D", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float innerDiameter = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Outer D", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float outerDiameter = 2f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Length", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float length = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Fillet", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float fillet = 0f;

        private float maxError = 0.01f;

        public int numSides => (int)Math.Max(Mathf.PI / Mathf.Acos(1 - maxError / outerDiameter), 24);

        public float MajorRadius => (outerDiameter + innerDiameter) / 4;
        public float MinorRadius => (outerDiameter - innerDiameter) / 4;

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
                Fields[nameof(innerDiameter)].uiControlEditor.onFieldChanged = ClampFillet;
                Fields[nameof(innerDiameter)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;
                Fields[nameof(outerDiameter)].uiControlEditor.onFieldChanged = ClampFillet;
                Fields[nameof(outerDiameter)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;
                Fields[nameof(length)].uiControlEditor.onFieldChanged = ClampFillet;
                Fields[nameof(length)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;
                Fields[nameof(fillet)].uiControlEditor.onFieldChanged = ClampFillet;
                Fields[nameof(fillet)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;

                Fields[nameof(outerDiameter)].uiControlEditor.onSymmetryFieldChanged =
                Fields[nameof(innerDiameter)].uiControlEditor.onSymmetryFieldChanged =
                Fields[nameof(length)].uiControlEditor.onSymmetryFieldChanged =
                Fields[nameof(fillet)].uiControlEditor.onSymmetryFieldChanged = ClampFillet;
            }
        }

        private void ClampFillet(BaseField f, object obj)
        {
            if (fillet > Mathf.Min((outerDiameter - innerDiameter) / 2f, length) + 0.001f)
            {
                float oldFillet = fillet;
                fillet = Mathf.Min((outerDiameter - innerDiameter) / 2f, length);
                MonoUtilities.RefreshPartContextWindow(part);
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
                maxOuterDiameter = majorRadMax * 2 - innerDiameter;
                maxInnerDiameter = -(minorRadMax * 2 - outerDiameter);
            }

            maxOuterDiameter = Mathf.Clamp(maxOuterDiameter, PPart.diameterMin, PPart.diameterMax);
            maxInnerDiameter = Mathf.Clamp(maxInnerDiameter, PPart.diameterMin, PPart.diameterMax);
            maxInnerDiameter = Mathf.Clamp(maxInnerDiameter, PPart.diameterMin, outerDiameter - PPart.diameterMin);

            minOuterDiameter = Mathf.Clamp(minOuterDiameter, innerDiameter + PPart.diameterMin, maxOuterDiameter);

            maxFillet = Mathf.Clamp(maxFillet, 0, length);
            maxFillet = Mathf.Clamp(maxFillet, 0, (outerDiameter - innerDiameter) / 2f);

            (Fields[nameof(outerDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxOuterDiameter;
            (Fields[nameof(outerDiameter)].uiControlEditor as UI_FloatEdit).minValue = minOuterDiameter;
            (Fields[nameof(innerDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxInnerDiameter;
            (Fields[nameof(innerDiameter)].uiControlEditor as UI_FloatEdit).minValue = minInnerDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).minValue = PPart.lengthMin;
            (Fields[nameof(fillet)].uiControlEditor as UI_FloatEdit).minValue = 0;
            (Fields[nameof(fillet)].uiControlEditor as UI_FloatEdit).maxValue = maxFillet;

        }

        public override float CalculateVolume()
        {
            // Using Pappus's centroid theorem: Volume = area of profile * path traveled by geometric centre of profile
            // area of profile: circle, pi * fillet * fillet / 4
            //                  rectangle, height * (outerDiam - innerdiam) - fillet * fillet
            // path of geometric centre: 2 * pi * majorRadius
            return ((Mathf.PI / 4f - 1f) * fillet * fillet + length * (outerDiameter - innerDiameter)) * 2 * Mathf.PI * MajorRadius;
        }

        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r /= (outerDiameter / 2);
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
            coords.r *= (outerDiameter / 2);
            coords.y *= length;
        }

        public override void UpdateTechConstraints()
        {
            Fields[nameof(innerDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit innerDiameterEdit = Fields[nameof(innerDiameter)].uiControlEditor as UI_FloatEdit;
            innerDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            innerDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(outerDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit outerDiameterEdit = Fields[nameof(outerDiameter)].uiControlEditor as UI_FloatEdit;
            outerDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            outerDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(length)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit lengthEdit = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
            lengthEdit.incrementLarge = PPart.lengthLargeStep;
            lengthEdit.incrementSmall = PPart.lengthSmallStep;

            Fields[nameof(fillet)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit filletEdit = Fields[nameof(fillet)].uiControlEditor as UI_FloatEdit;
            filletEdit.incrementLarge = PPart.lengthLargeStep;
            filletEdit.incrementSmall = PPart.lengthSmallStep;

            AdjustDimensionBounds();
            innerDiameter = Mathf.Clamp(innerDiameter, innerDiameterEdit.minValue, innerDiameterEdit.maxValue);
            outerDiameter = Mathf.Clamp(outerDiameter, outerDiameterEdit.minValue, outerDiameterEdit.maxValue);
            length = Mathf.Clamp(length, lengthEdit.minValue, lengthEdit.maxValue);
            fillet = Mathf.Clamp(fillet, filletEdit.minValue, filletEdit.maxValue);
        }
        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", innerDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", outerDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }

        internal override void InitializeAttachmentNodes() => InitializeAttachmentNodes(length, outerDiameter);

        internal override void UpdateShape(bool force = true)
        {
            part.CoMOffset = CoMOffset;
            Volume = CalculateVolume();
            GenerateMeshes(MajorRadius, MinorRadius, length, fillet / 2, (int)numSides);

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
                node.size = Math.Min((int)(innerDiameter / PPart.diameterLargeStep), 3);
                node.breakingTorque = node.breakingForce = Mathf.Max(50 * node.size * node.size, 50);
                RaiseChangeAttachNodeSize(node, innerDiameter, Mathf.PI * innerDiameter * innerDiameter * 0.25f + Mathf.PI * length * (innerDiameter+outerDiameter));
            }
        }

        private void GenerateColliders()
        {
            gameObject.GetComponentsInChildren<SphereCollider>().FirstOrDefault(c => c.name.Equals("Central_Sphere_Collider"))?.gameObject.DestroyGameObject();

            PPart.clearColliderHolder();
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
            int pointspercorner = (int)Math.Min(Math.Max(Mathf.PI / Mathf.Acos(1 - maxColliderError / Mathf.Max(fillet, maxColliderError)), 1), 30);
            Mesh colliderMesh = new Mesh();
            Vector3[] vertices = new Vector3[2*pointspercorner*4];
            int[] triangles = new int[(pointspercorner*4+1)*3*2+2*3*(pointspercorner*4-2)];
            GenerateColliderVertices(vertices, MajorRadius, MinorRadius, length, fillet / 2, pointspercorner);
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

        private void GenerateMeshes(float radius1, float radius2, float height, float filletRadius, int nbSides)
        {
            float maxMeshBendError = 0.01f;
            int pointsperprofile = (int)Math.Max(Mathf.PI / Mathf.Acos(1 - maxMeshBendError / Mathf.Max(fillet, maxMeshBendError)), 2) * 2;
            UncheckedMesh sideMesh = new UncheckedMesh(2*pointsperprofile*(nbSides+1), (pointsperprofile-1)*3*(nbSides+1)*2);
            GenerateSideVertices(sideMesh, true, radius1, radius2, height, filletRadius, pointsperprofile, nbSides, 0);
            GenerateSideVertices(sideMesh, false, radius1, radius2, height, filletRadius, pointsperprofile, nbSides, pointsperprofile*(nbSides+1));
            GenerateSideTriangles(sideMesh, true, nbSides, pointsperprofile, 0, 0);
            GenerateSideTriangles(sideMesh, false, nbSides, pointsperprofile, pointsperprofile*(nbSides+1), (pointsperprofile-1)*2*3*(nbSides+1));

            var tankULength = numSides * NormSideLength * (radius1+radius2) * 4;
            var tankVLength = length;

            RaiseChangeTextureScale("sides", PPart.legacyTextureHandler.SidesMaterial, new Vector2(tankULength, tankVLength));
            WriteToAppropriateMesh(sideMesh, PPart.SidesIconMesh, SidesMesh);

            UncheckedMesh capMesh = new UncheckedMesh(2*2*(nbSides+1), 2*2*(nbSides+1));
            GenerateCapVertices(capMesh, true, radius1, radius2-filletRadius, height, nbSides, 0);
            GenerateCapVertices(capMesh, false, radius1, radius2-filletRadius, height, nbSides, 2*(nbSides+1));
            GenerateCapTriangles(capMesh, true, nbSides, 0, 0);
            GenerateCapTriangles(capMesh, false, nbSides, 2*(nbSides+1), 2*3*(nbSides+1));
            WriteToAppropriateMesh(capMesh, PPart.EndsIconMesh, EndsMesh);
        }

        private void GenerateCapTriangles(UncheckedMesh mesh, bool up, int nbSides, int vertexOffset, int triangleOffset)
        {
            int i = 0;
            for (int side = 0; side < nbSides; side++)
            {
                if (up)
                {
                    mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 0;
                    mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 1;
                    mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 2;

                    mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 1;
                    mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 3;
                    mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 2;
                } else
                {
                    mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 0;
                    mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 2;
                    mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 1;

                    mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 3;
                    mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 1;
                    mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 2;
                }
            }
        }

        private void GenerateCapVertices(UncheckedMesh mesh, bool top, float revolutionRadius, float majorFeatureRadius, float height, int nbSides, int offset)
        {
            #region Vertices
            float _2pi = Mathf.PI * 2f;
            for (int side = 0; side <= nbSides; side++)
            {
                int currSide = side == nbSides ? 0 : side;

                float t1 = (float)currSide / nbSides * _2pi;
                Vector3 r1 = new Vector3(Mathf.Cos(t1) * revolutionRadius, 0f, Mathf.Sin(t1) * revolutionRadius);
                Vector3 r2 = Quaternion.AngleAxis(-t1 * Mathf.Rad2Deg, Vector3.up) * new Vector3(majorFeatureRadius, 0);

                mesh.vertices[offset + 2*side] = r1 + r2 + Vector3.up*height/2*(top ? 1:-1);
                mesh.vertices[offset + 2*side+1] = r1 - r2 + Vector3.up*height/2*(top ? 1:-1);
            }
            #endregion

            #region Normals
            for (int side = 0; side <= nbSides; side++)
            {
                // ugly, but quick to write
                mesh.normals[offset + 2*side] = Vector3.up*(top ? 1:-1);
                mesh.normals[offset + 2*side+1] = Vector3.up*(top ? 1:-1);
            }
            #endregion

            #region UVs
            for (int side = 0; side <= nbSides; side++)
            {
                int currSide = side == nbSides ? 0 : side;

                float t1 = ((float)currSide / nbSides + 0.25f) * 2f * Mathf.PI;
                mesh.uv[offset + 2*side] = new Vector2(Mathf.Cos(t1)*(top ? 1:-1), Mathf.Sin(t1))/2 + new Vector2(0.5f, 0.5f);
                mesh.uv[offset + 2*side+1] = new Vector2(Mathf.Cos(t1)*(top ? 1:-1), Mathf.Sin(t1))*(revolutionRadius-majorFeatureRadius)/(revolutionRadius+majorFeatureRadius)/2 + new Vector2(0.5f, 0.5f);
                mesh.tangents[offset + 2*side] = new Vector4(1, 0, 0, 1f);
                mesh.tangents[offset + 2*side+1] = new Vector4(1, 0, 0, 1f);
            }
            #endregion
        }

        private void GenerateSideVertices(UncheckedMesh mesh, bool outside, float revolutionRadius, float majorFeatureRadius, float height, float filletRadius, int pointsInProfile, int nbSides, int offset)
        {
            int pointsInCornerProfile = pointsInProfile/2;
            #region Vertices
            float _2pi = Mathf.PI * 2f;
            for (int side = 0; side <= nbSides; side++)
            {
                int currSide = side == nbSides ? 0 : side;

                float t1 = ((float)currSide / nbSides + 0.25f) * 2f * Mathf.PI;
                Vector3 r1 = new Vector3(Mathf.Cos(t1) * revolutionRadius, 0f, Mathf.Sin(t1) * revolutionRadius);

                foreach (bool top in new [] { true, false})
                {
                    for (int profilePoint = 0; profilePoint < pointsInCornerProfile; profilePoint++)
                    {
                        Vector3 xVector = Quaternion.AngleAxis(-t1 * Mathf.Rad2Deg, Vector3.up) * new Vector3((outside ? 1:-1), 0)*(majorFeatureRadius-filletRadius*(1-Mathf.Sin(((float)profilePoint/(pointsInCornerProfile-1)+(top ? 0:1))*Mathf.PI/2)));
                        Vector3 yVector = Vector3.up*(top ? 1:-1)*(height/2-filletRadius*(1-Mathf.Cos(((float)profilePoint/(pointsInCornerProfile-1)-(top ? 0:1))*Mathf.PI/2)));

                        mesh.vertices[offset+(2*side+(top?0:1))*pointsInCornerProfile+profilePoint] = r1 + xVector + yVector;
                        Vector3 normalInPlane = new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1))*(outside ? 1:-1)*(Mathf.Sin(((float)profilePoint/(pointsInCornerProfile-1)+(top ? 0f:1f))*Mathf.PI/2));
                        Vector3 normalUp = Vector3.up*(Mathf.Cos(((float)profilePoint/(pointsInCornerProfile-1)+(top ? 0f:1f))*Mathf.PI/2));
                        Vector3 normal = (normalInPlane+normalUp).normalized;
                        mesh.normals[offset+(2*side+(top?0:1))*pointsInCornerProfile+profilePoint] = normal;
                        mesh.tangents[offset+(2*side+(top?0:1))*pointsInCornerProfile+profilePoint] = new Vector4(-Mathf.Sin(t1), 0, Mathf.Cos(t1), (outside?-1:1));
                        float distanceIntoBend = (float)profilePoint/(pointsInCornerProfile-1)*_2pi/4*filletRadius;
                        mesh.uv[offset+(2*side+(top?0:1))*pointsInCornerProfile+profilePoint] = new Vector2((float)side / nbSides, 1-(distanceIntoBend+(top?0:1)*(height+filletRadius*(_2pi/4-2)))/(height+filletRadius*(_2pi/2-2)));
                    }
                }
            }
            #endregion
        }

        private void GenerateSideTriangles(UncheckedMesh mesh, bool outside, int nbSides, int pointsInProfile, int vertexOffset, int triangleOffset)
        {
            int nbFaces = mesh.vertices.Length;
            int nbTriangles = nbFaces * 2;
            int nbIndexes = nbTriangles*3;
            int i = 0;
            for (int side = 0; side <= nbSides; side++)
            {
                for (int segment = 0; segment <= pointsInProfile - 2; segment++)
                {
                    int current = segment + side * pointsInProfile;
                    int next = segment + (side < (nbSides) ? (side + 1) * pointsInProfile : 0);

                    if (outside)
                    {
                        mesh.triangles[triangleOffset + i++] = vertexOffset + current;
                        mesh.triangles[triangleOffset + i++] = vertexOffset + next;
                        mesh.triangles[triangleOffset + i++] = vertexOffset + next + 1;

                        mesh.triangles[triangleOffset + i++] = vertexOffset + current;
                        mesh.triangles[triangleOffset + i++] = vertexOffset + next + 1;
                        mesh.triangles[triangleOffset + i++] = vertexOffset + current + 1;
                    } else
                    {
                        mesh.triangles[triangleOffset + i++] = vertexOffset + current;
                        mesh.triangles[triangleOffset + i++] = vertexOffset + next + 1;
                        mesh.triangles[triangleOffset + i++] = vertexOffset + next;

                        mesh.triangles[triangleOffset + i++] = vertexOffset + current + 1;
                        mesh.triangles[triangleOffset + i++] = vertexOffset + next + 1;
                        mesh.triangles[triangleOffset + i++] = vertexOffset + current;
                    }
                }
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
