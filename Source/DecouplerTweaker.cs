using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ProceduralParts
{

    /// <summary>
    /// Module to allow tweaking of decouplers in the VAB.
    /// 
    /// This fairly flexible module allows tech dependent tweaking of the type and size of modules.
    /// There are options for it to target just one specific module, or all the modules on a part.
    /// </summary>
    public class DecouplerTweaker : PartModule
    {
        public override void OnAwake()
        {
            base.OnAwake();
            PartMessageService.Register(this);
        }

        /// <summary>
        /// In career mode, if this tech is not available then the option to have separators is not present
        /// </summary>
        [KSPField]
        public string separatorTechRequired;

        /// <summary>
        /// Maximum ejection force available. 
        /// </summary>
        [KSPField]
        public float maxEjectionForce = float.PositiveInfinity;

        /// <summary>
        /// Target a specific decoupler module only, the one attached to this node. Leave unspecified by default.
        /// </summary>
        [KSPField]
        public string explosiveNodeID;

        /// <summary>
        /// Allow targeting of all ModuleDecouple modules attached to a part, useful for symmetrical separators.
        /// </summary>
        [KSPField]
        public bool multipleTargets = false;


        /// <summary>
        /// Density of the decoupler. This is for use with Procedural Parts. Listens for ChangeVolume message
        /// </summary>
        [KSPField]
        public float density = 0.0f;

        /// <summary>
        /// Listen for a specific texture message, again for use with proceedural parts. If set, then listens for ChangeAttachNodeSize message.
        /// </summary>
        [KSPField]
        public string textureMessageName;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Style:"),
         UI_Toggle(disabledText = "Decoupler", enabledText = "Separator")]
        public bool isOmniDecoupler = false;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Force", guiUnits = "N", guiFormat = "F0"),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 5f, maxValue = float.PositiveInfinity, incrementLarge = 100f, incrementSlide = 5f)]
        public float ejectionForce = 200f;

        [KSPField(isPersistant = true)]
        public float mass;

        private ModuleDecouple decouple;

        public override void OnSave(ConfigNode node)
        {
            // Force saved value for enabled to be true.
            node.SetValue("isEnabled", "True");
        }

        public override void OnStart(PartModule.StartState state)
        {
            if (mass != 0)
                part.mass = mass;

            FindDecoupler();

            if (decouple == null)
            {
                Debug.LogError("Unable to find any decoupler modules");
                isEnabled = enabled = false;
                return;
            }

            Fields["isOmniDecoupler"].guiActiveEditor =
                string.IsNullOrEmpty(separatorTechRequired) || ResearchAndDevelopment.GetTechnologyState(separatorTechRequired) == RDTech.State.Available;

            if (ejectionForce == 0)
                ejectionForce = decouple.ejectionForce;

            OnUpdate();

            isEnabled = enabled = HighLogic.LoadedSceneIsEditor;
        }

        private void FindDecoupler()
        {
            if (explosiveNodeID != null)
            {
                for (int i = 0; i < part.Modules.Count; ++i)
                {
                    PartModule module = part.Modules[i];
                    if (module is ModuleDecouple && (decouple = (ModuleDecouple)module).explosiveNodeID == explosiveNodeID)
                        break;
                }
            }
            else
                decouple = part.Modules["ModuleDecouple"] as ModuleDecouple;
        }

        public override void OnUpdate()
        {
            if (!multipleTargets)
            {
                if (decouple == null)
                    FindDecoupler();
                decouple.ejectionForce = ejectionForce;
                decouple.isOmniDecoupler = isOmniDecoupler;
            }
            else
                foreach (PartModule m in part.Modules)
                    if (m is ModuleDecouple)
                    {
                        ModuleDecouple decouple = (ModuleDecouple)m;
                        decouple.ejectionForce = ejectionForce;
                        decouple.isOmniDecoupler = isOmniDecoupler;
                    }
        }

        // Plugs into procedural parts.
        // I may well change this message to something else in the fullness of time.
        [PartMessageListener(typeof(ChangeAttachNodeSizeDelegate), scenes:GameSceneFilter.AnyEditor)]
        private void ChangeAttachNodeSize(string name, float minDia, float area, int size)
        {
            if (name != textureMessageName)
                return;

            UI_FloatEdit ejectionForceEdit = (UI_FloatEdit)Fields["ejectionForce"].uiControlEditor;
            float oldForceRatio = ejectionForce / ejectionForceEdit.maxValue;

            // TODO: change this to a float curve implmentation and make it configurable.
            const float area1 = 0.625f * 0.625f * Mathf.PI, force1 = 25;
            const float area2 = 1.25f * 1.25f * Mathf.PI, force2 = 300;
            const float area3 = 2.5f * 2.5f * Mathf.PI, force3 = 800;

            // There's no real scaling law to the stock decouplers, will have maxima dependent on area so that it roughly fits
            // the stock parts.
            float maxForce = float.PositiveInfinity;
            if (area <= area1)
                maxForce = force1;
            else if (area <= area2)
                maxForce = Mathf.Lerp(force1, force2, Mathf.InverseLerp(area1, area2, area));
            else if (area <= area3)
                maxForce = Mathf.Lerp(force2, force3, Mathf.InverseLerp(area2, area3, area));
            else
                maxForce = area / area3 * force3;

            maxForce = Mathf.Round(maxForce / 5f) * 5f;
            maxForce = Mathf.Min(maxForce, this.maxEjectionForce);

            ejectionForceEdit.maxValue = maxForce;
            ejectionForce = Mathf.Round(maxForce * oldForceRatio * 5f) / 5f;
        }

        [PartMessageListener(typeof(ChangePartVolumeDelegate), scenes:GameSceneFilter.AnyEditor)]
        private void ChangeVolume(float volume)
        {
            if (density > 0)
                part.mass = mass = density * volume;
        }
    }

}