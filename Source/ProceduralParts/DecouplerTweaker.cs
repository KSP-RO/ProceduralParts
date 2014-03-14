using KSPAPIExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public class DecouplerTweaker : PartModule
{

    [KSPField]
    public string separatorTechRequired;

    [KSPField]
    public float maxEjectionForce = float.PositiveInfinity;

    [KSPField]
    public float density = 0.0f;


    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Style:"),
     UI_Toggle(disabledText = "Decoupler", enabledText = "Separator")]
    public bool isOmniDecoupler = false;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Force", guiUnits = "N", guiFormat = "F0"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 5f, maxValue = float.PositiveInfinity, incrementLarge = 100f, incrementSlide = 5f)]
    public float ejectionForce = 200f;

    private ModuleDecouple decouple;

    public override void OnSave(ConfigNode node)
    {
        // Force saved value for enabled to be true.
        node.SetValue("isEnabled", "True");
    }

    public override void OnStart(PartModule.StartState state)
    {
        Fields["isOmniDecoupler"].guiActiveEditor =
            string.IsNullOrEmpty(separatorTechRequired) || ResearchAndDevelopment.GetTechnologyState(separatorTechRequired) == RDTech.State.Available;


        decouple = (ModuleDecouple)part.Modules["ModuleDecouple"];
        if(ejectionForce == 0)
            ejectionForce = decouple.ejectionForce;

        Update();

        isEnabled = enabled = HighLogic.LoadedSceneIsEditor;
    }

    public void Update()
    {
        decouple.ejectionForce = ejectionForce;
        decouple.isOmniDecoupler = isOmniDecoupler;
    }

    // Plugs into procedural parts.
    public void ChangeTextureScale(string name, Material material, Vector2 textureScale)
    {
        if (name != "bottom")
            return;

        UI_FloatEdit ejectionForceEdit = (UI_FloatEdit)Fields["ejectionForce"].uiControlEditor;
        float oldForceRatio = ejectionForce / ejectionForceEdit.maxValue;

        // There's no real scaling law to the stock decouplers
        float scale = textureScale.x;
        float maxForce = float.PositiveInfinity;
        if (scale <= 0.625f)
            maxForce = 25;
        else
        {
            float factor;
            if (scale <= 1.25f)
                factor = (scale - 0.625f) / 0.625f * (14f - 8f) + 8f;
            else if (scale <= 2.5f)
                factor = (scale - 1.25f) / 1.25f * (10f - 14f) + 14f;
            else
                factor = 10;

            maxForce = scale * factor;
            maxForce *= maxForce;
            maxForce = Mathf.Round(maxForce / 5f) * 5f;
        }

        ejectionForceEdit.maxValue = Math.Min(maxEjectionForce, maxForce);
        ejectionForce = Mathf.Round(maxForce * oldForceRatio * 5f) / 5f;
    }


    public void ChangeVolume(float volume)
    {
        if(density > 0)
            part.mass = density * volume;
    }
}
