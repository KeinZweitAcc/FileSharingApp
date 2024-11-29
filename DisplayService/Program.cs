using System.Net;
using System.Text;
using System.Text.Json;

namespace DisplayService
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            var httpListener = new HttpListener();
            httpListener.Prefixes.Add("http://localhost:5000/Display/");
            httpListener.Start();
            Console.WriteLine("API-Gateway läuft auf http://localhost:5000/Display/");

            while (true)
            {
                var context = await httpListener.GetContextAsync();
                if (context.Request.HttpMethod == "POST")
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

        private static async Task HandlePostRequest(HttpListenerContext context)
        {
            try
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var requestBody = await reader.ReadToEndAsync();
                var userList = JsonSerializer.Deserialize<List<User>>(requestBody);

                Console.WriteLine("Empfangene Benutzerliste:");
                foreach (var user in userList)
                {
                    Console.WriteLine($"ID: {user.Id}, IP: {user.Ip}, Port: {user.Port}, Username: {user.Username}");
                }

                context.Response.StatusCode = 200; // OK
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("Daten erfolgreich empfangen"));

                var filteredIdList = FilterId(userList);
                var filteredUserList = FilterUserList(userList);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Verarbeiten der Anfrage: {ex.Message}");
                context.Response.StatusCode = 500; // Internal Server Error
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("Fehler beim Verarbeiten der Anfrage"));
            }
            finally
            {
                context.Response.Close();
            }
        }

        private static List<int> FilterId(List<User> userList)
        {
            return userList.Select(x => x.Id).ToList();
        }

        private static List<string> FilterUserList(List<User> userList)
        {
            return userList.Select(x => x.Username).ToList();
        }
    }

    public class User
    {
        public int Id { get; set; }
        public string Ip { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
    }
}
