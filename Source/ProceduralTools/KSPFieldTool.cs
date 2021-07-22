namespace ProceduralTools
{
    public class KSPFieldTool
    {
        private const System.Reflection.BindingFlags SetFieldFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        public static void SetField(PartModule instance, BaseField field, object val)
        {
            if (instance == null || field == null || val == null)
                return;
            var pai = field.uiControlEditor?.partActionItem as UIPartActionFieldItem;
            if (pai == null)
            {
                UIPartActionController.Instance.SpawnPartActionWindow(instance.part);
                pai = field.uiControlEditor?.partActionItem as UIPartActionFieldItem;
            }
            if (pai != null &&
                pai.GetType().GetMethod("SetFieldValue", SetFieldFlags) is var _mi)
            {
                _mi.Invoke(pai, new object[] { val });
            }
        }
    }
}
