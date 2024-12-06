using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace DisplayService
{
    public class Program
    {
        private static readonly ConcurrentDictionary<string, WebSocket> WebSocketConnections = new();

        static async Task Main(string[] args)
        {
            var httpListener = new HttpListener();
            httpListener.Prefixes.Add("http://localhost:5000/Display/");
            httpListener.Start();
            Console.WriteLine("API-Gateway läuft auf http://localhost:5000/Display/");

            while (true)
            {
                var context = await httpListener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    var webSocketContext = await context.AcceptWebSocketAsync(null);
                    var connectionId = Guid.NewGuid().ToString();
                    WebSocketConnections.TryAdd(connectionId, webSocketContext.WebSocket);

                    _ = HandleWebSocketConnection(connectionId, webSocketContext.WebSocket);
                }
                else if (context.Request.HttpMethod == "POST")
                {
                    await HandlePostRequest(context);
                }
                else
                {
                    context.Response.StatusCode = 405; // Method Not Allowed
                    context.Response.Close();
                }
            }
        }

        private static async Task HandleWebSocketConnection(string connectionId, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];

            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    WebSocketConnections.TryRemove(connectionId, out _);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by the WebSocket client",
                        CancellationToken.None);
                }
            }
        }

        private static async Task HandlePostRequest(HttpListenerContext context)
        {
            try
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var requestBody = await reader.ReadToEndAsync();
                var userList = JsonSerializer.Deserialize<List<User>>(requestBody);

                if (userList != null)
                {
                    Console.WriteLine("Empfangene Benutzerliste:");
                    foreach (var user in userList)
                    {
                        Console.WriteLine($"Name: {user.Name}, IP: {user.IpAddress}, Port: {user.Port}");
                    }

                    context.Response.StatusCode = 200; // OK
                    await context.Response.OutputStream.WriteAsync(
                        new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("Daten erfolgreich empfangen")));

                    var userListJson = JsonSerializer.Serialize(userList);
                    await SendUserListToAllClients(userListJson);
                }
                else
                {
                    context.Response.StatusCode = 400; // Bad Request
                    await context.Response.OutputStream.WriteAsync(
                        new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("Ungültige Benutzerdaten empfangen")));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Verarbeiten der Anfrage: {ex.Message}");
                context.Response.StatusCode = 500; // Internal Server Error
                await context.Response.OutputStream.WriteAsync(
                    new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("Fehler beim Verarbeiten der Anfrage")));
            }
            finally
            {
                context.Response.Close();
            }
        }

        public static async Task SendUserListToAllClients(string userListJson)
        {
            var buffer = Encoding.UTF8.GetBytes(userListJson);

            foreach (var webSocket in WebSocketConnections.Values)
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true,
                        CancellationToken.None);
                }
            }
        }
    }

    public class User
    {
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
    }
}