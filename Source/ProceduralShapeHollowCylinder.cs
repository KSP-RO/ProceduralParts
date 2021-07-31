using System;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{
    class ProceduralShapeHollowCylinder : ProceduralAbstractShape
    {
        private const string ModTag = "[ProceduralShapeHollowCylinder]";

        #region Config parameters

        [KSPField(guiActiveEditor = true, guiName = "Diameters", groupName = ProceduralPart.PAWGroupName)]
        private string diamTitleString = "";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Inner", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float innerDiameter = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Outer", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float outerDiameter = 2f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Length", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float length = 1f;

        private float maxError = 0.0125f;

        public int numSides => (int)Math.Max(Mathf.PI * Mathf.Sqrt(Mathf.Sqrt(outerDiameter)/(2f * maxError)), 24);

        [KSPField]
        public string TopNodeName = "top";

        [KSPField]
        public string BottomNodeName = "bottom";

        #endregion

        #region Utility Properties

        private float CornerCenterCornerAngle => 2 * Mathf.PI / numSides;
        private float NormSideLength => Mathf.Tan(CornerCenterCornerAngle / 2);

        #endregion

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsEditor)
            {
                UpdateTechConstraints();
                Fields[nameof(innerDiameter)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(outerDiameter)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(length)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
            }
        }

        public override void AdjustDimensionBounds()
        {
            float maxOuterDiameter = PPart.diameterMax;
            float maxInnerDiameter = PPart.diameterMax;
            float minOuterDiameter = PPart.diameterMin;
            float minInnerDiameter = PPart.diameterMin;

            float minLength = PPart.lengthMin;

            maxOuterDiameter = Mathf.Clamp(maxOuterDiameter, PPart.diameterMin, PPart.diameterMax);
            maxInnerDiameter = Mathf.Clamp(maxInnerDiameter, PPart.diameterMin, PPart.diameterMax);

            maxInnerDiameter = Mathf.Clamp(maxInnerDiameter, PPart.diameterMin, outerDiameter - PPart.diameterMin);
            minOuterDiameter = Mathf.Clamp(minOuterDiameter, innerDiameter + PPart.diameterMin, maxOuterDiameter);

            minLength = Mathf.Clamp(minLength, PPart.lengthMin, PPart.lengthMax - PPart.lengthSmallStep);

            (Fields[nameof(outerDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxOuterDiameter;
            (Fields[nameof(outerDiameter)].uiControlEditor as UI_FloatEdit).minValue = minOuterDiameter;
            (Fields[nameof(innerDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxInnerDiameter;
            (Fields[nameof(innerDiameter)].uiControlEditor as UI_FloatEdit).minValue = minInnerDiameter;

            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).minValue = minLength;
        }

        public override float CalculateVolume()
        {
            // Volume of large cylinder - volume of small cylinder
            return Mathf.PI * length / 4f * (outerDiameter * outerDiameter - innerDiameter * innerDiameter);
        }

        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r /= (outerDiameter / 2);
            coords.y /= length;
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r *= (outerDiameter / 2);
            coords.y *= length;
        }

        public override bool SeekVolume(float targetVolume, int dir) => SeekVolume(targetVolume, Fields[nameof(length)], dir);

        public override void TranslateAttachmentsAndNodes(BaseField f, object obj)
        {
            if (f.name == nameof(length) && obj is float oldLength)
            {
                HandleLengthChange(length, oldLength);
            }
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

            AdjustDimensionBounds();
            innerDiameter = Mathf.Clamp(innerDiameter, innerDiameterEdit.minValue, innerDiameterEdit.maxValue);
            outerDiameter = Mathf.Clamp(outerDiameter, outerDiameterEdit.minValue, outerDiameterEdit.maxValue);
            length = Mathf.Clamp(length, lengthEdit.minValue, lengthEdit.maxValue);
        }
        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", outerDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", innerDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }

        internal override void InitializeAttachmentNodes() => InitializeAttachmentNodes(length, outerDiameter);

        internal override void UpdateShape(bool force = true)
        {
            part.CoMOffset = CoMOffset;
            Volume = CalculateVolume();
            GenerateMeshes(outerDiameter / 2, innerDiameter / 2, length, numSides);

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
                RaiseChangeAttachNodeSize(node, innerDiameter, Mathf.PI * innerDiameter * innerDiameter * 0.25f + Mathf.PI * length * (innerDiameter + outerDiameter));
            }
        }

        private void GenerateColliders()
        {
            PPart.clearColliderHolder();
            // The first corner is at angle=0.
            // We want to start the colliders in between the corners.
            float offset = (360f / numSides) / 2 - 90f;

            for (int i=0; i<numSides; i++)
            {
                var go = new GameObject($"Mesh_Collider_{i}");
                var coll = go.AddComponent<MeshCollider>();
                go.transform.SetParent(PPart.ColliderHolder.transform, false);
                coll.convex = true;
                coll.sharedMesh = GenerateColliderMesh();
                var orientation = Quaternion.AngleAxis(90 + offset + (360f * i / numSides), Vector3.up);
                go.transform.localRotation *= orientation;
                go.transform.localPosition = Vector3.zero;
            }
        }

        private Mesh GenerateColliderMesh()
        {
            Mesh colliderMesh = new Mesh();
            Vector3[] vertices = new Vector3[8];
            int[] triangles = new int[36];
            GenerateColliderVertices(vertices, outerDiameter / 2, innerDiameter / 2, length);
            colliderMesh.vertices = vertices;
            GenerateColliderTriangles(triangles, 4);
            colliderMesh.triangles = triangles;
            return colliderMesh;
        }

        private void GenerateColliderTriangles(int[] triangles, int pointsInProfile)
        {
            // Profile triangles
            int i = 0;
            for (int segment = 0; segment < pointsInProfile-1; segment++)
            {
                triangles[i++] = segment * 2 + 0;
                triangles[i++] = segment * 2 + 1;
                triangles[i++] = segment * 2 + 2;

                triangles[i++] = segment * 2 + 1;
                triangles[i++] = segment * 2 + 3;
                triangles[i++] = segment * 2 + 2;
            }
            // Final triangles to close the shape
            triangles[i++] = 1;
            triangles[i++] = 0;
            triangles[i++] = pointsInProfile * 2 - 2;

            triangles[i++] = 1;
            triangles[i++] = pointsInProfile * 2 - 2;
            triangles[i++] = pointsInProfile * 2 - 1;

            // Side triangles
            for (int sidePoint = 0; sidePoint < pointsInProfile-2; sidePoint++)
            {
                triangles[i++] = 0;
                triangles[i++] = 2 * sidePoint + 2;
                triangles[i++] = 2 * sidePoint + 4;

                triangles[i++] = 1;
                triangles[i++] = 2 * sidePoint + 5;
                triangles[i++] = 2 * sidePoint + 3;
            }
        }

        private void GenerateColliderVertices(Vector3[] vertices, float outerRadius, float innerRadius, float height)
        {
            float width = NormSideLength;

            for (int corner = 0; corner < 4; corner++)
            {
                float xLength = ((corner == 0 | corner == 1) ? outerRadius : innerRadius);
                xLength *= Mathf.Cos(CornerCenterCornerAngle / 2);
                float offsetY = ((corner == 0 | corner == 3) ? 1 : -1) * (height / 2);
                Vector3 outVector = Vector3.forward * xLength;
                Vector3 upVector = Vector3.up * offsetY;
                Vector3 sideVector = Vector3.right * width * xLength;

                vertices[2 * corner] = outVector + upVector + sideVector;
                vertices[2 * corner + 1] = outVector + upVector - sideVector;
            }
        }

        private void GenerateMeshes(float radius1, float radius2, float height, int nbSides)
        {
            int verticalPoints = 2;
            UncheckedMesh sideMesh = new UncheckedMesh(verticalPoints * 2 * (nbSides + 1), (verticalPoints - 1) * 6 * (nbSides + 1));
            GenerateSideVertices(sideMesh, true, radius1, radius2, height, nbSides, verticalPoints - 1, 0);
            GenerateSideVertices(sideMesh, false, radius1, radius2, height, nbSides, verticalPoints - 1, verticalPoints * (nbSides + 1));
            GenerateSideTriangles(sideMesh, true, nbSides, verticalPoints, 0, 0);
            GenerateSideTriangles(sideMesh, false, nbSides, verticalPoints, verticalPoints * (nbSides + 1), (verticalPoints - 1) * 6 * (nbSides + 1));

            var tankULength = numSides * NormSideLength * radius1 * 2;
            var tankVLength = length;

            RaiseChangeTextureScale("sides", PPart.legacyTextureHandler.SidesMaterial, new Vector2(tankULength, tankVLength));
            WriteToAppropriateMesh(sideMesh, PPart.SidesIconMesh, SidesMesh);

            UncheckedMesh capMesh = new UncheckedMesh(4 * (nbSides + 1), 4 * (nbSides + 1));
            GenerateCapVertices(capMesh, true, radius1, radius2, height, nbSides, 0);
            GenerateCapVertices(capMesh, false, radius1, radius2, height, nbSides, 2 * (nbSides + 1));
            GenerateCapTriangles(capMesh, true, nbSides, 0, 0);
            GenerateCapTriangles(capMesh, false, nbSides, 2 * (nbSides + 1), 6 * (nbSides + 1));
            WriteToAppropriateMesh(capMesh, PPart.EndsIconMesh, EndsMesh);
        }

        private void GenerateCapTriangles(UncheckedMesh mesh, bool up, int nbSides, int vertexOffset, int triangleOffset)
        {
            int i = 0;
            for (int side = 0; side < nbSides; side++)
            {
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2;
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + (up ? 1 : 2);
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + (up ? 2 : 1);

                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + (up ? 1 : 3);
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + (up ? 3 : 1);
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 2;
            }
        }

        private void GenerateCapVertices(UncheckedMesh mesh, bool top, float outsideRadius, float insideRadius, float height, int nbSides, int offset)
        {
            for (int side = 0; side <= nbSides; side++)
            {
                int currSide = side == nbSides ? 0 : side;

                // Angle around the part, offset to align texture with other parts orientation
                float t1 = ((float)currSide / nbSides + 0.25f) * 2f * Mathf.PI;

                Vector3 r1 = new Vector3(Mathf.Cos(t1) * outsideRadius, 0f, Mathf.Sin(t1) * outsideRadius);
                Vector3 r2 = new Vector3(Mathf.Cos(t1) * insideRadius, 0f, Mathf.Sin(t1) * insideRadius);

                mesh.vertices[offset + 2 * side] = r1 + Vector3.up * height / 2 * (top ? 1 : -1);
                mesh.vertices[offset + 2 * side + 1] = r2 + Vector3.up * height / 2 * (top ? 1 : -1);

                mesh.normals[offset + 2 * side] = Vector3.up * (top ? 1 : -1);
                mesh.normals[offset + 2 * side + 1] = Vector3.up * (top ? 1 : -1);

                mesh.uv[offset + 2 * side] = new Vector2(Mathf.Cos(t1) * (top ? 1 : -1), Mathf.Sin(t1)) / 2 + new Vector2(0.5f, 0.5f);
                mesh.uv[offset + 2 * side + 1] = new Vector2(Mathf.Cos(t1) * (top ? 1 : -1), Mathf.Sin(t1)) * (insideRadius) / (outsideRadius) / 2 + new Vector2(0.5f, 0.5f);
                mesh.tangents[offset + 2 * side] = new Vector4(1, 0, 0, 1f);
                mesh.tangents[offset + 2 * side + 1] = new Vector4(1, 0, 0, 1f);
            }
        }

        private void GenerateSideVertices(UncheckedMesh mesh, bool outside, float outerRadius, float innerRadius, float height, int nbSides, int verticalPoints, int offset)
        {
            for (int side = 0; side <= nbSides; side++)
            {
                int currSide = side == nbSides ? 0 : side;

                // Angle around the part, offset to align texture with other parts orientation
                float t1 = ((float)currSide / nbSides + 0.25f) * 2f * Mathf.PI;

                for (int verticalPoint = 0; verticalPoint <= verticalPoints; verticalPoint++)
                {
                    float xLength = outside ? outerRadius : innerRadius;
                    Vector3 xVector = Quaternion.AngleAxis(-t1 * Mathf.Rad2Deg, Vector3.up) * new Vector3((outside ? 1:1), 0)*xLength;
                    Vector3 yVector = Vector3.up*(0.5f - verticalPoint)*height;

                    mesh.vertices[offset + ((verticalPoints + 1) * side + verticalPoint)] = xVector + yVector;
                    Vector3 normal = new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1)) * (outside ? 1 : -1);
                    mesh.normals[offset + ((verticalPoints + 1) * side + verticalPoint)] = normal;
                    mesh.tangents[offset + ((verticalPoints + 1) * side + verticalPoint)] = new Vector4(-Mathf.Sin(t1), 0, Mathf.Cos(t1), (outside ? -1 : 1));
                    mesh.uv[offset + ((verticalPoints + 1) * side + verticalPoint)] = new Vector2((float)side / nbSides * (outside ? 1 : -1), 1 - verticalPoint);
                }
            }
        }

        private void GenerateSideTriangles(UncheckedMesh mesh, bool outside, int nbSides, int pointsInProfile, int vertexOffset, int triangleOffset)
        {
            int i = 0;
            for (int side = 0; side <= nbSides; side++)
            {
                for (int segment = 0; segment <= pointsInProfile - 2; segment++)
                {
                    int current = segment + side * pointsInProfile;
                    int next = segment + (side < nbSides ? (side + 1) * pointsInProfile : 0);

                    mesh.triangles[triangleOffset + i++] = vertexOffset + current;
                    mesh.triangles[triangleOffset + i++] = vertexOffset + next + (outside ? 0 : 1);
                    mesh.triangles[triangleOffset + i++] = vertexOffset + next + (outside ? 1 : 0);

                    mesh.triangles[triangleOffset + i++] = vertexOffset + current + (outside ? 0 : 1);
                    mesh.triangles[triangleOffset + i++] = vertexOffset + next + 1;
                    mesh.triangles[triangleOffset + i++] = vertexOffset + current + (outside ? 1 : 0);
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
