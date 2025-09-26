using System.Collections.Generic;
using Colossal.UI.Binding;
using Game.SceneFlow;

namespace CS2M.UI
{
    public static class BindingsHelper
    {
        public static ValueBinding<T> GetValueBinding<T>(string group, string name)
        {
            IEnumerable<IBinding> bindings = GameManager.instance.userInterface.bindings.bindings;
            foreach (IBinding binding in bindings)
            {
                if (binding is ValueBinding<T> valueBinding)
                {
                    if (valueBinding.group == group && valueBinding.name == name)
                    {
                        return valueBinding;
                    }
                }
            }

            Log.Warn($"UI Binding {group}::{name} not found");
            return null;
        }
    }
}
