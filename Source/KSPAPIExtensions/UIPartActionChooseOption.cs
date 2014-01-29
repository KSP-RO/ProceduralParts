using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KSPAPIExtensions
{
    [UI_ChooseOption]
    public class UIPartActionChooseOption : UIPartActionFieldItem
    {
        public SpriteText fieldName;
        public UIButton incDown;
        public UIButton incUp;
        public UIProgressSlider slider;

        private int selectedIdx = -1;
        private string selectedValue;

        public static UIPartActionChooseOption CreateTemplate()
        {
            // Create the control
            GameObject editGo = new GameObject("UIPartActionChooseOption", typeof(UIPartActionChooseOption));
            UIPartActionChooseOption edit = editGo.GetComponent<UIPartActionChooseOption>();
            editGo.SetActive(false);

            // TODO: since I don't have access to EZE GUI, I'm copying out bits from other existing GUIs 
            // if someone does have access, they could do this better although really it works pretty well.
            UIPartActionButton evtp = UIPartActionController.Instance.eventItemPrefab;
            GameObject srcTextGo = evtp.transform.Find("Text").gameObject;
            GameObject srcBackgroundGo = evtp.transform.Find("Background").gameObject;
            GameObject srcButtonGo = evtp.transform.Find("Btn").gameObject;

            UIPartActionFloatRange paFlt = (UIPartActionFloatRange)UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionFloatRange));
            GameObject srcSliderGo = paFlt.transform.Find("Slider").gameObject;


            // Start building our control
            GameObject backgroundGo = (GameObject)Instantiate(srcBackgroundGo);
            backgroundGo.transform.parent = editGo.transform;

            GameObject sliderGo = (GameObject)Instantiate(srcSliderGo);
            sliderGo.transform.parent = editGo.transform;
            sliderGo.transform.localScale = new Vector3(0.81f, 1, 1);
            edit.slider = sliderGo.GetComponent<UIProgressSlider>();
            edit.slider.ignoreDefault = true;

            GameObject fieldNameGo = (GameObject)Instantiate(srcTextGo);
            fieldNameGo.transform.parent = editGo.transform;
            fieldNameGo.transform.localPosition = new Vector3(24, -8, 0);
            edit.fieldName = fieldNameGo.GetComponent<SpriteText>();

            GameObject incDownGo = (GameObject)Instantiate(srcButtonGo);
            incDownGo.transform.parent = edit.transform;
            incDownGo.transform.localScale = new Vector3(0.45f, 1.1f, 1f);
            incDownGo.transform.localPosition = new Vector3(11.5f, -9, 0); //>11
            edit.incDown = incDownGo.GetComponent<UIButton>();

            GameObject incDownLabelGo = (GameObject)Instantiate(srcTextGo);
            incDownLabelGo.transform.parent = editGo.transform;
            incDownLabelGo.transform.localPosition = new Vector3(5.5f, -7, 0); // <6
            SpriteText incDownLabel = incDownLabelGo.GetComponent<SpriteText>();
            incDownLabel.Text = "<<";

            GameObject incUpGo = (GameObject)Instantiate(srcButtonGo);
            incUpGo.transform.parent = edit.transform;
            incUpGo.transform.localScale = new Vector3(0.45f, 1.1f, 1f);
            incUpGo.transform.localPosition = new Vector3(187.5f, -9, 0); // >187
            edit.incUp = incUpGo.GetComponent<UIButton>();

            GameObject incUpLabelGo = (GameObject)Instantiate(srcTextGo);
            incUpLabelGo.transform.parent = editGo.transform;
            incUpLabelGo.transform.localPosition = new Vector3(181.5f, -7, 0); //<182
            SpriteText incUpLabel = incUpLabelGo.GetComponent<SpriteText>();
            incUpLabel.Text = ">>";

            return edit;
        }

        protected UI_ChooseOption fieldInfo
        {
            get
            {
                return (UI_ChooseOption)control;
            }
        }

        public override void Setup(UIPartActionWindow window, Part part, PartModule partModule, UI_Scene scene, UI_Control control, BaseField field)
        {
            base.Setup(window, part, partModule, scene, control, field);
            incDown.SetValueChangedDelegate(obj => IncrementValue(false));
            incUp.SetValueChangedDelegate(obj => IncrementValue(true));
            slider.SetValueChangedDelegate(OnValueChanged);
        }

        private void IncrementValue(bool up)
        {
            if (fieldInfo.options == null || fieldInfo.options.Length == 0)
                selectedIdx = -1;
            else
                selectedIdx = (selectedIdx + fieldInfo.options.Length + (up ? 1 : -1)) % fieldInfo.options.Length;
            SetValueFromIdx();
        }

        private void OnValueChanged(IUIObject obj)
        {
            if (fieldInfo.options == null || fieldInfo.options.Length == 0)
                selectedIdx = -1;
            else
                selectedIdx = Mathf.RoundToInt(slider.Value * (fieldInfo.options.Length - 1));
            SetValueFromIdx();
        }

        private void SetValueFromIdx()
        {
            if (selectedIdx >= 0)
            {
                selectedValue = fieldInfo.options[selectedIdx];
                field.SetValue(selectedValue, field.host);
            }
            UpdateControls();
        }

        private void UpdateControls()
        {
            if (selectedIdx < 0)
            {
                fieldName.Text = "**Not Found**";
                slider.Value = 0;
                return;
            }

            if (fieldInfo.display != null && fieldInfo.display.Length > selectedIdx)
            {
                fieldName.Text = field.guiName + ": " + fieldInfo.display[selectedIdx];
            }
            else
            {
                fieldName.Text = field.guiName + ": " + fieldInfo.options[selectedIdx];
            }
            slider.Value = (float)selectedIdx / (float)(fieldInfo.options.Length - 1);
        }

        private string GetFieldValue()
        {
            return field.GetValue<string>(field.host);
        }

        bool exceptionPrinted = false;
        public override void UpdateItem()
        {
            try
            {
                string newSelectedValue = GetFieldValue();
                if (newSelectedValue == selectedValue)
                    return;

                selectedIdx = -1;
                selectedValue = null;
                if (fieldInfo.options != null)
                    for (int i = 0; i < fieldInfo.options.Length; ++i)
                        if (newSelectedValue == fieldInfo.options[i])
                        {
                            selectedIdx = i;
                            selectedValue = newSelectedValue;
                            break;
                        }
                UpdateControls();
                exceptionPrinted = false;
            }
            catch (Exception ex)
            {
                if (!exceptionPrinted)
                    print(ex);
                exceptionPrinted = true;
            }
        }
    }


    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
    public class UI_ChooseOption : UI_Control
    {

        public string[] options;
        public string[] display;

    }
}