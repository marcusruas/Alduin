using System.Net.WebSockets;

namespace Alduin
{
    internal interface ICustomerServiceHandler
    {
        Task HandleAsync(HttpContext httpContext);
    }
}
