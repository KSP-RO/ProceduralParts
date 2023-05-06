using System;
using System.Linq;
using UnityEngine;

namespace ProceduralTools
{
    public class DragCubeTool : MonoBehaviour
    {
        public Part part;

        public static DragCubeTool UpdateDragCubes(Part p)
        {
            var tool = p.GetComponent<DragCubeTool>();
            if (tool == null)
            {
                tool = p.gameObject.AddComponent<DragCubeTool>();
                tool.part = p;
            }
            return tool;
        }

        public static void UpdateDragCubesImmediate(Part p)
        {
            if (!Ready(p))
                throw new InvalidOperationException("Not ready for drag cube rendering yet");
             
            UpdateCubes(p);
        }

        public void FixedUpdate()
        {
            if (Ready())
                UpdateCubes();
        }

        public bool Ready() => Ready(part);

        private static bool Ready(Part p)
        {
            if (HighLogic.LoadedSceneIsFlight)
                return FlightGlobals.ready; //&& !part.packed && part.vessel.loaded;
            if (HighLogic.LoadedSceneIsEditor)
                return p.localRoot == EditorLogic.RootPart && p.gameObject.layer != LayerMask.NameToLayer("TransparentFX");
            return true;
        }

        private void UpdateCubes()
        {
            UpdateCubes(part);
            Destroy(this);
        }

        private static void UpdateCubes(Part p)
        {
            if (FARinstalled)
                p.SendMessage("GeometryPartModuleRebuildMeshData");
            DragCube dragCube = DragCubeSystem.Instance.RenderProceduralDragCube(p);
            p.DragCubes.ClearCubes();
            p.DragCubes.Cubes.Add(dragCube);
            p.DragCubes.ResetCubeWeights();
            p.DragCubes.ForceUpdate(true, true, false);
            p.DragCubes.SetDragWeights();
        }

        private static bool? _farInstalled;
        public static bool FARinstalled
        {
            get
            {
                if (!_farInstalled.HasValue)
                {
                    _farInstalled = AssemblyLoader.loadedAssemblies.Any(a => a.assembly.GetName().Name == "FerramAerospaceResearch");
                }
                return _farInstalled.Value;
            }
        }
    }
}
