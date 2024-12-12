using LoginService.Database;
using LoginService.Database.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Net.Http;

namespace LoginService
{
    internal class Program
    {
        private static readonly ConcurrentDictionary<string, WebSocket> WebSocketConnections = new();
        private static readonly HttpClient httpClient = new HttpClient(); // Reuse HttpClient instance

        private record ConnectedUser(string UserName, string Ip, int Port);

        static async Task Main(string[] args)
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var dataContext = serviceProvider.GetRequiredService<DataContext>();

            // Ensure database is created and apply migrations
            try
            {
                dataContext.Database.Migrate();
                Console.WriteLine("Datenbankmigrationen erfolgreich angewendet.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler bei der Anwendung der Datenbankmigrationen: {ex.Message}");
            }

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
                    _ = HandleWebSocketConnection(webSocketContext.WebSocket, dataContext, context);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    Console.WriteLine("Ungültige Anfrage abgelehnt.");
                }
            }
        }

        public record RegisterData(string Name, string IpAddress, int Port);
        private static void RegisterClient(string client)
        {
            RegisterData receivedClient = JsonSerializer.Deserialize<RegisterData>(client);
            Console.WriteLine($"Client \"{receivedClient.Name}\" hat sich angemeldet. Client-Verbindung: {receivedClient.IpAddress}:{receivedClient.Port}");

            //HIER BENUTZER IN DATENBANK EINFÜGEN

            // DAS HTTP-DING OBEN WIRD NICHT MEHR BENÖTIGT, WENN WIR WEBSOCKETS VERWENDEN. IMPLEMENTIERT EIN GATEWAY, DAMIT DER CLIENT NUR EINE ADRESSE KENNEN MUSS, Z. B.: "WS://LOCALHOST:5000/FSA".
        }

        private static async Task HandleWebSocketConnection(WebSocket webSocket, DataContext dataContext, HttpListenerContext context)
        {
            var buffer = new byte[1024 * 4];

            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string receivedMessage = Encoding.UTF8.GetString(buffer, 0, result.Count); // Convert buffer to string
                    string[] message = receivedMessage.Split(';');

                    switch (message[0])
                    {
                        case "clientRegistration": RegisterClient(message[1]);
                            break;
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", CancellationToken.None);
                }

                //KEINE AHNUNG WAS IHR HIER UNTEN GEKOCHT HABT, MÜSST IHR SELBER SCHAUEN WIE UND WO IHR DAS VERWENDET. HABS VORERST AUSKOMMENTIERT.

                /*
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    // Deserialize the client registration message
                    var parts = message.Split(';');
                    if (parts.Length == 2 && parts[0] == "clientRegistration")
                    {
                        var clientInfo = JsonSerializer.Deserialize<ConnectedUser>(parts[1]);

                        if (clientInfo != null)
                        {
                            try
                            {
                                if (dataContext.Users.Any(x => x.Ip == clientInfo.Ip))
                                {
                                    throw new Exception("User schon angemeldet.");
                                }

                                await InsertUser(clientInfo, dataContext);
                                await SendUserListToDisplayService(dataContext);
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = 400;
                                context.Response.Close();
                                Console.WriteLine(
                                    $"Verbindung nicht möglich, User schon belegt oder Fehler: {ex.Message}");
                                continue;
                            }
                        }
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("WebSocket-Verbindung geschlossen.");
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Schließen bestätigt",
                        CancellationToken.None);

                    if (message != null)
                    {
                        var parts = message.Split(';');
                        if (parts.Length == 2 && parts[0] == "clientRegistration")
                        {
                            var clientInfo = JsonSerializer.Deserialize<ConnectedUser>(parts[1]);
                            if (clientInfo != null)
                            {
                                await DeleteUserByIp(clientInfo.Ip, dataContext);
                                await SendUserListToDisplayService(dataContext);
                            }
                        }
                    }
                }*/
            }
        }

        private static async Task InsertUser(ConnectedUser connectedUser, DataContext dataContext)
        {
            var user = new User
            {
                Ip = connectedUser.Ip,
                Port = connectedUser.Port,
                Username = connectedUser.UserName
            };

            try
            {
                dataContext.Users.Add(user);
                await dataContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Fehler beim Speichern des Benutzers: " + ex.Message);
            }
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
            services.AddDbContext<DataContext>(options => options.UseSqlite("Data Source=UserLog.db"));
        }

        private static async Task SendUserListToDisplayService(DataContext dataContext)
        {
            var users = await dataContext.Users.ToListAsync();
            var userList = users.Select(u => new
            {
                Name = u.Username,
                IpAddress = u.Ip,
                Port = u.Port
            }).ToList();

            string uriDisplayServiceDisplay = "http://localhost:5000/Display/";
            var jsonContent = new StringContent(JsonSerializer.Serialize(userList), Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(uriDisplayServiceDisplay, jsonContent);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Daten erfolgreich an DisplayService gesendet.");
            }
            else
            {
                Console.WriteLine($"Fehler beim Senden der Daten: {response.StatusCode}");
            }
        }
    }
}