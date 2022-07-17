using System;
using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{
    class ProceduralShapeHollowTruss : ProceduralAbstractShape
    {
        #region KSPFields
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

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Rods", guiUnits = "#", guiFormat = "F0", groupName = ProceduralPart.PAWGroupName),
            UI_FloatRange(minValue = 3, maxValue = 30, stepIncrement = 1, scene = UI_Scene.Editor)]
        public float nbRods = 12;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Tilt Angle", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = -180f, maxValue = 180f, incrementLarge = 10f, incrementSmall = 1f, incrementSlide = SliderPrecision, sigFigs = 3, unit = "°", useSI = true)]
        public float tiltAngle = 10f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Offset Angle", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementLarge = 10f, incrementSmall = 1f, incrementSlide = SliderPrecision, sigFigs = 3, unit = "°", useSI = true)]
        public float offsetAngle = 0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Symmetrical rods", groupName = ProceduralPart.PAWGroupName),
            UI_Toggle(disabledText = "Disabled", enabledText = "Enabled", scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.None)]
        public bool symmetryRods = true;

        [KSPField]
        public string TopNodeName = "top";

        [KSPField]
        public string BottomNodeName = "bottom";

        [KSPField]
        public float realLength = 0f;
        public float RealLength
        {
            get
            {
                Vector3 bottomPos = new Vector3(bottomDiameter/2, -length / 2, 0);
                Vector3 topPos = new Vector3(Mathf.Cos(tiltAngle * Mathf.Deg2Rad) * topDiameter/2, length / 2, Mathf.Sin(tiltAngle * Mathf.Deg2Rad) * topDiameter/2);
                Vector3 rodDirection = topPos - bottomPos;
                realLength = rodDirection.magnitude;
                return realLength;
            }
        }

        const float maxMeshBendError = 0.02f;
        #endregion

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
                Fields[nameof(offsetAngle)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(symmetryRods)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;

                Fields[nameof(nbRods)].uiControlEditor.onFieldChanged += ClampOffset;
                Fields[nameof(nbRods)].uiControlEditor.onSymmetryFieldChanged += ClampOffset;
            }
        }

        // Known issue; causes the nbRods slider to get dropped in the UI when hitting the offset limit
        private void ClampOffset(BaseField f, object obj)
        {
            float oldOffset = offsetAngle;
            offsetAngle = Mathf.Clamp(offsetAngle, -180f/nbRods, 180f/nbRods);
            if (offsetAngle != oldOffset)
                MonoUtilities.RefreshPartContextWindow(part);
        }

        public override void AdjustDimensionBounds()
        {
            float maxLength = PPart.lengthMax;
            float minLength = 0f;
            float maxBottomDiameter = PPart.diameterMax;
            float maxTopDiameter = PPart.diameterMax;
            float minBottomDiameter = PPart.diameterMin;
            float minTopDiameter = PPart.diameterMin;
            float maxRodRadius = PPart.diameterMax;
            float minRodRadius = PPart.diameterMin;

            // If realLength is too small, update the length to prevent too small size
            if (RealLength < PPart.lengthMin)
            {
                minLength = PPart.lengthMin;
                length = minLength;
                MonoUtilities.RefreshPartContextWindow(part);
            }
            // Vary the rod diameter to stay within min and max volume, given length
            if (PPart.volumeMax < float.PositiveInfinity)
                maxRodRadius = Mathf.Sqrt(PPart.volumeMax/(nbRods * RealLength * Mathf.PI));
            if (PPart.volumeMin > 0)
                minRodRadius = Mathf.Sqrt(PPart.volumeMin/(nbRods * RealLength * Mathf.PI));

            maxBottomDiameter = Mathf.Clamp(maxBottomDiameter, PPart.diameterMin, PPart.diameterMax);
            maxTopDiameter = Mathf.Clamp(maxTopDiameter, PPart.diameterMin, PPart.diameterMax);
            float absOffset = 180f/nbRods;

            (Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxBottomDiameter;
            (Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit).minValue = minBottomDiameter;
            (Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxTopDiameter;
            (Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit).minValue = minTopDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).maxValue = maxLength;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).minValue = minLength;
            (Fields[nameof(rodDiameter)].uiControlEditor as UI_FloatEdit).minValue = minRodRadius;
            (Fields[nameof(rodDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxRodRadius;
            (Fields[nameof(offsetAngle)].uiControlEditor as UI_FloatEdit).minValue = -absOffset;
            (Fields[nameof(offsetAngle)].uiControlEditor as UI_FloatEdit).maxValue = absOffset;

        }

        public override float CalculateVolume()
        {
            return Mathf.PI * rodDiameter * rodDiameter / 4 * RealLength * nbRods;
        }

        public override bool SeekVolume(float targetVolume, int dir) => SeekVolume(targetVolume, Fields[nameof(length)], dir);

        public override void TranslateAttachmentsAndNodes(BaseField f, object obj)
        {
            if (f.name == nameof(bottomDiameter) && obj is float oldBottomDiameter)
            {
                HandleDiameterChange((bottomDiameter + topDiameter) / 2, (oldBottomDiameter + topDiameter) / 2);
            }
            if (f.name == nameof(topDiameter) && obj is float oldTopDiameter)
            {
                HandleDiameterChange((topDiameter + bottomDiameter) / 2, (oldTopDiameter + bottomDiameter) / 2);
            }
            if (f.name == nameof(length) && obj is float oldLength)
            {
                HandleLengthChange(length, oldLength);
            }
        }

        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r /= (bottomDiameter + topDiameter)/4;
            coords.y /= length;
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r *= (bottomDiameter + topDiameter)/4;
            coords.y *= length;
        }

        public override void UpdateTechConstraints()
        {
            Fields[nameof(topDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit topDiameterEdit = Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit;
            topDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            topDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(bottomDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit bottomDiameterEdit = Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit;
            bottomDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            bottomDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(length)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit lengthEdit = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
            lengthEdit.incrementLarge = PPart.lengthLargeStep;
            lengthEdit.incrementSmall = PPart.lengthSmallStep;

            Fields[nameof(rodDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit rodEdit = Fields[nameof(rodDiameter)].uiControlEditor as UI_FloatEdit;
            rodEdit.incrementLarge = PPart.lengthLargeStep;
            rodEdit.incrementSmall = PPart.lengthSmallStep;

            AdjustDimensionBounds();
            topDiameter = Mathf.Clamp(topDiameter, topDiameterEdit.minValue, topDiameterEdit.maxValue);
            bottomDiameter = Mathf.Clamp(bottomDiameter, bottomDiameterEdit.minValue, bottomDiameterEdit.maxValue);
            length = Mathf.Clamp(length, lengthEdit.minValue, lengthEdit.maxValue);
            rodDiameter = Mathf.Clamp(rodDiameter, rodEdit.minValue, rodEdit.maxValue);
        }
        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", topDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", bottomDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }

        internal override void InitializeAttachmentNodes() => InitializeAttachmentNodes(length, (bottomDiameter + topDiameter)/2f);

        internal override void UpdateShape(bool force = true)
        {
            part.CoMOffset = CoMOffset;
            Volume = CalculateVolume();
            GenerateMeshes(bottomDiameter / 2, topDiameter / 2, length, rodDiameter / 2, (int)nbRods, tiltAngle * Mathf.Deg2Rad, offsetAngle * Mathf.Deg2Rad, symmetryRods);

            // WriteMeshes in AbstractSoRShape typically does UpdateNodeSize, UpdateProps, RaiseModelAndColliderChanged
            UpdateNodeSize(TopNodeName, topDiameter);
            UpdateNodeSize(BottomNodeName, bottomDiameter);
            PPart.UpdateProps();
            RaiseModelAndColliderChanged();
        }

        private void UpdateNodeSize(string nodeName, float nodeDiameter)
        {
            if (part.attachNodes.Find(n => n.id == nodeName) is AttachNode node)
            {
                node.size = Math.Min((int)(nodeDiameter / PPart.diameterLargeStep), 3);
                node.breakingTorque = node.breakingForce = Mathf.Max(50 * node.size * node.size, 50);
                RaiseChangeAttachNodeSize(node, nodeDiameter, Mathf.PI * nodeDiameter * nodeDiameter * 0.25f);
            }
        }

        #region meshes
        public void GenerateMeshes(float bottomRadius, float topRadius, float height, float rodRadius, int nbRods, float tiltAngle, float offsetAngle, bool both)
        {
            int nbRodSides = (int)Mathf.Max(Mathf.PI / Mathf.Acos(1 - maxMeshBendError / Mathf.Max(2 * rodRadius, maxMeshBendError)), 2) * 2;
            float CornerCenterCornerAngle = 2 * Mathf.PI / nbRodSides;
            float NormSideLength = Mathf.Tan(CornerCenterCornerAngle / 2);
            int vertPerRod = (nbRodSides + 1) * 2;
            int nVert = vertPerRod * nbRods * (both ? 2 : 1);
            int triPerRod = (nbRodSides) * 3 * 2;
            int nTri = triPerRod * nbRods * (both ? 2 : 1);

            UncheckedMesh uSideMesh = new UncheckedMesh(nVert, nTri);
            GenerateAllSideVertices(uSideMesh, bottomRadius, topRadius, rodRadius, height, tiltAngle, offsetAngle, nbRods, nbRodSides, 0);
            GenerateAllSideTriangles(uSideMesh, nbRods, nbRodSides, 0, 0);
            if (both)
            {
                GenerateAllSideVertices(uSideMesh, bottomRadius, topRadius, rodRadius, height, -tiltAngle, -offsetAngle, nbRods, nbRodSides, nVert / 2);
                GenerateAllSideTriangles(uSideMesh, nbRods, nbRodSides, nVert / 2, nTri / 2);
            }
            float tankULength = nbRodSides * NormSideLength * rodRadius * 2;
            float tankVLength = RealLength;

            RaiseChangeTextureScale("sides", PPart.legacyTextureHandler.SidesMaterial, new Vector2(tankULength, tankVLength));
            WriteToAppropriateMesh(uSideMesh, PPart.SidesIconMesh, SidesMesh);

            int nVertCap = 2 * nbRodSides * nbRods * 2 * (both ? 2 : 1);
            int nTriCap = 2 * 3 * nbRodSides * nbRods * 2 * (both ? 2 : 1);
            UncheckedMesh capMesh = new UncheckedMesh(nVertCap, nTriCap);
            GenerateAllCapVertices(capMesh, bottomRadius, topRadius, rodRadius, height, tiltAngle, offsetAngle, nbRods, nbRodSides, 0);
            GenerateAllCapTriangles(capMesh, nbRods, nbRodSides, 0, 0);
            if (both)
            {
                GenerateAllCapVertices(capMesh, bottomRadius, topRadius, rodRadius, height, -tiltAngle, -offsetAngle, nbRods, nbRodSides, nVertCap / 2);
                GenerateAllCapTriangles(capMesh, nbRods, nbRodSides, nVertCap / 2, nTriCap / 2);
            }
            WriteToAppropriateMesh(capMesh, PPart.EndsIconMesh, EndsMesh);

            GenerateColliders(bottomRadius, topRadius, height, rodRadius, nbRods, tiltAngle, offsetAngle, both, nbRodSides);
        }

        private void GenerateColliders(float bottomRadius, float topRadius, float height, float rodRadius, int nbRods, float tiltAngle, float offsetAngle, bool both, int nbRodSides)
        {
            PPart.ClearColliderHolder();
            GenerateCollidersOneWay(tiltAngle, offsetAngle, nbRods);
            if (both)
                GenerateCollidersOneWay(-tiltAngle, -offsetAngle, nbRods);

            void GenerateCollidersOneWay(float tiltAngle, float offsetAngle, int startColliderIndex)
            {
                for (int i = 0; i < nbRods; i++)
                {
                    var go = new GameObject($"Mesh_Collider_{i + startColliderIndex}");
                    var coll = go.AddComponent<MeshCollider>();
                    go.transform.SetParent(PPart.ColliderHolder.transform, false);
                    coll.convex = true;
                    coll.sharedMesh = GenerateColliderMesh(0, bottomRadius, topRadius, height, rodRadius, tiltAngle, nbRodSides);
                    var orientation = Quaternion.AngleAxis((360f * i / nbRods) - offsetAngle * Mathf.Rad2Deg, Vector3.up);
                    go.transform.localRotation *= orientation;
                }
            }
        }

        private static Mesh GenerateColliderMesh(float angle, float bottomRadius, float topRadius, float height, float rodRadius, float tiltAngle, int nbSides)
        {
            int nTriSide = nbSides * 6;
            int nTriCaps = 2 * 3 * nbSides * 2;
            UncheckedMesh colliderMesh = new UncheckedMesh(2 * (nbSides + 1), nTriSide + nTriCaps);
            GenerateSideVertices(colliderMesh, angle, bottomRadius, topRadius, height, rodRadius, tiltAngle, nbSides, 0);
            GenerateSideTriangles(colliderMesh, nbSides, 0, 0);
            GenerateCapTriangles(colliderMesh, nbSides, 0, nTriSide);

            Mesh mesh = new Mesh();
            mesh.vertices = colliderMesh.vertices;
            mesh.triangles = colliderMesh.triangles;
            return mesh;
        }

        private static void GenerateAllCapTriangles(UncheckedMesh capMesh, int nbRods, int nbRodSides, int vertOffset, int triOffset)
        {
            for (int rodNumber = 0; rodNumber < nbRods; rodNumber++)
            {
                int vertexOffset = 2 * rodNumber * (nbRodSides + 1);
                int triangleOffset = 3 * 2 * rodNumber * (nbRodSides - 2);
                GenerateCapTriangles(capMesh, nbRodSides, vertexOffset + vertOffset, triangleOffset + triOffset);
            }
        }

        private static void GenerateAllCapVertices(UncheckedMesh capMesh, float bottomRadius, float topRadius, float rodRadius, float height, float tiltAngle, float offsetAngle, int nbRods, int nbRodSides, int offset)
        {
            for (int rodNumber = 0; rodNumber < nbRods; rodNumber++)
            {
                int indexOffset = 2 * rodNumber * (nbRodSides + 1);
                float t1 = (float)rodNumber / nbRods * 2 * Mathf.PI + offsetAngle;
                GenerateCapVertices(capMesh, t1, bottomRadius, topRadius, height, rodRadius, tiltAngle, nbRodSides, indexOffset + offset);
            }
        }

        private static void GenerateAllSideTriangles(UncheckedMesh sideMesh, float nbRods, int nbRodSides, int vertOffset, int triOffset)
        {
            for (int rodNumber = 0; rodNumber < nbRods; rodNumber++)
            {
                int vertexOffset = 2 * rodNumber * (nbRodSides + 1);
                int triangleOffset = 3 * 2 * rodNumber * nbRodSides;
                GenerateSideTriangles(sideMesh, nbRodSides, vertexOffset + vertOffset, triangleOffset + triOffset);
            }
        }

        private static void GenerateAllSideVertices(UncheckedMesh sideMesh, float bottomRadius, float topRadius, float rodRadius, float height, float tiltAngle, float offsetAngle, float nbRods, int nbRodSides, int offset)
        {
            for (int rodNumber = 0; rodNumber < nbRods; rodNumber++)
            {
                int indexOffset = 2 * rodNumber * (nbRodSides + 1);
                float t1 = (float)rodNumber / nbRods * 2 * Mathf.PI + offsetAngle;
                GenerateSideVertices(sideMesh, t1, bottomRadius, topRadius, height, rodRadius, tiltAngle, nbRodSides, indexOffset + offset);
            }
        }

        private static void GenerateCapTriangles(UncheckedMesh mesh, int nbSides, int vertexOffset, int triangleOffset)
        {
            int i = 0;
            for (int side = 0; side < nbSides - 2; side++)
            {
                mesh.triangles[triangleOffset + i++] = vertexOffset;
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 2;
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 4;

                mesh.triangles[triangleOffset + i++] = vertexOffset + 1;
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 5;
                mesh.triangles[triangleOffset + i++] = vertexOffset + side * 2 + 3;
            }
        }

        private static void GenerateCapVertices(UncheckedMesh mesh, float angle, float bottomRadius, float topRadius, float height, float rodRadius, float tiltAngle, int nbSides, int offset)
        {
            Vector3 bottomPos = new Vector3(Mathf.Cos(angle) * bottomRadius, -height / 2, Mathf.Sin(angle) * bottomRadius);
            Vector3 topPos = new Vector3(Mathf.Cos(angle + tiltAngle) * topRadius, height / 2, Mathf.Sin(angle + tiltAngle) * topRadius);
            Vector3 rodDirection = topPos - bottomPos;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, rodDirection);
            for (int side = 0; side < nbSides; side++)
            {
                int currSide = side == nbSides ? 0 : side;

                float t1 = ((float)currSide / nbSides + 0.25f) * 2f * Mathf.PI + angle;
                Vector3 r1 = new Vector3(Mathf.Cos(t1) * rodRadius, 0f, Mathf.Sin(t1) * rodRadius);

                for (int profilePoint = 0; profilePoint < 2; profilePoint++)
                {
                    int idx = offset + 2 * side + profilePoint;
                    Vector3 yVector = Vector3.up * (profilePoint) * rodDirection.magnitude;
                    mesh.vertices[idx] = bottomPos + rotation.normalized * (r1 + yVector);
                    mesh.normals[idx] = rotation * new Vector3(0f, (profilePoint - 0.5f) * 2, 0f);
                    mesh.tangents[idx] = rotation * new Vector4(-Mathf.Sin(t1), 0, Mathf.Cos(t1), 1f);
                    mesh.uv[idx] = new Vector2(Mathf.Cos(t1) * (profilePoint - 0.5f) * 2, Mathf.Sin(t1)) / 2 + new Vector2(0.5f, 0.5f);
                }
            }
        }

        // Generate a single rod
        private static void GenerateSideVertices(UncheckedMesh mesh, float angle, float bottomRadius, float topRadius, float height, float rodRadius, float tiltAngle, int nbSides, int offset)
        {
            Vector3 bottomPos = new Vector3(Mathf.Cos(angle) * bottomRadius, -height / 2, Mathf.Sin(angle) * bottomRadius);
            Vector3 topPos = new Vector3(Mathf.Cos(angle+tiltAngle) * topRadius, height / 2, Mathf.Sin(angle+tiltAngle) * topRadius);
            Vector3 rodDirection = topPos - bottomPos;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, rodDirection);
            for (int side = 0; side <= nbSides; side++)
            {
                int currSide = side == nbSides ? 0 : side;

                float t1 = ((float)currSide / nbSides + 0.25f) * 2f * Mathf.PI + angle;
                Vector3 r1 = new Vector3(Mathf.Cos(t1) * rodRadius, 0f, Mathf.Sin(t1) * rodRadius);

                for (int profilePoint = 0; profilePoint < 2; profilePoint++)
                {
                    int idx = offset + 2 * side + profilePoint;
                    Vector3 yVector = Vector3.up * (profilePoint) * rodDirection.magnitude;
                    mesh.vertices[idx] = bottomPos + rotation.normalized * (r1 + yVector);
                    mesh.normals[idx] = rotation * new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1));
                    mesh.tangents[idx] = rotation * new Vector4(-Mathf.Sin(t1), 0, Mathf.Cos(t1), -1f);
                    mesh.uv[idx] = new Vector2((float)side / (nbSides), profilePoint);
                }
            }
        }

        private static void GenerateSideTriangles(UncheckedMesh mesh, int nbSides, int vertexOffset, int triangleOffset)
        {
            int i = 0;
            for (int side = 0; side < nbSides; side++)
            {
                int current = side * 2;
                int next = (side + 1) * 2;

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
            mesh.WriteTo((HighLogic.LoadedScene == GameScenes.LOADING) ? iconMesh : normalMesh);
        }
        #endregion
    }
}
