using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;

namespace Alduin.Core.Services.CustomerService
{
    public interface ICustomerServiceHandler
    {
        Task HandleWebSocket(WebSocket clientWebSocket);
    }
}
