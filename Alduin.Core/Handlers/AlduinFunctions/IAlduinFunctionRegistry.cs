using System.Text.Json;

namespace Alduin.Core.Handlers.AlduinFunctions
{
    public interface IAlduinFunctionRegistry
    {
        public void Register<TArgs>(string name, Func<IServiceProvider, TArgs, Task<object>> handler);
        public bool TryGet(string name, out Func<IServiceProvider, JsonElement, Task<object>>? handler);
        public string MaskInformation(string input, int visibleStart = 3, int visibleEnd = 2);
    }
}
