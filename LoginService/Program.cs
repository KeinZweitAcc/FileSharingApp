using LoginService.Database;
using LoginService.Database.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace LoginService
{
    internal class Program
    {
        private static readonly ConcurrentDictionary<string, WebSocket> WebSocketConnections = new();

        static async Task Main(string[] args)
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var dataContext = serviceProvider.GetRequiredService<DataContext>();

            // Ensure database is created and apply migrations
            dataContext.Database.Migrate();

            var httpListener = new HttpListener();
            httpListener.Prefixes.Add("http://localhost:5000/login/");
            httpListener.Start();
            Console.WriteLine("API-Gateway läuft auf http://localhost:5000/login/");

            while (true)
            {
                var context = await httpListener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {
                    var webSocketContext = await context.AcceptWebSocketAsync(null);
                    Console.WriteLine("Neue WebSocket-Verbindung akzeptiert");
                    _ = HandleWebSocketConnection(webSocketContext.WebSocket, dataContext);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    Console.WriteLine("Ungültige Anfrage abgelehnt.");
                }
            }
        }

        private static async Task HandleWebSocketConnection(WebSocket webSocket, DataContext dataContext)
        {
            var buffer = new byte[1024 * 4];
            string userIp = null;

            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"Nachricht empfangen: {message}");

                    var response = await ProcessMessage(message, dataContext);
                    userIp = message.Split(",")[0].Trim(); // Speichere die IP-Adresse des Benutzers

                    var encodedResponse = Encoding.UTF8.GetBytes(response);
                    await webSocket.SendAsync(new ArraySegment<byte>(encodedResponse), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("WebSocket-Verbindung geschlossen.");
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Schließen bestätigt", CancellationToken.None);

                    if (userIp != null)
                    {
                        await DeleteUserByIp(userIp, dataContext);
                    }
                }
            }
        }

        private static async Task<string> ProcessMessage(string message, DataContext dataContext)
        {
            var parts = message.Split(",")
                .Select(x => x.Trim()).ToArray();

            var Ip = parts[0];
            var Port = parts[1];
            var Username = parts[2];

            Console.WriteLine($"Gegebene Parameter wurden verarbeitet | IP: {Ip}, Port: {Port}, Username: {Username}");

            int portToUse = Convert.ToInt32(Port);

            await InsertUser(Ip, portToUse, Username, dataContext);

            return $"Gegebene Parameter wurden verarbeitet | IP: {Ip}, Port: {Port}, Username: {Username}";
        }

        private static async Task InsertUser(string Ip, int Port, string Username, DataContext dataContext)
        {
            var user = new User
            {
                Ip = Ip,
                Port = Port,
                Username = Username
            };

            dataContext.Users.Add(user);
            await dataContext.SaveChangesAsync();
        }

        private static async Task DeleteUserByIp(string ip, DataContext dataContext)
        {
            var user = await dataContext.Users.FirstOrDefaultAsync(u => u.Ip == ip);
            if (user != null)
            {
                dataContext.Users.Remove(user);
                await dataContext.SaveChangesAsync();
                Console.WriteLine($"Benutzer mit IP {ip} wurde gelöscht.");
            }
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<DataContext>(options =>
                options.UseSqlite("Data Source=UserLog.db"));
        }
    }
}