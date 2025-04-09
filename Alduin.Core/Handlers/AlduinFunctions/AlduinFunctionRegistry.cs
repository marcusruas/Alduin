using System.Text.Json;

namespace Alduin.Core.Handlers.AlduinFunctions
{
    internal class AlduinFunctionRegistry : IAlduinFunctionRegistry
    {
        private readonly Dictionary<string, Func<JsonElement, Task<object>>> _handlers = new(StringComparer.OrdinalIgnoreCase);

        public void Register<TArgs>(string name, Func<TArgs, Task<object>> handler)
        {
            _handlers[name] = async (JsonElement json) =>
            {
                var args = json.Deserialize<TArgs>()!;
                return await handler(args);
            };
        }

        public bool TryGet(string name, out Func<JsonElement, Task<object>>? handler)
        {
            return _handlers.TryGetValue(name, out handler);
        }
    }
}
