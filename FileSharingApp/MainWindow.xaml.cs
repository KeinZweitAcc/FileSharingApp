using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FileSharingApp
{
    public partial class MainWindow : Window
    {
        private ClientWebSocket _webSocket = new ClientWebSocket();
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTextBox.Text;
            var ip = IpTextBox.Text;
            var port = int.Parse(PortTextBox.Text);

            var clientInfo = new
            {
                UserName = username,
                Ip = ip,
                Port = port
            };

            var serializedClient = JsonSerializer.Serialize(clientInfo);
            var message = $"clientRegistration;{serializedClient}";

            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await _webSocket.ConnectAsync(new Uri($"ws://localhost:5000/login/"), _cancellationTokenSource.Token);
                await SendMessageAsync(message);
                _ = ReceiveMessagesAsync();

                LoginButton.IsEnabled = false;
                LogoutButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error connecting to server: {ex.Message}");
            }
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client logout", CancellationToken.None);
                _webSocket.Dispose();
                _webSocket = new ClientWebSocket();

                LoginButton.IsEnabled = true;
                LogoutButton.IsEnabled = false;
                UserListBox.Items.Clear();
            }
        }

        private async Task SendMessageAsync(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true,
                CancellationToken.None);
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[1024 * 4];

            while (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var userList = JsonSerializer.Deserialize<List<User>>(message);

                    Dispatcher.Invoke(() =>
                    {
                        UserListBox.Items.Clear();
                        if (userList != null)
                        {
                            foreach (var user in userList)
                            {
                                UserListBox.Items.Add($"Name: {user.Name}, IP: {user.IpAddress}, Port: {user.Port}");
                            }
                        }
                    });
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server",
                        CancellationToken.None);
                    _webSocket.Dispose();
                    _webSocket = new ClientWebSocket();

                    Dispatcher.Invoke(() =>
                    {
                        LoginButton.IsEnabled = true;
                        LogoutButton.IsEnabled = false;
                        UserListBox.Items.Clear();
                    });
                }
            }
        }

        public class User
        {
            public string Name { get; set; } = string.Empty;
            public string IpAddress { get; set; } = string.Empty;
            public int Port { get; set; }
        }
    }
}