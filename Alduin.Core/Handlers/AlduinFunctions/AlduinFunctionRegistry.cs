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

        public string MaskInformation(string input, int visibleStart = 3, int visibleEnd = 2)
        {
            if (string.IsNullOrEmpty(input) || input.Length <= visibleStart + visibleEnd)
                return input;

            string start = input.Substring(0, visibleStart);
            string end = input.Substring(input.Length - visibleEnd);
            string middle = new string('*', input.Length - visibleStart - visibleEnd);

            return start + middle + end;
        }
    }
}
