using System;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{
    class ProceduralShapeHollowCone : ProceduralAbstractShape
    {
        private const string ModTag = "[ProceduralShapeHollowCone]";
        public override Vector3 CoMOffset => CoMOffset_internal();

        #region Config parameters

        [KSPField(guiActiveEditor = true, guiName = "Top diameters", groupName = ProceduralPart.PAWGroupName)]
        private string topTitleString = "";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Inner", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float topInnerDiameter = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Outer", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float topOuterDiameter = 2f;

        [KSPField(guiActiveEditor = true, guiName = "Bottom diameters", groupName = ProceduralPart.PAWGroupName)]
        private string bottomTitleString = "";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Inner", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float bottomInnerDiameter = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Outer", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float bottomOuterDiameter = 2f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Length", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float length = 1f;

        private const float maxError = 0.0125f;

        public int numSides => (int)Math.Max(Mathf.PI * Mathf.Sqrt(Mathf.Sqrt((Math.Max(bottomOuterDiameter, topOuterDiameter)))/(2f * maxError)), 24);

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
                Fields[nameof(bottomInnerDiameter)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;
                Fields[nameof(bottomOuterDiameter)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;
                Fields[nameof(topInnerDiameter)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;
                Fields[nameof(topOuterDiameter)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;
                Fields[nameof(length)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;
            }
        }

        public override void AdjustDimensionBounds()
        {
            float bottomMaxOuterDiameter = PPart.diameterMax;
            float bottomMaxInnerDiameter = PPart.diameterMax;
            float bottomMinOuterDiameter = PPart.diameterMin;
            float bottomMinInnerDiameter = PPart.diameterMin;

            float topMaxOuterDiameter = PPart.diameterMax;
            float topMaxInnerDiameter = PPart.diameterMax;
            float topMinOuterDiameter = PPart.diameterMin;
            float topMinInnerDiameter = PPart.diameterMin;

            float minLength = PPart.lengthMin;

            // Clamp bottom diameters
            bottomMaxOuterDiameter = Mathf.Clamp(bottomMaxOuterDiameter, PPart.diameterMin, PPart.diameterMax);
            bottomMaxInnerDiameter = Mathf.Clamp(bottomMaxInnerDiameter, PPart.diameterMin, PPart.diameterMax);

            bool bottomAllowedZero = topOuterDiameter > topInnerDiameter;
            bottomMaxInnerDiameter = Mathf.Clamp(bottomMaxInnerDiameter, PPart.diameterMin, bottomOuterDiameter - (bottomAllowedZero ? 0f : PPart.diameterMin));
            bottomMinOuterDiameter = Mathf.Clamp(bottomMinOuterDiameter, bottomInnerDiameter + (bottomAllowedZero ? 0f : PPart.diameterMin), bottomMaxOuterDiameter);

            // Clamp top diameters
            topMaxOuterDiameter = Mathf.Clamp(topMaxOuterDiameter, PPart.diameterMin, PPart.diameterMax);
            topMaxInnerDiameter = Mathf.Clamp(topMaxInnerDiameter, PPart.diameterMin, PPart.diameterMax);

            bool topAllowedZero = bottomOuterDiameter > bottomInnerDiameter;
            topMaxInnerDiameter = Mathf.Clamp(topMaxInnerDiameter, PPart.diameterMin, topOuterDiameter - (topAllowedZero ? 0f : PPart.diameterMin));
            topMinOuterDiameter = Mathf.Clamp(topMinOuterDiameter, topInnerDiameter + (topAllowedZero ? 0f : PPart.diameterMin), topMaxOuterDiameter);

            minLength = Mathf.Clamp(minLength, PPart.lengthMin, PPart.lengthMax - PPart.lengthSmallStep);

            // Bottom diameters
            (Fields[nameof(bottomOuterDiameter)].uiControlEditor as UI_FloatEdit).maxValue = bottomMaxOuterDiameter;
            (Fields[nameof(bottomOuterDiameter)].uiControlEditor as UI_FloatEdit).minValue = bottomMinOuterDiameter;
            (Fields[nameof(bottomInnerDiameter)].uiControlEditor as UI_FloatEdit).maxValue = bottomMaxInnerDiameter;
            (Fields[nameof(bottomInnerDiameter)].uiControlEditor as UI_FloatEdit).minValue = bottomMinInnerDiameter;

            // Top diameters
            (Fields[nameof(topOuterDiameter)].uiControlEditor as UI_FloatEdit).maxValue = topMaxOuterDiameter;
            (Fields[nameof(topOuterDiameter)].uiControlEditor as UI_FloatEdit).minValue = topMinOuterDiameter;
            (Fields[nameof(topInnerDiameter)].uiControlEditor as UI_FloatEdit).maxValue = topMaxInnerDiameter;
            (Fields[nameof(topInnerDiameter)].uiControlEditor as UI_FloatEdit).minValue = topMinInnerDiameter;

            // Length
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).minValue = minLength;

        }

        public override float CalculateVolume()
        {
            // Volume of large truncated cone - volume of small truncated cone
            return Mathf.PI * length / 12f * (bottomOuterDiameter * bottomOuterDiameter + bottomOuterDiameter * topOuterDiameter + topOuterDiameter * topOuterDiameter
                                             - bottomInnerDiameter * bottomInnerDiameter - bottomInnerDiameter * topInnerDiameter - topInnerDiameter * topInnerDiameter);
        }

        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r /= (bottomOuterDiameter / 2);
            coords.y /= length;
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r *= (bottomOuterDiameter / 2);
            coords.y *= length;
        }

        public override bool SeekVolume(float targetVolume, int dir) => SeekVolume(targetVolume, Fields[nameof(length)], dir);

        public override void TranslateAttachmentsAndNodes(BaseField f, object obj)
        {
            if (f.name == nameof(bottomOuterDiameter) && obj is float oldBottomDiameter)
            {
                HandleDiameterChange((bottomOuterDiameter + topOuterDiameter) / 2, (oldBottomDiameter + topOuterDiameter) / 2);
            }
            if (f.name == nameof(topOuterDiameter) && obj is float oldTopDiameter)
            {
                HandleDiameterChange((topOuterDiameter + bottomOuterDiameter) / 2, (oldTopDiameter + bottomOuterDiameter) / 2);
            }
            if (f.name == nameof(length) && obj is float oldLength)
            {
                HandleLengthChange(length, oldLength);
            }
        }

        private void HandleDiameterChange(BaseField f, object obj)
        {
            if ((f.name == nameof(topOuterDiameter) || f.name == nameof(bottomOuterDiameter)) && obj is float prevDiam)
            {
                // Nothing to do for stack-attached nodes.
                float oldTopDiameter = (f.name == nameof(topOuterDiameter)) ? prevDiam : topOuterDiameter;
                float oldBottomDiameter = (f.name == nameof(bottomOuterDiameter)) ? prevDiam : bottomOuterDiameter;
                foreach (Part p in part.children)
                {
                    if (p.FindAttachNodeByPart(part) is AttachNode node && node.nodeType == AttachNode.NodeType.Surface)
                    {
                        GetAttachmentNodeLocation(node, out Vector3 worldSpace, out Vector3 localToHere, out ShapeCoordinates coord);
                        float y_from_bottom = coord.y + (length / 2);
                        float oldDiameterAtY = Mathf.Lerp(oldBottomDiameter, oldTopDiameter, y_from_bottom / length);
                        float newDiameterAtY = Mathf.Lerp(bottomOuterDiameter, topOuterDiameter, y_from_bottom / length);
                        float ratio = newDiameterAtY / oldDiameterAtY;
                        coord.r *= ratio;
                        MovePartByAttachNode(node, coord);
                    }
                }
            }
        }

        public override void UpdateTechConstraints()
        {
            Fields[nameof(bottomInnerDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit bottomInnerDiameterEdit = Fields[nameof(bottomInnerDiameter)].uiControlEditor as UI_FloatEdit;
            bottomInnerDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            bottomInnerDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(bottomOuterDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit bottomOuterDiameterEdit = Fields[nameof(bottomOuterDiameter)].uiControlEditor as UI_FloatEdit;
            bottomOuterDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            bottomOuterDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(topInnerDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit topInnerDiameterEdit = Fields[nameof(topInnerDiameter)].uiControlEditor as UI_FloatEdit;
            topInnerDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            topInnerDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(topOuterDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit topOuterDiameterEdit = Fields[nameof(topOuterDiameter)].uiControlEditor as UI_FloatEdit;
            topOuterDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            topOuterDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(length)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit lengthEdit = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
            lengthEdit.incrementLarge = PPart.lengthLargeStep;
            lengthEdit.incrementSmall = PPart.lengthSmallStep;

            AdjustDimensionBounds();
            bottomInnerDiameter = Mathf.Clamp(bottomInnerDiameter, bottomInnerDiameterEdit.minValue, bottomInnerDiameterEdit.maxValue);
            bottomOuterDiameter = Mathf.Clamp(bottomOuterDiameter, bottomOuterDiameterEdit.minValue, bottomOuterDiameterEdit.maxValue);
            topInnerDiameter = Mathf.Clamp(topInnerDiameter, topInnerDiameterEdit.minValue, topInnerDiameterEdit.maxValue);
            topOuterDiameter = Mathf.Clamp(topOuterDiameter, topOuterDiameterEdit.minValue, topOuterDiameterEdit.maxValue);
            length = Mathf.Clamp(length, lengthEdit.minValue, lengthEdit.maxValue);
        }

        private Vector3 CoMOffset_internal()
        {
            //h * ((BO^2 + 2*BO*TO + 3*TO^2) - (BI^2 + 2*BI*TI + 3*TI^2)) / 4 * ((BO^2 + BO*TO + TO^2) - (BI^2 + BI*TI + TI^2))
            float num = (bottomOuterDiameter * bottomOuterDiameter + 2 * bottomOuterDiameter * topOuterDiameter + 3 * topOuterDiameter * topOuterDiameter)
                      - (bottomInnerDiameter * bottomInnerDiameter + 2 * bottomInnerDiameter * topInnerDiameter + 3 * topInnerDiameter * topInnerDiameter);
            float denom = (bottomOuterDiameter * bottomOuterDiameter + bottomOuterDiameter * topOuterDiameter + topOuterDiameter * topOuterDiameter)
                      - (bottomInnerDiameter * bottomInnerDiameter + bottomInnerDiameter * topInnerDiameter + topInnerDiameter * topInnerDiameter);
            Vector3 res = new Vector3(0, length * ((num / (4 * denom)) - 0.5f), 0);
            return res;
        }

        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", topOuterDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", bottomOuterDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }

        internal override void InitializeAttachmentNodes() => InitializeAttachmentNodes(length, (bottomOuterDiameter + topOuterDiameter) / 2);

        internal override void UpdateShape(bool force = true)
        {
            part.CoMOffset = CoMOffset;
            Volume = CalculateVolume();
            GenerateMeshes(bottomOuterDiameter / 2, bottomInnerDiameter / 2, topOuterDiameter / 2, topInnerDiameter / 2, length, numSides);

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
                node.size = Math.Min((int)(bottomInnerDiameter / PPart.diameterLargeStep), 3);
                node.breakingTorque = node.breakingForce = Mathf.Max(50 * node.size * node.size, 50);
                RaiseChangeAttachNodeSize(node, bottomInnerDiameter, Mathf.PI * bottomInnerDiameter * bottomInnerDiameter * 0.25f + Mathf.PI * length * (bottomInnerDiameter + bottomOuterDiameter));
            }
        }

        private void GenerateColliders()
        {
            PPart.ClearColliderHolder();
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
            GenerateColliderVertices(vertices, bottomOuterDiameter / 2, bottomInnerDiameter / 2, topOuterDiameter / 2, topInnerDiameter / 2, length);
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

        private void GenerateColliderVertices(Vector3[] vertices, float bottomOuterRadius, float bottomInnerRadius, float topOuterRadius, float topInnerRadius, float height)
        {
            float width = NormSideLength;

            for (int corner = 0; corner < 4; corner++)
            {
                float xLength = corner switch
                {
                    0 => topOuterRadius,
                    1 => bottomOuterRadius,
                    2 => bottomInnerRadius,
                    _ => topInnerRadius
                };

                xLength *= Mathf.Cos(CornerCenterCornerAngle / 2);
                float offsetY = ((corner == 0 | corner == 3) ? 1 : -1) * (height / 2);
                Vector3 outVector = Vector3.forward * xLength;
                Vector3 upVector = Vector3.up * offsetY;
                Vector3 sideVector = Vector3.right * width * xLength;

                vertices[2 * corner] = outVector + upVector + sideVector;
                vertices[2 * corner + 1] = outVector + upVector - sideVector;
            }
        }

        private void GenerateMeshes(float bottomOuterRadius, float bottomInnerRadius, float topOuterRadius, float topInnerRadius, float height, int nbSides)
        {
            float verticalpointDensity = 12f;
            int outsideVerticalPoints = (int)Math.Floor(Mathf.Abs(bottomOuterRadius - topOuterRadius) * verticalpointDensity) + 2;
            int insideVerticalPoints = (int)Math.Floor(Mathf.Abs(bottomInnerRadius - topInnerRadius) * verticalpointDensity) + 2;
            UncheckedMesh sideMesh = new UncheckedMesh((outsideVerticalPoints + insideVerticalPoints) * (nbSides + 1), (outsideVerticalPoints + insideVerticalPoints - 2) * 3 * (nbSides + 1));
            GenerateSideVertices(sideMesh, true, bottomOuterRadius, bottomInnerRadius, topOuterRadius, topInnerRadius, height, nbSides, outsideVerticalPoints - 1, 0);
            GenerateSideVertices(sideMesh, false, bottomOuterRadius, bottomInnerRadius, topOuterRadius, topInnerRadius, height, nbSides, insideVerticalPoints - 1, outsideVerticalPoints * (nbSides + 1));
            GenerateSideTriangles(sideMesh, true, nbSides, outsideVerticalPoints, 0, 0);
            GenerateSideTriangles(sideMesh, false, nbSides, insideVerticalPoints, outsideVerticalPoints * (nbSides + 1), (outsideVerticalPoints - 1) * 6 * (nbSides + 1));

            var tankULength = numSides * NormSideLength * (topOuterRadius + bottomOuterRadius) * 2;
            var tankVLength = length;

            RaiseChangeTextureScale("sides", PPart.legacyTextureHandler.SidesMaterial, new Vector2(tankULength, tankVLength));
            WriteToAppropriateMesh(sideMesh, PPart.SidesIconMesh, SidesMesh);

            UncheckedMesh capMesh = new UncheckedMesh(4 * (nbSides + 1), 4 * (nbSides + 1));
            GenerateCapVertices(capMesh, true, topOuterRadius, topInnerRadius, height, nbSides, 0);
            GenerateCapVertices(capMesh, false, bottomOuterRadius, bottomInnerRadius, height, nbSides, 2 * (nbSides + 1));
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

        private void GenerateSideVertices(UncheckedMesh mesh, bool outside, float bottomOuterRadius, float bottomInnerRadius, float topOuterRadius, float topInnerRadius, float height, int nbSides, int verticalPoints, int offset)
        {
            for (int side = 0; side <= nbSides; side++)
            {
                int currSide = side == nbSides ? 0 : side;

                // Angle around the part, offset to align texture with other parts orientation
                float t1 = ((float)currSide / nbSides + 0.25f) * 2f * Mathf.PI;

                for (int verticalPoint = 0; verticalPoint <= verticalPoints; verticalPoint++)
                {
                    float currFrac = (float)verticalPoint / verticalPoints;
                    float xLength = outside ? (topOuterRadius + currFrac * (bottomOuterRadius - topOuterRadius)) : (topInnerRadius + currFrac * (bottomInnerRadius - topInnerRadius));
                    Vector3 xVector = Quaternion.AngleAxis(-t1 * Mathf.Rad2Deg, Vector3.up) * new Vector3((outside ? 1:1), 0)*xLength;
                    Vector3 yVector = Vector3.up*(0.5f - currFrac)*height;

                    mesh.vertices[offset + ((verticalPoints + 1) * side + verticalPoint)] = xVector + yVector;
                    Vector3 normalInPlane = new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1)) * (outside ? 1 : -1);
                    Vector3 normal = (normalInPlane * height + Vector3.up * (outside ? (bottomOuterRadius - topOuterRadius) : (topInnerRadius-bottomInnerRadius))).normalized;
                    mesh.normals[offset + ((verticalPoints + 1) * side + verticalPoint)] = normal;
                    mesh.tangents[offset + ((verticalPoints + 1) * side + verticalPoint)] = new Vector4(-Mathf.Sin(t1), 0, Mathf.Cos(t1), (outside ? -1 : 1));
                    mesh.uv[offset + ((verticalPoints + 1) * side + verticalPoint)] = new Vector2((float)side / nbSides * (outside ? 1 : -1), 1 - currFrac);
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
            var target = (HighLogic.LoadedScene == GameScenes.LOADING) ? iconMesh : normalMesh;
            mesh.WriteTo(target);
        }
    }
}
