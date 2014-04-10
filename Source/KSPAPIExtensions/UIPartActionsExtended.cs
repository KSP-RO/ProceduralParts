using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using KSPAPIExtensions.PartMessage;
using KSPAPIExtensions.DebuggingUtils;

namespace KSPAPIExtensions
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    internal class UIPartActionsExtendedRegistrationAddon : MonoBehaviour
    {
        public void Start()
        {
            if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight)
                return;

            UIPartActionController controller = UIPartActionController.Instance;
            if (controller == null)
            {
                print("Controller instance is null");
                return;
            }

            if (controller.fieldPrefabs.Find(item => item.GetType().FullName == typeof(UIPartActionFloatEdit).FullName) != null)
                return;

            FieldInfo typesField = (from fld in controller.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                    where fld.FieldType == typeof(List<Type>)
                                    select fld).First();
            List<Type> fieldPrefabTypes = (List<Type>)typesField.GetValue(controller);

            // Register prefabs.
            controller.fieldPrefabs.Add(UIPartActionFloatEdit.CreateTemplate());
            fieldPrefabTypes.Add(typeof(UI_FloatEdit));

            controller.fieldPrefabs.Add(UIPartActionChooseOption.CreateTemplate());
            fieldPrefabTypes.Add(typeof(UI_ChooseOption));

            int idx = controller.fieldPrefabs.FindIndex(item => item.GetType() == typeof(UIPartActionLabel));
            controller.fieldPrefabs[idx] = UIPartActionLabelImproved.CreateTemplate((UIPartActionLabel)controller.fieldPrefabs[idx]);

            controller.resourceItemEditorPrefab = UIPartActionResourceEditorImproved.CreateTemplate(controller.resourceItemEditorPrefab);
        }
    }


    internal class UIPartActionResourceEditorImproved : UIPartActionResourceEditor, PartMessagePartProxy
    {
        public override void Setup(UIPartActionWindow window, Part part, UI_Scene scene, UI_Control control, PartResource resource)
        {
            double amount = resource.amount;
            base.Setup(window, part, scene, control, resource);
            this.resource.amount = amount;

            slider.SetValueChangedDelegate(OnSliderChanged);
        }

        private float oldSliderValue;

        public override void UpdateItem()
        {
            base.UpdateItem();

            SIPrefix prefix = (resource.maxAmount).GetSIPrefix();
            Func<double, string> Formatter = prefix.GetFormatter(resource.maxAmount, sigFigs: 4);

            resourceMax.Text = Formatter(resource.maxAmount) + " " + prefix.PrefixString();
            resourceAmnt.Text = Formatter(resource.amount);

            oldSliderValue = slider.Value = (float)(resource.amount / resource.maxAmount);
        }

        private void OnSliderChanged(IUIObject obj)
        {
            if (oldSliderValue == slider.Value)
                return;
            oldSliderValue = slider.Value;

            SIPrefix prefix = resource.maxAmount.GetSIPrefix();
            resource.amount = prefix.Round((double)slider.Value * this.resource.maxAmount, sigFigs:4);
            if (this.scene == UI_Scene.Editor)
                SetSymCounterpartsAmount(resource.amount);
        }

        public Part proxyPart
        {
            get { return resource.part; }
        }

        internal static UIPartActionResourceEditorImproved CreateTemplate(UIPartActionResourceEditor oldEditor)
        {
            GameObject editGo = (GameObject)Instantiate(oldEditor.gameObject);
            Destroy(editGo.GetComponent<UIPartActionResourceEditor>());
            UIPartActionResourceEditorImproved edit = editGo.AddComponent<UIPartActionResourceEditorImproved>();
            editGo.SetActive(false);
            edit.transform.parent = oldEditor.transform.parent;
            edit.transform.localPosition = oldEditor.transform.localPosition;

            // Find all the bits.
            edit.slider = editGo.transform.Find("Slider").GetComponent<UIProgressSlider>();
            edit.resourceAmnt = editGo.transform.Find("amnt").GetComponent<SpriteText>();
            edit.resourceName = editGo.transform.Find("name").GetComponent<SpriteText>();
            edit.resourceMax = editGo.transform.Find("total").GetComponent<SpriteText>();
            edit.flowBtn = editGo.transform.Find("StateBtn").GetComponent<UIStateToggleBtn>();

            return edit;
        }
    }


    internal class UIPartActionLabelImproved : UIPartActionLabel
    {
        private SpriteText label;
        private void Awake()
        {
            label = base.gameObject.GetComponentInChildren<SpriteText>();
        }

        public override void UpdateItem()
        {
            object target = isModule ? (object)partModule : part;

            Type fieldType = field.FieldInfo.FieldType;
            if (fieldType == typeof(double))
            {
                double value = (double)field.FieldInfo.GetValue(target);
                label.Text = (string.IsNullOrEmpty(field.guiName) ? field.name : field.guiName) + " " +
                    (string.IsNullOrEmpty(field.guiFormat) ? value.ToString() : value.ToStringExt(field.guiFormat))
                    + field.guiUnits;
            }
            if (fieldType == typeof(float))
            {
                float value = (float)field.FieldInfo.GetValue(target);
                label.Text = (string.IsNullOrEmpty(field.guiName) ? field.name : field.guiName) + " " +
                    (string.IsNullOrEmpty(field.guiFormat) ? value.ToString() : value.ToStringExt(field.guiFormat))
                    + field.guiUnits;
            }
            else
                label.Text = this.field.GuiString(target);
        }

        internal static UIPartActionLabelImproved CreateTemplate(UIPartActionLabel oldLabel)
        {
            GameObject labelGo = (GameObject)Instantiate(oldLabel.gameObject);
            Destroy(labelGo.GetComponent<UIPartActionLabel>());
            UIPartActionLabelImproved label = labelGo.AddComponent<UIPartActionLabelImproved>();
            labelGo.SetActive(false);
            label.transform.parent = oldLabel.transform.parent;
            label.transform.localPosition = oldLabel.transform.localPosition;
            
            return label;
        }
    }

}