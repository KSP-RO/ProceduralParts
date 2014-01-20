using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public class StretchyCylinderTank : AbstractStretchyTank
{

    [KSPField]
    public string tankModelName = "stretchyTank";

    [KSPField]
    public string sidesName = "sides";

    [KSPField]
    public string endsName = "ends";

    #region callbacks and initialization

    public override void OnStart(PartModule.StartState state)
    {
        print("*ST* On Start");
        try
        {
            UIPartActionFloatEdit.RegisterTemplate();
        }
        catch (Exception ex)
        {
            print(ex.ToString());
        }

        tankModel = part.FindModelTransform(tankModelName);
        
        Transform sides = part.FindModelTransform(sidesName);
        Transform ends = part.FindModelTransform(endsName);

        sidesMaterial = sides.renderer.material;
        endsMaterial = ends.renderer.material;
 
        base.OnStart(state);
    }

    public override void OnInitialize()
    {
        base.OnInitialize();
    }

    public override void Update()
    {
        updateVolume();
        base.Update();
    }

    public override void GetMaterialsAndScale(out Material sidesMaterial, out Material endsMaterial, out Vector2 sideScale)
    {
        Renderer renderer = tankModel.GetComponentInChildren<Renderer>();

        sidesMaterial = this.sidesMaterial;
        endsMaterial = this.endsMaterial;
        sideScale.x = ((float)Math.PI) * diameter;
        sideScale.y = length;
    }

    public override object addTankAttachment(Transform attach)
    {
        attach.parent = tankModel.transform;
        return attach;
    }

    public override void removeTankAttachment(object data)
    {
        ((Transform)data).parent = null;
    }

    private Transform tankModel;
    private Material sidesMaterial;
    private Material endsMaterial;

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

    private void updateVolume()
    {
        if (diameter == oldDiameter && length == oldLength)
            return;

        tankVolume = length * diameter * diameter * (float)(Math.PI / 4.0d);

        // TODO: rebuild the mesh rather than just transforming it.
        tankModel.localScale = new Vector3(diameter, length, diameter);

        oldDiameter = diameter;
        oldLength = length;

        foreach (Part sym in part.symmetryCounterparts)
        {
            StretchyCylinderTank counterpart = sym.Modules.OfType<StretchyCylinderTank>().FirstOrDefault();
            counterpart.diameter = diameter;
            counterpart.length = length;
        }
    }

    #endregion
}


