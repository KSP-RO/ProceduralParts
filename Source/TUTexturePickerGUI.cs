using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Reflection;
using System.Collections;

namespace ProceduralParts
{
    internal class TextureSetContainer
    {
        public string name;
        public string title;
        public object textureSet;
        public string mainTexName;
        public string metalTexName;
        public string maskTexName;

        public TextureSetContainer(string name, string title, object textureSet)
        {
            this.name = name;
            this.title = title;
            this.textureSet = textureSet;
        }
    }
    internal class TextureSetContainerMainComparer : IEqualityComparer<TextureSetContainer>
    {
        public bool Equals(TextureSetContainer x, TextureSetContainer y)
        {
            if (ReferenceEquals(x, y)) return true;
            return x is null || y is null ? false : x.mainTexName == y.mainTexName;
        }
        public int GetHashCode(TextureSetContainer container)
        {
            if (container is null) return 0;
            return container?.mainTexName?.GetHashCode() ?? 0;
        }
    }

    internal class TextureSetContainerMetalComparer : IEqualityComparer<TextureSetContainer>
    {
        public bool Equals(TextureSetContainer x, TextureSetContainer y)
        {
            if (ReferenceEquals(x, y)) return true;
            return x is null || y is null ? false : x.metalTexName == y.metalTexName;
        }
        public int GetHashCode(TextureSetContainer container)
        {
            if (container is null) return 0;
            return container?.metalTexName?.GetHashCode() ?? 0;
        }
    }

    internal class TextureSetContainerMaskComparer : IEqualityComparer<TextureSetContainer>
    {
        public bool Equals(TextureSetContainer x, TextureSetContainer y)
        {
            if (ReferenceEquals(x, y)) return true;
            return x is null || y is null ? false : x.maskTexName == y.maskTexName;
        }
        public int GetHashCode(TextureSetContainer container)
        {
            if (container is null) return 0;
            return container?.maskTexName?.GetHashCode() ?? 0;
        }
    }

    internal class TUTexturePickerGUI : MonoBehaviour
    {
        private const int GUIWidth = 400;
        private const int GUIHeight = 500;
        private const int INDENT = 15;
        private Rect Window = new Rect(250, 100, GUIWidth, GUIHeight);
        public ProceduralPart parent;
        private static readonly Assembly TUAssembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "TexturesUnlimited")?.assembly;
        private static bool TUSetupDone = false;
        private static Type TexturesUnlimitedLoaderType, TUTextureContainerType, KSPTextureSwitchType;
        private static Type TextureSetType, TextureSetMaterialDataType;
        private static Type ShaderPropertyType, ShaderPropertyTextureType;
        private static MethodInfo TUSetRecoloringMethod;
        private static IDictionary loadedSets;
        private BaseField CurrentTextureField => parent?.part?.Modules["KSPTextureSwitch"]?.Fields["currentTextureSet"];

        private bool DictionariesInitialized = false;
        private readonly Dictionary<string, bool> MainToggles = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> MaskToggles = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> MetalToggles = new Dictionary<string, bool>();
        private readonly Dictionary<string, TextureSetContainer> texturesDict = new Dictionary<string, TextureSetContainer>();
        private static readonly TextureSetContainerMainComparer MainsComparer = new TextureSetContainerMainComparer();
        private static readonly TextureSetContainerMetalComparer MetalsComparer = new TextureSetContainerMetalComparer();
        private static readonly TextureSetContainerMaskComparer MasksComparer = new TextureSetContainerMaskComparer();
        private Vector2 scrollPos = new Vector2();

        public void ShowForPart(ProceduralPart parent)
        {
            this.parent = parent;
            SetupTUReflection();
        }

        public void OnGUI()
        {
            if (parent.showTUPickerGUI)
            {
                if (!DictionariesInitialized)
                {
                    BuildDictionaries();
                    BuildToggles();
                    DictionariesInitialized = true;
                }
                Window = GUILayout.Window(GetHashCode(), Window, GUIDisplay, "#PP_Picker_TUTextureSelector", GUILayout.Width(GUIWidth), GUILayout.Height(GUIHeight));
            }
        }

        private void BuildToggles()
        {
            MainToggles.Clear();
            MaskToggles.Clear();
            MetalToggles.Clear();
            foreach (var x in texturesDict)
            {
                if (!MainToggles.ContainsKey(x.Value.mainTexName))
                    MainToggles.Add(x.Value.mainTexName, false);
                if (!MetalToggles.ContainsKey(x.Value.metalTexName))
                    MetalToggles.Add(x.Value.metalTexName, false);
                if (!MaskToggles.ContainsKey(x.Value.maskTexName))
                    MaskToggles.Add(x.Value.maskTexName, false);
            }
        }
        private void BuildDictionaries()
        {
            texturesDict.Clear();
            // We don't need to enumerate all of loadedSets, we really want to enumerate the KSPTextureSwitch elements.
            if (CurrentTextureField is BaseField)
            {
                foreach (string name in (CurrentTextureField?.uiControlEditor as UI_ChooseOption)?.options)
                {
                    if (loadedSets.Contains(name) &&
                        loadedSets[name] is var textureSet &&
                        textureSet?.GetType() == TextureSetType &&
                        TextureSetType.GetField("name")?.GetValue(textureSet) is string tsName &&
                        TextureSetType.GetField("title")?.GetValue(textureSet) is string title &&
                        TextureSetType.GetField("textureData")?.GetValue(textureSet) is Array tsmdArray)
                    {
                        TextureSetContainer container = new TextureSetContainer(tsName, title, textureSet);
                        foreach (var tsmd in tsmdArray)
                        {
                            foreach (var prop in TextureSetMaterialDataType.GetField("shaderProperties").GetValue(tsmd) as Array)
                            {
                                string shaderName = ShaderPropertyType.GetField("name")?.GetValue(prop) as string;
                                if (shaderName == "_MainTex")
                                    container.mainTexName = ShaderPropertyTextureType.GetField("textureName")?.GetValue(prop) as string;
                                else if (shaderName == "_MaskTex")
                                    container.maskTexName = ShaderPropertyTextureType.GetField("textureName")?.GetValue(prop) as string;
                                else if (shaderName == "_MetallicGlossMap")
                                    container.metalTexName = ShaderPropertyTextureType.GetField("textureName")?.GetValue(prop) as string;
                            }
                        }
                        texturesDict.Add(container.name, container);
                    }
                }
            }
        }

        internal void GUIDisplay(int windowID)
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                scrollPos = GUILayout.BeginScrollView(scrollPos);
                foreach (TextureSetContainer main in texturesDict.Values.Distinct(MainsComparer))
                {
                    DisplayToggles(MainToggles, main.mainTexName, INDENT);
                    if (!MainToggles.ContainsKey(main.mainTexName) || MainToggles[main.mainTexName])
                    {
                        foreach (TextureSetContainer metal in texturesDict.Values.Where(x => x.mainTexName == main.mainTexName).Distinct(MetalsComparer))
                        {
                            DisplayToggles(MetalToggles, metal.metalTexName, INDENT * 2);
                            if (!MetalToggles.ContainsKey(metal.metalTexName) || MetalToggles[metal.metalTexName])
                            {
                                foreach (TextureSetContainer mask in texturesDict.Values.Where(x => x.mainTexName == main.mainTexName && x.metalTexName == metal.metalTexName).Distinct(MasksComparer))
                                {
                                    TextureButton($"{mask.name}", INDENT * 3);
                                }
                            }
                        }
                    }
                }
                GUILayout.EndScrollView();
            }

            if (GUILayout.Button("#PP_Close"))
            {
                Destroy(this);
                parent.showTUPickerGUI = false;
                parent.texturePickerGUI = null;
            }

            GUI.DragWindow();
        }

        private void DisplayToggles(Dictionary<string,bool> ToggleDict, string key, float gap)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(gap);
            if (ToggleDict.ContainsKey(key))
                ToggleDict[key] = GUILayout.Toggle(ToggleDict[key], $"{key}");
            else
                GUILayout.Label($"[x] {key}");
            GUILayout.EndHorizontal();
        }

        private void TextureButton(string sTarget, float gap)
        {
            GUILayout.BeginHorizontal();
            if (gap > 0)
                GUILayout.Space(gap);
            if (GUILayout.Button(sTarget))
            {
                PartModule kspTextureSwitchPM = parent.part.Modules["KSPTextureSwitch"];
                string prev = CurrentTextureField.GetValue(CurrentTextureField.host) as string;
                var tuContainer = KSPTextureSwitchType.GetField("textureSets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(kspTextureSwitchPM);
                var recolorData = TUTextureContainerType.GetField("customColors", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tuContainer) as Array;
                CurrentTextureField.SetValue(sTarget, CurrentTextureField.host);
                CurrentTextureField.uiControlEditor.onFieldChanged.Invoke(CurrentTextureField, prev);
                TUSetRecoloringMethod.Invoke(kspTextureSwitchPM, new object[] { string.Empty, recolorData });
                MonoUtilities.RefreshPartContextWindow(parent.part);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        public static void SetupTUReflection()
        {
            if (TUSetupDone) return;

            if (TUAssembly is Assembly)
            {
                TexturesUnlimitedLoaderType = TUAssembly.GetType("KSPShaderTools.TexturesUnlimitedLoader");
                TUTextureContainerType = TUAssembly.GetType("KSPShaderTools.TextureSetContainer");
                KSPTextureSwitchType = TUAssembly.GetType("KSPShaderTools.KSPTextureSwitch");
                TextureSetType = TUAssembly.GetType("KSPShaderTools.TextureSet");
                TextureSetMaterialDataType = TUAssembly.GetType("KSPShaderTools.TextureSetMaterialData");
                ShaderPropertyType = TUAssembly.GetType("KSPShaderTools.ShaderProperty");
                ShaderPropertyTextureType = TUAssembly.GetType("KSPShaderTools.ShaderPropertyTexture");
                loadedSets = TexturesUnlimitedLoaderType.GetField("loadedTextureSets", BindingFlags.Public | BindingFlags.Static).GetValue(null) as IDictionary;
                TUSetRecoloringMethod = KSPTextureSwitchType.GetMethod("setSectionColors");
            }

            TUSetupDone = true;
        }
    }
}
